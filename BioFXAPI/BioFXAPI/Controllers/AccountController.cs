using BioFXAPI.Models;
using BioFXAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Security.Cryptography;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly PasswordService _passwordService;
        private readonly EmailService _emailService;
        private readonly ILogger<AccountController> _logger;
        private readonly string _frontendBaseUrl;
        private readonly EmailVerificationService _emailVerification;

        public AccountController(IConfiguration configuration, PasswordService passwordService, EmailService emailService, ILogger<AccountController> logger, EmailVerificationService emailVerification)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _passwordService = passwordService;
            _emailService = emailService;
            _logger = logger;
            _emailVerification = emailVerification;
            _frontendBaseUrl = configuration["Frontend:BaseUrl"]
                ?? throw new InvalidOperationException("Frontend:BaseUrl no está configurado en appsettings.");
        }

        public record TestEmailRequest(string To, string Subject, string Body);

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

                var normEmail = request.Email.Trim().ToLowerInvariant();

                if (await EmailExists(normEmail, connection))
                    return Conflict(new { message = "El correo ya se encuentra registrado." });

                var abandoned = await TryGetAbandonedUserAsync(normEmail, connection);

                // Crear usuario, persona y datos de facturación automáticamente
                using var transaction = connection.BeginTransaction();
                try
                {
                    var hashedPassword = _passwordService.HashPassword(request.Password);
                    var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

                    int userId;
                    bool wasReactivated = false;

                    if (abandoned.Found)
                    {
                        userId = abandoned.UserId;
                        wasReactivated = true;

                        await ReactivateAbandonedUserAsync(userId, hashedPassword, connection, transaction);
                    }
                    else
                    {
                        userId = await InsertUserAsync(normEmail, hashedPassword, connection, transaction);

                        await InsertPersonaAsync(userId, request.Nombre, request.Apellido, request.Telefono, connection, transaction);
                        await InsertDatosFacturacionAsync(userId, connection, transaction);
                    }

                    await _emailVerification.IssueNewVerificationTokenAsync(userId: userId, email: normEmail, ip: ip, connection: connection, tx: transaction);

                    transaction.Commit();

                    return Ok(new
                    {
                        message = wasReactivated
                            ? "Se reactivó tu registro. Por favor, revisa tu correo para confirmar."
                            : "Registro satisfactorio. Por favor, verifique su correo para activar su cuenta.",
                        userId,
                        email = normEmail,
                        emailSent = true
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

        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token, [FromQuery] string email)
        {
            var url = _frontendBaseUrl.EndsWith("/") ? _frontendBaseUrl : _frontendBaseUrl + "/";

            try
            {
                if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
                    return Redirect(url + "verify-email/verify-email.html?status=invalid");

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var result = await TryGetValidEmailTokenAsync(email, token, connection);
                if (!result.IsValid)
                    return Redirect(url + "verify-email/verify-email.html?status=invalid");

                using var tx = connection.BeginTransaction();
                try
                {
                    await VerifyUserEmailAsync(result.UserId, result.TokenId, connection, tx);
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }

                return Redirect(url + "verify-email/verify-email.html?status=ok");
            }
            catch
            {
                return Redirect(url + "verify-email/verify-email.html?status=error");
            }
        }

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

        [Authorize]
        [EnableRateLimiting("StrictPasswordOps")]
        [HttpPost("test-email")]
        public async Task<IActionResult> TestEmail([FromBody] TestEmailRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.To))
                return BadRequest(new { message = "Falta 'To'." });

            try
            {
                await _emailService.SendSimpleEmailAsync(req.To,
                    string.IsNullOrWhiteSpace(req.Subject) ? "Prueba BioFX" : req.Subject,
                    string.IsNullOrWhiteSpace(req.Body) ? "Hola" : req.Body);

                return Ok(new { message = "Correo de prueba enviado." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo envío de prueba a {To}", req.To);
                return StatusCode(500, new { error = "Fallo SMTP", details = ex.Message });
            }
        }

        [HttpPost("resend-verification")]
        public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request)
        {
            // Respuesta genérica para evitar enumeración de correos
            object GenericOk(string message, string action) => new { message, action };

            if (string.IsNullOrWhiteSpace(request?.Email))
                return BadRequest(new { message = "Se necesita el correo." });

            var email = request.Email.Trim().ToLowerInvariant();
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // 1) Buscar usuario (sin enumeración)
                var userQuery = @"SELECT TOP 1 id, emailConfirmado, Activo FROM dbo.Usuario WHERE email = @Email";
                using var userCmd = new SqlCommand(userQuery, connection);
                userCmd.Parameters.AddWithValue("@Email", email);

                int userId;
                bool emailConfirmado;
                bool activo;

                using (var reader = await userCmd.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                        return Ok(GenericOk("Si el correo existe y no está verificado, enviamos un enlace.", "GENERIC"));

                    userId = reader.GetInt32(0);
                    emailConfirmado = reader.GetBoolean(1);
                    activo = reader.GetBoolean(2);
                }

                if (!activo)
                    return Ok(GenericOk("Si el correo existe y no está verificado, enviamos un enlace.", "GENERIC"));

                if (emailConfirmado)
                    return Ok(new { message = "El correo ya está verificado.", action = "ALREADY_VERIFIED" });

                // 2) Delegar regla + DB + envío al EmailVerificationService (usa appsettings)
                var outcome = await _emailVerification.EvaluateAndMaybeResendAsync(
                    userId: userId,
                    email: email,
                    ip: ip,
                    connection: connection,
                    autoResendWhenAllowed: true);

                // 3) Respuesta consistente para UI
                // outcome.action será "EMAIL_NOT_CONFIRMED" y outcome.resendAction indica el detalle
                return Ok(outcome);
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        private async Task InsertDatosFacturacionAsync(int usuarioId, SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"INSERT INTO DatosFacturacion (UsuarioId, Activo, CreadoEl, ActualizadoEl)
                  VALUES (@UsuarioId, 1, GETUTCDATE(), GETUTCDATE())";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@UsuarioId", usuarioId);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<int> InsertUserAsync(string email, string hashedPassword, SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"INSERT INTO Usuario (email, contrasenaHash, creadoEl, emailConfirmado, Activo)
                  OUTPUT INSERTED.id
                  VALUES (@Email, @PasswordHash, GETUTCDATE(), 0, 1)";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@Email", email);
            command.Parameters.AddWithValue("@PasswordHash", hashedPassword);

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
        
        private async Task<bool> EmailExists(string email, SqlConnection connection)
        {
            var query = "SELECT 1 FROM Usuario WHERE email = @Email and Activo = 1";
            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Email", email);
            return (await command.ExecuteScalarAsync()) != null;
        }

        private async Task<(bool Found, int UserId)> TryGetAbandonedUserAsync(string email, SqlConnection connection)
        {
            var query = @"
                SELECT TOP 1 id
                FROM dbo.Usuario
                WHERE email = @Email
                  AND Activo = 0
                  AND emailConfirmado = 0
                  AND creadoEl < DATEADD(day, -7, GETUTCDATE());";

            using var cmd = new SqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@Email", email);
            var obj = await cmd.ExecuteScalarAsync();
            if (obj is int id) return (true, id);
            return (false, 0);
        }

        private async Task ReactivateAbandonedUserAsync(int userId, string newPasswordHash, SqlConnection connection, SqlTransaction tx)
        {
            var query = @"
                UPDATE dbo.Usuario
                SET Activo = 1,
                    contrasenaHash = @Hash,
                    intentosFallidos = 0,
                    bloqueadoHasta = NULL,
                    actualizadoEl = GETUTCDATE()
                WHERE id = @UserId
                  AND Activo = 0
                  AND emailConfirmado = 0;";

            using var cmd = new SqlCommand(query, connection, tx);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@Hash", newPasswordHash);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<(bool IsValid, int UserId, int TokenId)> TryGetValidEmailTokenAsync(string email,string rawToken,SqlConnection connection)
        {
            var normEmail = email.Trim().ToLowerInvariant();
            var tokenHash = _emailVerification.ComputeTokenHash(rawToken);

            var query = @"
                SELECT TOP 1 t.Id AS TokenId, u.id AS UserId
                FROM dbo.EmailVerificationTokens t
                JOIN dbo.Usuario u ON u.id = t.UsuarioId
                WHERE u.email = @Email
                  AND u.Activo = 1
                  AND u.emailConfirmado = 0
                  AND t.Activo = 1
                  AND t.UsadoEl IS NULL
                  AND t.RevocadoEl IS NULL
                  AND t.TokenHash = @TokenHash
                  AND t.ExpiraEl > GETUTCDATE()
                ORDER BY t.CreadoEl DESC;";

            using var cmd = new SqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@Email", normEmail);
            cmd.Parameters.Add("@TokenHash", System.Data.SqlDbType.VarBinary, 32).Value = tokenHash;

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return (false, 0, 0);

            var tokenId = reader.GetInt32(0);
            var userId = reader.GetInt32(1);
            return (true, userId, tokenId);
        }

        private async Task VerifyUserEmailAsync(int userId, int tokenId, SqlConnection connection, SqlTransaction tx)
        {
            // 1) Confirmar email del usuario
            var confirmUserQuery = @"
                UPDATE dbo.Usuario
                SET emailConfirmado = 1,
                    actualizadoEl = GETUTCDATE()
                WHERE id = @UserId
                  AND emailConfirmado = 0;";

            using (var cmd = new SqlCommand(confirmUserQuery, connection, tx))
            {
                cmd.Parameters.AddWithValue("@UserId", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            // 2) Marcar token como usado y desactivarlo
            var markUsedQuery = @"
                UPDATE dbo.EmailVerificationTokens
                SET UsadoEl = GETUTCDATE(),
                    Activo = 0,
                    ActualizadoEl = GETUTCDATE()
                WHERE Id = @TokenId
                  AND Activo = 1
                  AND UsadoEl IS NULL;";

            using (var cmd = new SqlCommand(markUsedQuery, connection, tx))
            {
                cmd.Parameters.AddWithValue("@TokenId", tokenId);
                await cmd.ExecuteNonQueryAsync();
            }

            // 3) Invalidar cualquier otro token pendiente del usuario
            await _emailVerification.InvalidatePendingTokensAsync(userId, connection, tx);

        }

        private static string GenerateSixDigitCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(0, 6)
                .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
        }        

        public record ResendVerificationRequest(string Email);

    }
}