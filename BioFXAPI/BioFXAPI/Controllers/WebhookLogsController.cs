using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookLogsController : ControllerBase
    {
        private readonly string _cs;
        private readonly ILogger<WebhookLogsController> _logger;

        public WebhookLogsController(IConfiguration cfg, ILogger<WebhookLogsController> logger)
        {
            _cs = cfg.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        [HttpPost("placetopay")]
        [AllowAnonymous]
        public async Task<IActionResult> ReceivePlacetoPay()
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            var cfg = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var verify = bool.TryParse(cfg["PlacetoPay:VerifyWebhookSignature"], out var v) && v;
            var secret = cfg["PlacetoPay:SecretKey"]!;

            int requestId = -1;
            string? bodySignature = null;
            string? statusStatus = null;
            string? statusDate = null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // requestId: int o string
                if (root.TryGetProperty("requestId", out var ridEl))
                {
                    if (ridEl.ValueKind == JsonValueKind.Number && ridEl.TryGetInt32(out var rid))
                    {
                        requestId = rid;
                    }
                    else if (ridEl.ValueKind == JsonValueKind.String &&
                             int.TryParse(ridEl.GetString(), out var rid2))
                    {
                        requestId = rid2;
                    }
                }

                // status.status y status.date
                if (root.TryGetProperty("status", out var stEl))
                {
                    if (stEl.TryGetProperty("status", out var stStatusEl) && stStatusEl.ValueKind == JsonValueKind.String)
                        statusStatus = stStatusEl.GetString();

                    if (stEl.TryGetProperty("date", out var stDateEl) && stDateEl.ValueKind == JsonValueKind.String)
                        statusDate = stDateEl.GetString();
                }

                // signature en el BODY (Checkout)
                if (root.TryGetProperty("signature", out var sigEl) && sigEl.ValueKind == JsonValueKind.String)
                    bodySignature = sigEl.GetString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Payload de webhook malformado o no parseable. Body (primeros 500 chars): {Body}",
                    body.Length > 500 ? body[..500] : body);
            }

            // Firma esperada según Checkout: SHA-256(requestId + status.status + status.date + secretKey)
            if (verify)
            {
                if (requestId <= 0 || string.IsNullOrWhiteSpace(statusStatus) ||
                    string.IsNullOrWhiteSpace(statusDate) || string.IsNullOrWhiteSpace(bodySignature))
                {
                    await InsertInvalidSignatureLog(requestId, body, bodySignature);
                    return Unauthorized(new { message = "Invalid signature" });
                }

                var raw = $"{requestId}{statusStatus}{statusDate}{secret}";
                var received = bodySignature!.Trim();

                bool ok;

                if (received.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    // SHA-256
                    received = received.Substring("sha256:".Length);
                    using var sha256 = SHA256.Create();
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
                    var expectedHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    ok = string.Equals(expectedHex, received, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    // SHA-1 (modo legado) -- Para las pruebas. 
                    using var sha1 = SHA1.Create();
                    var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(raw));
                    var expectedHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    ok = string.Equals(expectedHex, received, StringComparison.OrdinalIgnoreCase);
                }

                if (!ok)
                {
                    await InsertInvalidSignatureLog(requestId, body, bodySignature);
                    return Unauthorized(new { message = "Invalid signature" });
                }
            }


            // Idempotencia: si ya existe un webhook PROCESSED para este requestId, no reprocesar
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            if (requestId > 0)
            {
                var alreadyProcessed = await con.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM WebhookLog WHERE RequestId = @RequestId AND Status = 'PROCESSED'",
                    new { RequestId = requestId });

                if (alreadyProcessed > 0)
                {
                    _logger.LogInformation(
                        "Webhook duplicado ignorado para RequestId={RequestId} — ya existe registro PROCESSED",
                        requestId);
                    return Ok(new { received = true, requestId, duplicate = true });
                }
            }

            // Registrar log 'RECEIVED'
            var logId = await con.ExecuteScalarAsync<int>(
                     @"INSERT INTO WebhookLog(RequestId, Payload, Signature, Status, Processed, CreadoEl, ActualizadoEl, Activo)
               OUTPUT INSERTED.Id
               VALUES(@RequestId, @Payload, @Signature, 'RECEIVED', 0, GETUTCDATE(), GETUTCDATE(), 1)",
                 new
                 {
                     RequestId = requestId > 0 ? requestId : (int?)null,
                     Payload = body,
                     Signature = bodySignature ?? string.Empty
                 });

            // Disparar reconsulta si tenemos requestId
            if (requestId > 0)
            {
                var apiBase = cfg["Webhook:InternalApiBaseUrl"];
                if (string.IsNullOrWhiteSpace(apiBase))
                    apiBase = $"{Request.Scheme}://{Request.Host}";
                using var http = new HttpClient();
                var content = new StringContent(JsonSerializer.Serialize(new { requestId }), Encoding.UTF8, "application/json");

                try
                {
                    var internalKey = cfg["InternalApiKey"];
                    if (string.IsNullOrWhiteSpace(internalKey))
                    {
                        await con.ExecuteAsync(
                            "UPDATE WebhookLog SET Status='ERROR', Processed=0, ActualizadoEl=GETUTCDATE() WHERE Id=@Id",
                            new { Id = logId });
                        return StatusCode(500, new { message = "InternalApiKey no configurada." });
                    }

                    http.DefaultRequestHeaders.Remove("X-Internal-Api-Key");
                    http.DefaultRequestHeaders.Add("X-Internal-Api-Key", internalKey);

                    var r = await http.PostAsync($"{apiBase}/api/Transactions/internal/refresh-by-request", content);
                    var respBody = await r.Content.ReadAsStringAsync();

                    await con.ExecuteAsync(
                      "UPDATE WebhookLog SET Status=@S, Processed=@P, ActualizadoEl=GETUTCDATE(), Payload = @Payload WHERE Id=@Id",
                      new
                      {
                          S = r.IsSuccessStatusCode ? "PROCESSED" : "ERROR",
                          P = r.IsSuccessStatusCode ? 1 : 0,
                          Id = logId,
                          Payload = body + "\n\nINTERNAL_RESPONSE_STATUS=" + (int)r.StatusCode + "\nINTERNAL_RESPONSE_BODY=" + respBody
                      });
                }
                catch
                {
                    await con.ExecuteAsync(
                        "UPDATE WebhookLog SET Status='ERROR', Processed=0, ActualizadoEl=GETUTCDATE() WHERE Id=@Id",
                        new { Id = logId });
                }
            }

            return Ok(new { received = true, requestId });
        }

        private async Task InsertInvalidSignatureLog(int requestId, string body, string? sig)
        {
            using var conErr = new SqlConnection(_cs);
            await conErr.OpenAsync();
            await conErr.ExecuteAsync(
                @"INSERT INTO WebhookLog(RequestId, Payload, Signature, Status, Processed, CreadoEl, ActualizadoEl, Activo)
          VALUES(@RequestId, @Payload, @Signature, 'INVALID_SIGNATURE', 0, GETUTCDATE(), GETUTCDATE(), 1)",
                new
                {
                    RequestId = requestId > 0 ? requestId : (int?)null,
                    Payload = body,
                    Signature = sig ?? string.Empty
                });
        }


        [Authorize]
        [HttpGet("last")]
        public async Task<IActionResult> Last([FromQuery] int take = 50)
        {
            using var con = new SqlConnection(_cs);
            var rows = await con.QueryAsync<dynamic>(
                @"SELECT TOP (@Take) Id, RequestId, Status, CreadoEl FROM WebhookLog ORDER BY Id DESC",
                new { Take = take });
            return Ok(rows);
        }
    }
}
