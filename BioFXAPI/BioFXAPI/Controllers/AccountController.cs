using BioFXAPI.Models;
using BioFXAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Security.Claims;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly PasswordService _passwordService;
        private readonly EmailService _emailService;

        public AccountController(IConfiguration configuration, PasswordService passwordService, EmailService emailService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _passwordService = passwordService;
            _emailService = emailService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
                return BadRequest(new { message = "Se necesita el correo y la contraseña." });

            if (!_passwordService.IsPasswordStrong(request.Password))
                return BadRequest(new { message = "La contraseña no cumple con los requisitos de seguridad." });

            // VALIDAR DATOS PERSONALES
            if (string.IsNullOrEmpty(request.Nombre) || string.IsNullOrEmpty(request.Apellido))
                return BadRequest(new { message = "Nombre y apellido son requeridos." });

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                if (await EmailExists(request.Email, connection))
                    return Conflict(new { message = "El correo ya se encuentra registrado." });

                // Crear usuario, persona y datos de facturación automáticamente
                using var transaction = connection.BeginTransaction();
                try
                {
                    var hashedPassword = _passwordService.HashPassword(request.Password);
                    var emailToken = GenerateSecureToken();

                    // 1. CREAR USUARIO
                    var userId = await InsertUserAsync(request.Email, hashedPassword, emailToken, connection, transaction);

                    // 2. CREAR PERSONA
                    await InsertPersonaAsync(userId, request.Nombre, request.Apellido, request.Telefono, connection, transaction);

                    // 3. CREAR DATOS DE FACTURACIÓN (vacío)
                    await InsertDatosFacturacionAsync(userId, connection, transaction);

                    transaction.Commit();

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _emailService.SendVerificationEmailAsync(request.Email, emailToken);
                        }
                        catch (Exception ex)
                        {
                            
                        }
                    });

                    return Ok(new
                    {
                        message = "Registro satisfactorio. Por favor, verifique su correo para activar su cuenta.",
                        userId,
                        email = request.Email
                    });
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        #region Métodos Privados
        private async Task InsertDatosFacturacionAsync(int usuarioId, SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"INSERT INTO DatosFacturacion (UsuarioId, Activo, CreadoEl, ActualizadoEl)
                  VALUES (@UsuarioId, 1, GETUTCDATE(), GETUTCDATE())";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@UsuarioId", usuarioId);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<int> InsertUserAsync(string email, string hashedPassword, string emailToken, SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"INSERT INTO Usuario (email, contrasenaHash, creadoEl, emailConfirmado, 
                  tokenVerificacionEmail, expiracionTokenVerificacion, Activo) 
                  OUTPUT INSERTED.id 
                  VALUES (@Email, @PasswordHash, GETUTCDATE(), 0, @Token, DATEADD(hour, 24, GETUTCDATE()), 1)";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@Email", email);
            command.Parameters.AddWithValue("@PasswordHash", hashedPassword);
            command.Parameters.AddWithValue("@Token", emailToken);

            return (int)await command.ExecuteScalarAsync();
        }

        private async Task InsertPersonaAsync(int usuarioId, string nombre, string apellido, string telefono, SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"INSERT INTO Persona (Nombre, Apellido, Telefono, UsuarioId, Activo, CreadoEl, ActualizadoEl)
                  VALUES (@Nombre, @Apellido, @Telefono, @UsuarioId, 1, GETUTCDATE(), GETUTCDATE())";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@Nombre", nombre);
            command.Parameters.AddWithValue("@Apellido", apellido);
            command.Parameters.AddWithValue("@Telefono", telefono ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@UsuarioId", usuarioId);

            await command.ExecuteNonQueryAsync();
        }
        #endregion

        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token, [FromQuery] string email)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                if (!await IsValidEmailToken(email, token, connection))
                    return BadRequest(new { message = "Token de verificación inválido o expirado." });

                VerifyUserEmail(email, connection);
                return Ok(new { message = "Correo verificado satisfactoriamente." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos.", details = ex.Message });
            }
        }

        #region Métodos Privados
        private async Task<bool> EmailExists(string email, SqlConnection connection)
        {
            var query = "SELECT 1 FROM Usuario WHERE email = @Email AND Activo = 1";
            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Email", email);
            return (await command.ExecuteScalarAsync()) != null;
        }

        private async Task<bool> IsValidEmailToken(string email, string token, SqlConnection connection)
        {
            var query = "SELECT expiracionTokenVerificacion FROM Usuario WHERE email = @Email AND tokenVerificacionEmail = @Token";
            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Email", email);
            command.Parameters.AddWithValue("@Token", token);

            var expiration = await command.ExecuteScalarAsync() as DateTime?;
            return expiration.HasValue && expiration.Value > DateTime.UtcNow;
        }

        private void VerifyUserEmail(string email, SqlConnection connection)
        {
            ExecuteNonQuery(
                "UPDATE Usuario SET emailConfirmado = 1, tokenVerificacionEmail = NULL, expiracionTokenVerificacion = NULL WHERE email = @Email",
                new { Email = email }, connection);
        }

        private async Task SendVerificationEmail(string email, string token)
        {
            try
            {
                await _emailService.SendVerificationEmailAsync(email, token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al enviar correo: {ex.Message}");
            }
        }

        private string GenerateSecureToken()
        {
            var tokenData = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(tokenData);
            return Convert.ToBase64String(tokenData).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        private string GenerateSixDigitCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 6)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private void ExecuteNonQuery(string query, object parameters, SqlConnection connection)
        {
            using var command = new SqlCommand(query, connection);
            foreach (var prop in parameters.GetType().GetProperties())
                command.Parameters.AddWithValue($"@{prop.Name}", prop.GetValue(parameters) ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        #endregion

        [Authorize]
        [HttpPost("change-email-request")]
        public async Task<IActionResult> ChangeEmailRequest([FromBody] ChangeEmailRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Verificar si el nuevo email ya existe
            if (await EmailExists(request.NuevoEmail, connection))
                return BadRequest(new { message = "El correo ya está en uso." });

            var token = GenerateSixDigitCode();
            var expiration = DateTime.UtcNow.AddHours(1);

            // Guardar token y nuevo email pendiente
            var query = @"UPDATE Usuario 
                  SET nuevoEmailPendiente = @NuevoEmail,
                      tokenCambioEmail = @Token,
                      expiracionTokenCambioEmail = @Expiration
                  WHERE id = @UserId";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@NuevoEmail", request.NuevoEmail);
            command.Parameters.AddWithValue("@Token", token);
            command.Parameters.AddWithValue("@Expiration", expiration);
            command.Parameters.AddWithValue("@UserId", userId);

            await command.ExecuteNonQueryAsync();

            // Enviar correo de verificación
            await _emailService.SendEmailChangeConfirmationAsync(request.NuevoEmail, token);

            return Ok(new { message = "Se ha enviado un código de verificación al nuevo correo." });
        }

        [HttpPost("verify-email-change")]
        public async Task<IActionResult> VerifyEmailChange([FromBody] VerifyEmailChangeRequest request)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Verificar token válido
            var userQuery = @"SELECT id FROM Usuario 
                      WHERE nuevoEmailPendiente = @NuevoEmail 
                      AND tokenCambioEmail = @Token 
                      AND expiracionTokenCambioEmail > @CurrentTime";

            using var userCmd = new SqlCommand(userQuery, connection);
            userCmd.Parameters.AddWithValue("@NuevoEmail", request.NuevoEmail);
            userCmd.Parameters.AddWithValue("@Token", request.Token);
            userCmd.Parameters.AddWithValue("@CurrentTime", DateTime.UtcNow);

            var userId = await userCmd.ExecuteScalarAsync() as int?;

            if (!userId.HasValue)
                return BadRequest(new { message = "Token inválido o expirado." });

            // Actualizar email
            var updateQuery = @"UPDATE Usuario 
                        SET email = @NuevoEmail,
                            nuevoEmailPendiente = NULL,
                            tokenCambioEmail = NULL,
                            expiracionTokenCambioEmail = NULL
                        WHERE id = @UserId";

            using var updateCmd = new SqlCommand(updateQuery, connection);
            updateCmd.Parameters.AddWithValue("@NuevoEmail", request.NuevoEmail);
            updateCmd.Parameters.AddWithValue("@UserId", userId.Value);

            await updateCmd.ExecuteNonQueryAsync();

            return Ok(new { message = "Correo electrónico actualizado exitosamente." });
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

            if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
                return BadRequest(new { message = "La contraseña actual y la nueva contraseña son requeridas." });

            if (!_passwordService.IsPasswordStrong(request.NewPassword))
                return BadRequest(new { message = "La nueva contraseña no cumple con los requisitos de seguridad." });

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Obtener el hash de la contraseña actual del usuario
                var query = "SELECT contrasenaHash FROM Usuario WHERE id = @UserId AND Activo = 1";
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UserId", userId);

                var currentHash = await command.ExecuteScalarAsync() as string;

                if (currentHash == null)
                    return NotFound(new { message = "Usuario no encontrado." });

                // Verificar la contraseña actual
                if (!_passwordService.VerifyPassword(request.CurrentPassword, currentHash))
                    return BadRequest(new { message = "La contraseña actual es incorrecta." });

                // Hash de la nueva contraseña
                var newHash = _passwordService.HashPassword(request.NewPassword);

                // Actualizar la contraseña
                var updateQuery = @"UPDATE Usuario 
                            SET contrasenaHash = @NewHash, 
                                fechaActualizacionContrasena = GETUTCDATE(), 
                                actualizadoEl = GETUTCDATE() 
                            WHERE id = @UserId";

                using var updateCommand = new SqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@NewHash", newHash);
                updateCommand.Parameters.AddWithValue("@UserId", userId);

                await updateCommand.ExecuteNonQueryAsync();

                return Ok(new { message = "Contraseña actualizada exitosamente." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

    }
}