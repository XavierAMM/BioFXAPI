using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Diagnostics;
using BioFXAPI.Models;
using BioFXAPI.Notifications;


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

            _logger.LogInformation(
                "EmailSettings cargados: SmtpServer={SmtpServer}, SmtpPort={SmtpPort}, SenderEmail={SenderEmail}, EnableSsl={EnableSsl}",
                _smtpServer, _smtpPort, _senderEmail, _enableSsl);
        }

        private async Task SendEmailAsync(
            string toEmail,
            string subject,
            string htmlContent,
            byte[]? attachmentBytes,
            string? attachmentFileName,
            string? attachmentContentType)
        {
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress("BioFX", _senderEmail));
            email.To.Add(MailboxAddress.Parse(toEmail));
            email.Subject = subject;

            var builder = new BodyBuilder
            {
                HtmlBody = htmlContent
            };

            if (attachmentBytes != null &&
                attachmentBytes.Length > 0 &&
                !string.IsNullOrWhiteSpace(attachmentFileName))
            {
                var ct = string.IsNullOrWhiteSpace(attachmentContentType)
                    ? "application/octet-stream"
                    : attachmentContentType;

                builder.Attachments.Add(attachmentFileName, attachmentBytes, ContentType.Parse(ct));
            }

            email.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(_smtpServer, _smtpPort, GetSecureSocketOption());
            if (string.IsNullOrWhiteSpace(_senderPassword))
                throw new InvalidOperationException("EmailSettings:SenderPassword no está configurado.");

            await smtp.AuthenticateAsync(_senderEmail, _senderPassword);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
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
                message.Subject = "Activa tu cuenta – BioFX";

                var tokenEnc = Uri.EscapeDataString(verificationToken);
                var emailEnc = Uri.EscapeDataString(recipientEmail);
                var verifyUrl = $"https://api.biofx.com.ec/api/account/verify-email?token={tokenEnc}&email={emailEnc}";

                var bodyBuilder = new BodyBuilder();

                bodyBuilder.HtmlBody = $@"
                  <div style='display:none;max-height:0;overflow:hidden;color:transparent;opacity:0;'>
                    Verifica tu correo para activar tu cuenta en BioFX.
                  </div>
                  <div style=""font-family: Arial, Helvetica, sans-serif; background:#f5f7f8; padding:24px 0;"">
                    <div style=""max-width:600px;margin:0 auto;background:#ffffff;border-radius:8px;
                                box-shadow:0 1px 3px rgba(0,0,0,0.06);overflow:hidden;"">
                      <div style=""padding:24px 24px 0 24px;border-bottom:1px solid #e8eef0"">
                        <h1 style=""margin:0;font-size:22px;line-height:28px;color:#0b3b34;"">¡Bienvenido a BioFX!</h1>
                        <p style=""margin:8px 0 0 0;color:#4b5b5b;font-size:14px;line-height:20px;"">
                          Solo falta un paso para activar tu cuenta.
                        </p>
                      </div>

                      <div style=""padding:24px;"">
                        <p style=""margin:0 0 16px 0;color:#2a3a3a;font-size:14px;line-height:22px;"">
                          Verifica tu correo haciendo clic en el siguiente botón:
                        </p>

                        <div style=""text-align:center;margin:24px 0 28px 0;"">
                          <a href=""{verifyUrl}""
                             style=""background:#2c9c8a;color:#ffffff;text-decoration:none;
                                    padding:12px 24px;border-radius:6px;font-weight:bold;
                                    display:inline-block;font-size:15px;"">
                            Verificar correo
                          </a>
                        </div>

                        <p style=""margin:0 0 12px 0;color:#6b7a7a;font-size:12px;line-height:18px;"">
                          Si el botón no funciona, copia y pega este enlace en tu navegador:
                        </p>
                        <p style=""margin:0 0 20px 0;word-break:break-all;font-size:12px;line-height:18px;color:#2a3a3a;"">
                          <a href=""{verifyUrl}"" style=""color:#2c9c8a;text-decoration:underline;"">{verifyUrl}</a>
                        </p>

                        <div style=""background:#f8f9fa;border:1px solid #e8eef0;border-radius:6px;padding:12px 14px;margin-top:8px;"">
                          <p style=""margin:0;color:#627070;font-size:12px;line-height:18px;"">
                            <strong>Vigencia:</strong> este enlace expira en <strong>15 minutos</strong>.
                          </p>
                        </div>
                      </div>

                      <div style=""padding:18px 24px;border-top:1px solid #e8eef0;color:#7a8a8a;font-size:12px;line-height:18px;"">
                        <p style=""margin:0 0 8px 0;"">
                          Si no creaste esta cuenta, puedes ignorar este mensaje.
                        </p>
                        <p style=""margin:0;color:#97a6a6;"">
                          © {DateTime.UtcNow.Year} BioFX Medical
                        </p>
                      </div>
                    </div>
                  </div>
                ";

                // Texto plano
                bodyBuilder.TextBody = $@"
                Bienvenido a BioFX

                Solo falta un paso para activar tu cuenta. Abre este enlace:
                {verifyUrl}

                Vigencia: el enlace expira en 15 minutos.

                Si no creaste esta cuenta, ignora este mensaje.
                © {DateTime.UtcNow.Year} BioFX Medical
                ";

                message.Body = bodyBuilder.ToMessageBody();

                using (var client = new SmtpClient())
                {
                    client.Timeout = 30000;
                    await client.ConnectAsync(_smtpServer, _smtpPort, GetSecureSocketOption());
                    if (string.IsNullOrWhiteSpace(_senderPassword))
                        throw new InvalidOperationException("EmailSettings:SenderPassword no está configurado.");

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
                client.Timeout = 30000;
                await client.ConnectAsync(_smtpServer, _smtpPort, GetSecureSocketOption()); // 465=SslOnConnect, 587=StartTls
                if (string.IsNullOrWhiteSpace(_senderPassword))
                    throw new InvalidOperationException("EmailSettings:SenderPassword no está configurado.");

                await client.AuthenticateAsync(_senderEmail, _senderPassword);

                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }

        public async Task SendOrderPaidToShippingAsync(OrderPaidToShippingEmail model)
        {
            var subject = $"Nueva orden pagada – Ref {model.OrderReference} – Req {model.RequestId}";
            var createdLocalStr = FormatUtcInGuayaquil(model.OrderCreatedAt);

            string itemsHtml = "";
            if (model.Items != null && model.Items.Count > 0)
            {
                itemsHtml = @"
            <table style='width:100%;border-collapse:collapse;font-size:13px;'>
              <thead>
                <tr>
                  <th style='border-bottom:1px solid #ddd;text-align:left;padding:6px;'>Producto</th>
                  <th style='border-bottom:1px solid #ddd;text-align:right;padding:6px;'>Cantidad</th>
                  <th style='border-bottom:1px solid #ddd;text-align:right;padding:6px;'>Precio unitario</th>
                  <th style='border-bottom:1px solid #ddd;text-align:right;padding:6px;'>Subtotal</th>
                </tr>
              </thead>
              <tbody>";

                foreach (var it in model.Items)
                {
                    itemsHtml += $@"
                        <tr>
                          <td style='border-bottom:1px solid #f0f0f0;padding:6px;'>{System.Net.WebUtility.HtmlEncode(it.ProductName)}</td>
                          <td style='border-bottom:1px solid #f0f0f0;padding:6px;text-align:right;'>{it.Quantity}</td>
                          <td style='border-bottom:1px solid #f0f0f0;padding:6px;text-align:right;'>{it.UnitPrice:0.00}</td>
                          <td style='border-bottom:1px solid #f0f0f0;padding:6px;text-align:right;'>{it.TotalPrice:0.00}</td>
                        </tr>";
                }

                itemsHtml += @"
                    </tbody>
                </table>";
            }

            var html = $@"
                <div style='font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#222;'>
                    <h2 style='margin-top:0;'>Nueva orden pagada para despacho</h2>

                    <h3>Datos de la orden</h3>
                    <ul>
                    <li><strong>Referencia:</strong> {System.Net.WebUtility.HtmlEncode(model.OrderReference)}</li>
                    <li><strong>RequestId (PlacetoPay):</strong> {model.RequestId}</li>
                    <li><strong>Fecha de creación:</strong> {createdLocalStr}</li>
                    <li><strong>Estado de la orden:</strong> {System.Net.WebUtility.HtmlEncode(model.OrderStatus)}</li>
                    <li><strong>Estado del pago:</strong> {System.Net.WebUtility.HtmlEncode(model.PaymentStatus)}</li>
                    </ul>

                    <h3>Datos del comprador</h3>
                    <ul>
                    <li><strong>Nombre:</strong> {System.Net.WebUtility.HtmlEncode(model.CustomerFullName)}</li>
                    <li><strong>Email:</strong> {System.Net.WebUtility.HtmlEncode(model.CustomerEmail)}</li>
                    {(string.IsNullOrWhiteSpace(model.CustomerPhone) ? "" : $"<li><strong>Teléfono:</strong> {System.Net.WebUtility.HtmlEncode(model.CustomerPhone)}</li>")}
                    <li><strong>Tipo/Número documento:</strong> {System.Net.WebUtility.HtmlEncode(model.DocumentType)} – {System.Net.WebUtility.HtmlEncode(model.DocumentNumber)}</li>
                    {(string.IsNullOrWhiteSpace(model.DoctorName) ? "" : $"<li><strong>Médico tratante:</strong> {System.Net.WebUtility.HtmlEncode(model.DoctorName)}</li>")}
                    </ul>

                    <h3>Dirección de entrega</h3>
                    <ul>
                    <li>{System.Net.WebUtility.HtmlEncode(model.AddressLine)}</li>
                    <li>{System.Net.WebUtility.HtmlEncode(model.City)}, {System.Net.WebUtility.HtmlEncode(model.Province)}</li>
                    <li>{System.Net.WebUtility.HtmlEncode(model.Country)}{(string.IsNullOrWhiteSpace(model.PostalCode) ? "" : $" – CP {System.Net.WebUtility.HtmlEncode(model.PostalCode)}")}</li>
                    </ul>

                    <h3>Detalle de pago</h3>
                    <ul>
                    <li><strong>Total:</strong> {model.TotalAmount:0.00} {System.Net.WebUtility.HtmlEncode(model.Currency)}</li>
                    {(string.IsNullOrWhiteSpace(model.PaymentMethod) ? "" : $"<li><strong>Método de pago:</strong> {System.Net.WebUtility.HtmlEncode(model.PaymentMethod)} – {System.Net.WebUtility.HtmlEncode(model.PaymentMethodName ?? "")}</li>")}
                    {(string.IsNullOrWhiteSpace(model.IssuerName) ? "" : $"<li><strong>Emisor:</strong> {System.Net.WebUtility.HtmlEncode(model.IssuerName)}</li>")}
                    </ul>

                    {(string.IsNullOrEmpty(itemsHtml) ? "" : $@"
                    <h3>Detalle de productos</h3>
                    {itemsHtml}
                    ")}

                    <p style='margin-top:20px;'>
                    Esta orden ya está pagada y lista para gestión de envío. 
                    Usar la referencia y el RequestId para cualquier coordinación con el cliente.
                    </p>
                </div>";

            await SendEmailAsync(
                model.ToEmail,
                subject,
                html,
                model.AttachmentBytes,
                model.AttachmentFileName,
                model.AttachmentContentType);
        }

        private static string FormatUtcInGuayaquil(DateTime utc)
        {
            // Asegurar UTC
            if (utc.Kind == DateTimeKind.Local) utc = utc.ToUniversalTime();
            if (utc.Kind == DateTimeKind.Unspecified) utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

            var tz = GetGuayaquilTimeZoneSafe();
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);

            return local.ToString("dd/MM/yyyy HH:mm");
        }

        private static TimeZoneInfo GetGuayaquilTimeZoneSafe()
        {
            // 1) IANA (Linux)
            // 2) Windows (Windows Server / IIS)
            // 3) Fallback UTC
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/Guayaquil");
            }
            catch (TimeZoneNotFoundException)
            {
                // Windows ID (UTC-5, sin DST) común en Windows
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
                }
                catch
                {
                    return TimeZoneInfo.Utc;
                }
            }
            catch (InvalidTimeZoneException)
            {
                // Si la zona está corrupta en el sistema
                return TimeZoneInfo.Utc;
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

        public async Task SendSimpleEmailAsync(string to, string subject, string body)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("BioFX", _senderEmail));
            message.To.Add(new MailboxAddress("", to));
            message.Subject = subject;

            var builder = new BodyBuilder { TextBody = body, HtmlBody = $"<pre>{System.Net.WebUtility.HtmlEncode(body)}</pre>" };
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient(new ProtocolLogger(Console.OpenStandardError()));
            client.Timeout = 30000;
            await client.ConnectAsync(_smtpServer, _smtpPort, GetSecureSocketOption());
            if (string.IsNullOrWhiteSpace(_senderPassword))
                throw new InvalidOperationException("EmailSettings:SenderPassword no está configurado.");
            await client.AuthenticateAsync(_senderEmail, _senderPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }



    }
}