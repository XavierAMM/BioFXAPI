using Microsoft.AspNetCore.Authentication.JwtBearer;
using BioFXAPI.Models;
using BioFXAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly PasswordService _passwordService;
        private readonly IConfiguration _configuration;

        public AuthController(IConfiguration configuration, PasswordService passwordService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _passwordService = passwordService;
            _configuration = configuration;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
                return BadRequest(new { message = "Se necesita el correo y la contraseña." });

            try
            {
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                var loginResult = await ValidateLoginAttemptAsync(request.Email, request.Password, ipAddress);

                return loginResult switch
                {
                    LoginResult.Success => await HandleSuccessfulLoginAsync(request.Email, ipAddress),
                    LoginResult.LockedOut => StatusCode((int)HttpStatusCode.Forbidden,
                        new { message = "La cuenta está temporalmente bloqueada. Intente nuevamente después." }),
                    LoginResult.EmailNotConfirmed => Unauthorized(new { message = "Por favor, confirme su correo antes de iniciar sesión." }),
                    LoginResult.AccountInactive => Unauthorized(new { message = "Cuenta inactiva. Contacte al administrador." }),
                    _ => Unauthorized(new { message = "Correo o contraseña no válidas." })
                };
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error interno", details = ex.Message });
            }
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("biofx_auth", new CookieOptions
            {
                Path = "/",
                Secure = true,
                //SameSite = SameSiteMode.Lax
                SameSite = SameSiteMode.None
            });
            return Ok(new { Message = "Logout exitoso." });
        }

        #region Métodos Privados de Helper
        private async Task<LoginResult> ValidateLoginAttemptAsync(string email, string password, string ipAddress)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var userData = await GetUserLoginDataAsync(email, connection);
            if (!userData.UserFound) return LoginResult.InvalidCredentials;

            // Verificar si la cuenta está inactiva
            if (!userData.Activo)
                return LoginResult.AccountInactive;

            // Verificar si el bloqueo ya expiró
            if (userData.LockoutEnd.HasValue && userData.LockoutEnd.Value <= DateTime.UtcNow)
                await ResetFailedAttemptsAsync(email, connection);

            // Verificar si la cuenta está bloqueada
            if (_passwordService.IsAccountLocked(userData.LockoutEnd))
                return LoginResult.LockedOut;

            // Verificar si el email está confirmado
            if (!userData.EmailConfirmed)
                return LoginResult.EmailNotConfirmed;

            // Verificar contraseña
            return _passwordService.VerifyPassword(password, userData.StoredHash)
                ? await HandleSuccessfulLoginAsync(userData.UserId, ipAddress, connection)
                : await HandleFailedLoginAsync(email, userData.FailedAttempts, connection);
        }

        private async Task<(bool UserFound, int UserId, string StoredHash, int FailedAttempts, DateTime? LockoutEnd, bool EmailConfirmed, bool Activo, bool EsAdministrador)>
        GetUserLoginDataAsync(string email, SqlConnection connection)
        {
            var query = @"SELECT id, contrasenaHash, intentosFallidos, bloqueadoHasta, emailConfirmado, Activo, esAdministrador
                  FROM Usuario WHERE email = @Email AND Activo = 1";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Email", email);

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return (false, 0, "", 0, null, false, false, false);

            return (true,
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                    reader.GetBoolean(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6));
        }

        private async Task ResetFailedAttemptsAsync(string email, SqlConnection connection)
        {
            await ExecuteNonQueryAsync(
                @"UPDATE Usuario SET intentosFallidos = 0, bloqueadoHasta = NULL, actualizadoEl = GETUTCDATE() 
                  WHERE email = @Email",
                new { Email = email }, connection);
        }

        private async Task<LoginResult> HandleSuccessfulLoginAsync(int userId, string ipAddress, SqlConnection connection)
        {
            await ExecuteNonQueryAsync(
                @"UPDATE Usuario SET intentosFallidos = 0, bloqueadoHasta = NULL, ultimoLogin = GETUTCDATE(), 
                  ultimaIpLogin = @IpAddress, actualizadoEl = GETUTCDATE() WHERE id = @UserId",
                new { UserId = userId, IpAddress = ipAddress }, connection);

            return LoginResult.Success;
        }

        private async Task<LoginResult> HandleFailedLoginAsync(string email, int currentFailedAttempts, SqlConnection connection)
        {
            var newFailedAttempts = currentFailedAttempts + 1;
            var lockoutEnd = _passwordService.CalculateLockoutEnd(newFailedAttempts);

            await ExecuteNonQueryAsync(
                @"UPDATE Usuario SET intentosFallidos = @Attempts, bloqueadoHasta = @LockoutEnd, actualizadoEl = GETUTCDATE() 
                  WHERE email = @Email",
                new { Attempts = newFailedAttempts, LockoutEnd = lockoutEnd, Email = email }, connection);

            return LoginResult.InvalidCredentials;
        }

        private async Task<IActionResult> HandleSuccessfulLoginAsync(string email, string ipAddress)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var userData = await GetUserLoginDataAsync(email, connection);
            if (!userData.UserFound)
                return Unauthorized(new { message = "Usuario no encontrado." });

            // Obtener la fecha de creación del usuario
            var fechaCreacion = await GetFechaCreacionUsuarioAsync(userData.UserId, connection);

            // Actualizar último login
            await ExecuteNonQueryAsync(
                @"UPDATE Usuario SET intentosFallidos = 0, bloqueadoHasta = NULL, ultimoLogin = GETUTCDATE(), 
         ultimaIpLogin = @IpAddress, actualizadoEl = GETUTCDATE() WHERE id = @UserId",
                new { UserId = userData.UserId, IpAddress = ipAddress }, connection);

            // Obtener datos de la persona
            var personaData = await GetPersonaDataAsync(userData.UserId, connection);

            // Generar token JWT
            var token = GenerateJwtToken(new User { Id = userData.UserId, Email = email });

            Response.Cookies.Append(
                "biofx_auth",
                token,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    //SameSite = SameSiteMode.Lax,
                    SameSite = SameSiteMode.None,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddHours(24)
                }
            );


            return Ok(new LoginResponse
            {
                Message = "Inicio de sesión satisfactorio.",
                UserId = userData.UserId,
                Email = email,
                //Token = token,
                Persona = personaData,
                FechaCreacion = fechaCreacion,
                EsAdministrador = userData.EsAdministrador
            });
        }

        private async Task<DateTime> GetFechaCreacionUsuarioAsync(int userId, SqlConnection connection)
        {
            var query = "SELECT creadoEl FROM Usuario WHERE id = @UserId";
            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@UserId", userId);

            return (DateTime)await command.ExecuteScalarAsync();
        }
        private async Task<PersonaInfo> GetPersonaDataAsync(int usuarioId, SqlConnection connection)
        {
            var query = @"SELECT Nombre, Apellido, Telefono 
                  FROM Persona WHERE UsuarioId = @UsuarioId AND Activo = 1";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@UsuarioId", usuarioId);

            using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new PersonaInfo
            {
                Nombre = reader.GetString(0),
                Apellido = reader.GetString(1),
                Telefono = reader.IsDBNull(2) ? null : reader.GetString(2)
            };
        }

        private string GenerateJwtToken(User user)
        {
            try
            {
                var jwtSettings = _configuration.GetSection("Jwt");
                var secretKey = jwtSettings["Secret"];

                if (string.IsNullOrEmpty(secretKey) || secretKey.Length < 32)
                {
                    throw new InvalidOperationException("JWT Secret no configurado o muy corto");
                }

                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(secretKey);

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Email, user.Email),
                        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
                    }),
                    Expires = DateTime.UtcNow.AddHours(24),
                    Issuer = jwtSettings["Issuer"],
                    Audience = jwtSettings["Audience"],
                    SigningCredentials = new SigningCredentials(
                        new SymmetricSecurityKey(key),
                        SecurityAlgorithms.HmacSha256Signature)
                };

                var token = tokenHandler.CreateToken(tokenDescriptor);
                return tokenHandler.WriteToken(token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generando JWT: {ex.Message}");
                throw;
            }
        }

        private async Task ExecuteNonQueryAsync(string query, object parameters, SqlConnection connection)
        {
            using var command = new SqlCommand(query, connection);
            foreach (var prop in parameters.GetType().GetProperties())
                command.Parameters.AddWithValue($"@{prop.Name}", prop.GetValue(parameters) ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }
        #endregion
    }

    public enum LoginResult { Success, InvalidCredentials, LockedOut, EmailNotConfirmed, AccountInactive }
    public class User { public int Id { get; set; } public string Email { get; set; } }
}