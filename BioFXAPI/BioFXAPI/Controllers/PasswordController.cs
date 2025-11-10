using BioFXAPI.Models;
using BioFXAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Data.SqlClient;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PasswordController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly PasswordService _passwordService;
        private readonly EmailService _emailService;

        public PasswordController(IConfiguration configuration, PasswordService passwordService, EmailService emailService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _passwordService = passwordService;
            _emailService = emailService;
        }

        [EnableRateLimiting("StrictPasswordOps")]
        [HttpPost("forgot")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            if (string.IsNullOrEmpty(request.Email))
                return BadRequest(new { message = "El correo electrónico es requerido." });

            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var emailConfirmed = await IsEmailConfirmed(request.Email, connection);
                if (!emailConfirmed.HasValue) return Ok(new { message = "Si el correo existe, las instrucciones para recuperar la contraseña fueron enviadas." });
                if (!emailConfirmed.Value) return BadRequest(new { message = "Primero debe verificar su correo electrónico antes de recuperar la contraseña." });

                var resetToken = GenerateSecureToken();
                var expiration = DateTime.UtcNow.AddHours(1);

                UpdatePasswordResetToken(request.Email, resetToken, expiration, connection);
                await _emailService.SendPasswordResetEmailAsync(request.Email, resetToken);

                return Ok(new { message = "Las instrucciones para recuperar la contraseña fueron enviadas a su correo." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos.", details = ex.Message });
            }
        }

        [EnableRateLimiting("StrictPasswordOps")]
        [HttpPost("reset")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (string.IsNullOrEmpty(request.Token) || string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.NewPassword))
                return BadRequest(new { message = "Token, correo electrónico y nueva contraseña son requeridos." });

            if (!_passwordService.IsPasswordStrong(request.NewPassword))
                return BadRequest(new { message = "La nueva contraseña no cumple con los requisitos de seguridad." });

            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                if (!await IsValidResetToken(request.Email, request.Token, connection))
                    return BadRequest(new { message = "Token inválido o expirado." });

                var hashedPassword = _passwordService.HashPassword(request.NewPassword);
                UpdateUserPassword(request.Email, request.Token, hashedPassword, connection);

                return Ok(new { message = "Contraseña restablecida exitosamente. Ya puede iniciar sesión con su nueva contraseña." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos.", details = ex.Message });
            }
        }

        #region Métodos Privados
        private async Task<bool?> IsEmailConfirmed(string email, SqlConnection connection)
        {
            var query = "SELECT emailConfirmado FROM Usuario WHERE email = @Email AND Activo = 1";
            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Email", email);
            return await command.ExecuteScalarAsync() as bool?;
        }

        private void UpdatePasswordResetToken(string email, string token, DateTime expiration, SqlConnection connection)
        {
            ExecuteNonQuery(
                @"UPDATE Usuario SET tokenResetContrasena = @Token, expiracionTokenResetContrasena = @Expiration, 
                  actualizadoEl = GETUTCDATE() WHERE email = @Email",
                new { Token = token, Expiration = expiration, Email = email }, connection);
        }

        private async Task<bool> IsValidResetToken(string email, string token, SqlConnection connection)
        {
            var query = "SELECT expiracionTokenResetContrasena FROM Usuario WHERE email = @Email AND tokenResetContrasena = @Token AND Activo = 1";
            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Email", email);
            command.Parameters.AddWithValue("@Token", token);

            var expiration = await command.ExecuteScalarAsync() as DateTime?;
            return expiration.HasValue && expiration.Value > DateTime.UtcNow;
        }

        private void UpdateUserPassword(string email, string token, string hashedPassword, SqlConnection connection)
        {
            ExecuteNonQuery(
                @"UPDATE Usuario SET contrasenaHash = @PasswordHash, tokenResetContrasena = NULL, 
                  expiracionTokenResetContrasena = NULL, fechaActualizacionContrasena = GETUTCDATE(), 
                  actualizadoEl = GETUTCDATE() WHERE email = @Email AND tokenResetContrasena = @Token",
                new { PasswordHash = hashedPassword, Email = email, Token = token }, connection);
        }

        private string GenerateSecureToken()
        {
            var tokenData = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(tokenData);
            return Convert.ToBase64String(tokenData).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        private void ExecuteNonQuery(string query, object parameters, SqlConnection connection)
        {
            using var command = new SqlCommand(query, connection);
            foreach (var prop in parameters.GetType().GetProperties())
                command.Parameters.AddWithValue($"@{prop.Name}", prop.GetValue(parameters) ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        #endregion
    }
}