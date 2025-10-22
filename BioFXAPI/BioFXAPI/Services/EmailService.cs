using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Diagnostics;

namespace BioFXAPI.Services
{
    public class EmailService
    {
        private readonly string _smtpServer;
        private readonly int _smtpPort;
        private readonly string _senderEmail;
        private readonly string _senderPassword;
        private readonly bool _enableSsl;

        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _logger = logger;
            var emailSettings = configuration.GetSection("EmailSettings");
            _smtpServer = emailSettings["SmtpServer"];
            _smtpPort = int.Parse(emailSettings["SmtpPort"]);
            _senderEmail = emailSettings["SenderEmail"];
            _senderPassword = emailSettings["SenderPassword"];
            _enableSsl = bool.Parse(emailSettings["EnableSsl"]);
        }

        private SecureSocketOptions GetSecureSocketOption()
        {
            if (!_enableSsl)
                return SecureSocketOptions.None;

            
            return _smtpPort == 465 ?
                SecureSocketOptions.SslOnConnect :
                SecureSocketOptions.StartTls;
        }

        public async Task SendVerificationEmailAsync(string recipientEmail, string verificationToken)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("BioFX", _senderEmail));
                message.To.Add(new MailboxAddress("", recipientEmail));
                message.Subject = "Verificación de su cuenta BioFX";

                // SIN URL ENCODE - usar valores directos
                var bodyBuilder = new BodyBuilder();

                bodyBuilder.HtmlBody = $@"
            <h2>¡Bienvenido a BioFX!</h2>
            <p>Por favor verifique su correo electrónico haciendo clic al URL a continuación:</p>
            <p><a href='https://api.biofx.com.ec/api/account/verify-email?token={verificationToken}&email={recipientEmail}'>Verificar correo electrónico</a></p>
            <p>Este URL expirará en 24 horas.</p>
            <br>
            <p>Si no creó esta cuenta, ignore este mensaje.</p>
        ";

                bodyBuilder.TextBody = $@"
            ¡Bienvenido a BioFX!
            
            Por favor verifique su correo electrónico haciendo clic al URL a continuación:
            https://api.biofx.com.ec/api/account/verify-email?token={verificationToken}&email={recipientEmail}
            
            Este URL expirará en 24 horas.
            
            Si no creó esta cuenta, ignore este mensaje.
        ";

                message.Body = bodyBuilder.ToMessageBody();

                using (var client = new SmtpClient())
                {
                    client.Timeout = 30000;
                    await client.ConnectAsync(_smtpServer, _smtpPort, GetSecureSocketOption());
                    await client.AuthenticateAsync(_senderEmail, _senderPassword);
                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                }

                _logger.LogInformation("✅ Email de verificación enviado exitosamente");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error en SendVerificationEmailAsync2: {ex.Message}");
                throw;
            }
        }

        public async Task SendPasswordResetEmailAsync(string recipientEmail, string resetToken)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("BioFX", _senderEmail));
            message.To.Add(new MailboxAddress("", recipientEmail));
            message.Subject = "Restablecimiento de Contraseña - BioFX";

            // Codificar parámetros para URL
            var encodedToken = System.Web.HttpUtility.UrlEncode(resetToken);
            var encodedEmail = System.Web.HttpUtility.UrlEncode(recipientEmail);

            var bodyBuilder = new BodyBuilder();
            bodyBuilder.HtmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                    <h2 style='color: #2c9c8a;'>Restablecimiento de Contraseña</h2>
                    <p>Hemos recibido una solicitud para restablecer su contraseña de BioFX.</p>
            
                    <div style='background-color: #f8f9fa; padding: 20px; border-radius: 5px; margin: 20px 0;'>
                        <p>Haga clic en el siguiente botón para crear una nueva contraseña:</p>
                        <p style='text-align: center; margin: 25px 0;'>
                            <a href='https://api.biofx.com.ec/reset-password/reset-password.html?token={encodedToken}&email={encodedEmail}' 
                                style='background-color: #2c9c8a; color: white; padding: 12px 24px; 
                                        text-decoration: none; border-radius: 5px; font-weight: bold; 
                                        display: inline-block;'>
                                Restablecer Contraseña
                            </a>
                        </p>
                    </div>
            
                    <p>Este enlace expirará en 1 hora.</p>
            
                    <p style='color: #666; font-size: 14px;'>
                        Si no solicitó este restablecimiento, por favor ignore este correo.
                    </p>
            
                    <hr style='border: none; border-top: 1px solid #e4efe9; margin: 20px 0;'>
                    <p style='color: #666; font-size: 14px;'>Atentamente,<br>Equipo de BioFX Medical</p>
                </div>
            ";

            // Versión texto plano
            bodyBuilder.TextBody = $@"
                Restablecimiento de Contraseña - BioFX

                Hemos recibido una solicitud para restablecer su contraseña.

                Para restablecer su contraseña, visite el siguiente enlace:
                https://api.biofx.com.ec/reset-password/reset-password.html?token={encodedToken}&email={encodedEmail}

                Este enlace expirará en 1 hora.

                Si no solicitó este restablecimiento, por favor ignore este correo.

                Atentamente,
                Equipo de BioFX Medical
            ";

            message.Body = bodyBuilder.ToMessageBody();

            using (var client = new SmtpClient())
            {
                await client.ConnectAsync(_smtpServer, _smtpPort, GetSecureSocketOption());
                await client.AuthenticateAsync(_senderEmail, _senderPassword);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }

        public async Task SendEmailChangeConfirmationAsync(string email, string token)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("BioFX", _senderEmail));
            message.To.Add(new MailboxAddress("", email));
            message.Subject = "Confirmación de cambio de correo electrónico";

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                <h2 style='color: #2c9c8a;'>Confirmación de cambio de correo</h2>
                <p>Se ha solicitado un cambio de correo electrónico para su cuenta de BioFX.</p>
                
                <div style='background-color: #f8f9fa; padding: 20px; border-radius: 5px; margin: 20px 0; text-align: center;'>
                    <p style='font-size: 16px; margin-bottom: 10px;'>Su código de verificación es:</p>
                    <h3 style='font-size: 32px; letter-spacing: 5px; color: #2c9c8a; margin: 20px 0;'>{token}</h3>
                    <p style='font-size: 14px; color: #666;'>Este código expirará en 1 hora.</p>
                </div>
                
                <p style='color: #666; font-size: 14px;'>
                    Si no solicitó este cambio, por favor ignore este correo y contacte a soporte.
                </p>
            </div>",

                TextBody = $"Su código de verificación para cambio de email es: {token}. Este código expirará en 1 hora."
            };

            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_smtpServer, _smtpPort, GetSecureSocketOption());
            await client.AuthenticateAsync(_senderEmail, _senderPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}