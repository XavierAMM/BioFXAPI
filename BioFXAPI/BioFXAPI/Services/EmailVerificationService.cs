using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BioFXAPI.Services
{
    public class EmailVerificationService
    {
        private readonly EmailService _emailService;
        private readonly ILogger<EmailVerificationService> _logger;

        public int TokenMinutes { get; }
        public int ResendCooldownMinutes { get; }
        public int ResendDailyLimit { get; }

        public EmailVerificationService(EmailService emailService,IConfiguration cfg,ILogger<EmailVerificationService> logger)
        {
            _emailService = emailService;
            _logger = logger;

            TokenMinutes = cfg.GetValue<int?>("EmailVerification:TokenMinutes") ?? 15;
            ResendCooldownMinutes = cfg.GetValue<int?>("EmailVerification:ResendCooldownMinutes") ?? 2;
            ResendDailyLimit = cfg.GetValue<int?>("EmailVerification:ResendDailyLimit") ?? 5;
        }

        public record ResendOutcome(
            string message,
            string action,
            string? resendAction = null,
            bool? canResend = null,
            int? cooldownSeconds = null,
            DateTime? expiresAtUtc = null);

        public async Task<ResendOutcome> EvaluateAndMaybeResendAsync(int userId, string email, string ip, SqlConnection connection, bool autoResendWhenAllowed)
        {
            var normEmail = email.Trim().ToLowerInvariant();

            // 1) Daily limit
            using (var dailyCmd = new SqlCommand(@"
                SELECT COUNT(1)
                FROM dbo.EmailVerificationTokens
                WHERE UsuarioId = @UserId
                  AND CreadoEl > DATEADD(hour, -24, GETUTCDATE());", connection))
            {
                dailyCmd.Parameters.AddWithValue("@UserId", userId);
                var dailyCount = (int)await dailyCmd.ExecuteScalarAsync();

                if (dailyCount >= ResendDailyLimit)
                {
                    return new ResendOutcome(
                        message: "Tu correo no está confirmado. Has alcanzado el límite de reenvíos por hoy. Intenta más tarde.",
                        action: "EMAIL_NOT_CONFIRMED",
                        resendAction: "DAILY_LIMIT",
                        canResend: false);
                }
            }

            // 2) Último token (cooldown y expiración)
            DateTime? lastCreated = null;
            DateTime? lastExpires = null;

            using (var lastCmd = new SqlCommand(@"
                SELECT TOP 1 CreadoEl, ExpiraEl
                FROM dbo.EmailVerificationTokens
                WHERE UsuarioId = @UserId
                ORDER BY CreadoEl DESC;", connection))
            {
                lastCmd.Parameters.AddWithValue("@UserId", userId);
                using var r = await lastCmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    lastCreated = r.IsDBNull(0) ? null : r.GetDateTime(0);
                    lastExpires = r.IsDBNull(1) ? null : r.GetDateTime(1);
                }
            }

            // 3) Token vigente
            if (lastExpires.HasValue && lastExpires.Value > DateTime.UtcNow)
            {
                return new ResendOutcome(
                    message: "Tu correo no está confirmado. Revisa tu bandeja de entrada: ya tienes un enlace vigente.",
                    action: "EMAIL_NOT_CONFIRMED",
                    resendAction: "TOKEN_STILL_VALID",
                    canResend: false,
                    expiresAtUtc: lastExpires);
            }

            // 4) Cooldown
            if (lastCreated.HasValue)
            {
                var cooldownUntil = lastCreated.Value.AddMinutes(ResendCooldownMinutes);
                if (cooldownUntil > DateTime.UtcNow)
                {
                    var seconds = (int)Math.Ceiling((cooldownUntil - DateTime.UtcNow).TotalSeconds);
                    return new ResendOutcome(
                        message: "Revisa tu correo. Ya se envió un enlace recientemente.",
                        action: "EMAIL_NOT_CONFIRMED",
                        resendAction: "COOLDOWN",
                        canResend: false,
                        cooldownSeconds: seconds);
                }
            }

            if (!autoResendWhenAllowed)
            {
                // Caso UI: se permite reenvío, pero no autoenvía.
                return new ResendOutcome(
                    message: "Tu correo no está confirmado. Puedes reenviar el enlace de verificación.",
                    action: "EMAIL_NOT_CONFIRMED",
                    resendAction: "CAN_RESEND",
                    canResend: true);
            }

            // 5) Crear token nuevo y enviar
            var token = GenerateSecureToken();
            var tokenHash = HashTokenSha256(token);

            using var tx = connection.BeginTransaction();
            try
            {
                await InvalidateTokensAsync(userId, connection, tx);

                using (var ins = new SqlCommand(@"
                    INSERT INTO dbo.EmailVerificationTokens
                        (UsuarioId, TokenHash, ExpiraEl, UsadoEl, RevocadoEl, EmailEnviadoA, IpCreacion, Activo, CreadoEl, ActualizadoEl)
                    VALUES
                        (@UserId, @TokenHash, DATEADD(minute, @Minutes, GETUTCDATE()), NULL, NULL, @Email, @Ip, 1, GETUTCDATE(), GETUTCDATE());",
                    connection, tx))
                {
                    ins.Parameters.AddWithValue("@UserId", userId);
                    ins.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
                    ins.Parameters.AddWithValue("@Minutes", TokenMinutes);
                    ins.Parameters.AddWithValue("@Email", normEmail);
                    ins.Parameters.AddWithValue("@Ip", ip ?? "Unknown");
                    await ins.ExecuteNonQueryAsync();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            try
            {
                await _emailService.SendVerificationEmailAsync(normEmail, token);

                return new ResendOutcome(
                    message: "Te reenviamos el enlace de verificación.",
                    action: "EMAIL_NOT_CONFIRMED",
                    resendAction: "RESENT",
                    canResend: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reenviando verificación a {Email}", normEmail);
                return new ResendOutcome(
                    message: "No se pudo enviar el correo en este momento. Intenta más tarde.",
                    action: "EMAIL_NOT_CONFIRMED",
                    resendAction: "SEND_FAILED",
                    canResend: true);
            }
        }

        private static async Task InvalidateTokensAsync(int userId, SqlConnection connection, SqlTransaction tx)
        {
            using var cmd = new SqlCommand(@"
                UPDATE dbo.EmailVerificationTokens
                SET Activo = 0,
                    RevocadoEl = COALESCE(RevocadoEl, GETUTCDATE()),
                    ActualizadoEl = GETUTCDATE()
                WHERE UsuarioId = @UserId
                  AND Activo = 1
                  AND UsadoEl IS NULL;", connection, tx);

            cmd.Parameters.AddWithValue("@UserId", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        private static byte[] HashTokenSha256(string token)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        }

        private static string GenerateSecureToken()
        {
            var tokenData = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(tokenData);
            return Convert.ToBase64String(tokenData).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        public async Task IssueNewVerificationTokenAsync(int userId, string email, string ip, SqlConnection connection, SqlTransaction tx)
        {
            var normEmail = email.Trim().ToLowerInvariant();
            var token = GenerateSecureToken();
            var tokenHash = HashTokenSha256(token);

            await InvalidateTokensAsync(userId, connection, tx);

            using (var ins = new SqlCommand(@"
        INSERT INTO dbo.EmailVerificationTokens
            (UsuarioId, TokenHash, ExpiraEl, UsadoEl, RevocadoEl, EmailEnviadoA, IpCreacion, Activo, CreadoEl, ActualizadoEl)
        VALUES
            (@UserId, @TokenHash, DATEADD(minute, @Minutes, GETUTCDATE()), NULL, NULL, @Email, @Ip, 1, GETUTCDATE(), GETUTCDATE());",
                connection, tx))
            {
                ins.Parameters.AddWithValue("@UserId", userId);
                ins.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
                ins.Parameters.AddWithValue("@Minutes", TokenMinutes);
                ins.Parameters.AddWithValue("@Email", normEmail);
                ins.Parameters.AddWithValue("@Ip", ip ?? "Unknown");
                await ins.ExecuteNonQueryAsync();
            }

            // devolver token para el envío
            await _emailService.SendVerificationEmailAsync(normEmail, token);
        }

        public byte[] ComputeTokenHash(string token)
        {
            return HashTokenSha256(token);
        }

        public Task InvalidatePendingTokensAsync(int userId, SqlConnection connection, SqlTransaction tx)
        {
            return InvalidateTokensAsync(userId, connection, tx);
        }


    }
}
