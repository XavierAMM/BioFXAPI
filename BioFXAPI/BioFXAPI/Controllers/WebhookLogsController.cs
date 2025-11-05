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
        public WebhookLogsController(IConfiguration cfg) => _cs = cfg.GetConnectionString("DefaultConnection");

        [HttpPost("placetopay")]
        [AllowAnonymous]
        public async Task<IActionResult> ReceivePlacetoPay()
        {
            // 1) Leer cuerpo tal cual
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            // 2) Tomar firma del header (acepta dos variantes comunes)
            string? signatureHeader =
                Request.Headers.TryGetValue("Signature", out var s1) ? s1.ToString() :
                Request.Headers.TryGetValue("X-PTP-Signature", out var s2) ? s2.ToString() :
                Request.Headers.TryGetValue("X-Signature", out var s3) ? s3.ToString() :
                Request.Headers.TryGetValue("PTP-Signature", out var s4) ? s4.ToString() :
                string.Empty;

            signatureHeader = signatureHeader?.Trim();

            // 3) ¿Verificación activada?
            var verify = bool.TryParse(HttpContext.RequestServices
                            .GetRequiredService<IConfiguration>()["PlacetoPay:VerifyWebhookSignature"], out var v) && v;

            // 4) Validar HMAC si corresponde
            if (verify)
            {
                var secret = HttpContext.RequestServices
                              .GetRequiredService<IConfiguration>()["PlacetoPay:SecretKey"]!;
                
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));

                var expected = digest; // bytes HMAC

                bool matchB64 = false;
                try
                {
                    var sigBytes = Convert.FromBase64String(signatureHeader ?? "");
                    matchB64 = CryptographicOperations.FixedTimeEquals(expected, sigBytes);
                }
                catch { /* no era Base64 */ }

                // intentar Hex → bytes
                bool matchHex = false;
                if (!matchB64 && !string.IsNullOrWhiteSpace(signatureHeader))
                {
                    string hex = signatureHeader.Trim().Replace("-", "");
                    if (hex.Length % 2 == 0)
                    {
                        try
                        {
                            var sigBytes = new byte[hex.Length / 2];
                            for (int i = 0; i < sigBytes.Length; i++)
                                sigBytes[i] = Convert.ToByte(hex.Substring(2 * i, 2), 16);

                            matchHex = CryptographicOperations.FixedTimeEquals(expected, sigBytes);
                        }
                        catch { /* no era hex válido */ }
                    }
                }

                bool match = matchB64 || matchHex;


                if (!match)
                {
                    // Registrar intento inválido y devolver 401
                    using var conErr = new SqlConnection(_cs);
                    await conErr.OpenAsync();
                    await conErr.ExecuteAsync(
                        @"INSERT INTO WebhookLog(RequestId, Payload, Signature, Status, Processed, CreadoEl, ActualizadoEl, Activo)
                          VALUES(@RequestId, @Payload, @Signature, 'INVALID_SIGNATURE', 0, SYSDATETIME(), SYSDATETIME(), 1)",
                                new { RequestId = (int?)null, Payload = body, Signature = signatureHeader });

                    return Unauthorized(new { message = "Invalid signature" });
                }
            }

            // 5) Extraer requestId si viene en el payload
            int requestId = -1;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("requestId", out var ridEl) && ridEl.TryGetInt32(out var rid))
                    requestId = rid;
                else if (root.TryGetProperty("data", out var dataEl) &&
                         dataEl.TryGetProperty("requestId", out var ridEl2) && ridEl2.TryGetInt32(out var rid2))
                    requestId = rid2;
                else if (root.TryGetProperty("request", out var reqEl) &&
                         reqEl.TryGetProperty("requestId", out var ridEl3) && ridEl3.TryGetInt32(out var rid3))
                    requestId = rid3;
            }
            catch { /* no JSON */ }


            // 6) Registrar log 'RECEIVED' y disparar reconsulta oficial (fuente de verdad)
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var logId = await con.ExecuteScalarAsync<int>(
             @"INSERT INTO WebhookLog(RequestId, Payload, Signature, Status, Processed, CreadoEl, ActualizadoEl, Activo)
               OUTPUT INSERTED.Id
               VALUES(@RequestId, @Payload, @Signature, 'RECEIVED', 0, SYSDATETIME(), SYSDATETIME(), 1)",
                     new { RequestId = requestId > 0 ? requestId : (int?)null, Payload = body, Signature = signatureHeader });

            if (requestId > 0)
            {
                var apiBase = $"{Request.Scheme}://{Request.Host}";
                using var http = new HttpClient();
                var content = new StringContent(JsonSerializer.Serialize(new { requestId }), Encoding.UTF8, "application/json");
                try
                {
                    var r = await http.PostAsync($"{apiBase}/api/Transactions/refresh-by-request", content);
                    await con.ExecuteAsync(
                      "UPDATE WebhookLog SET Status=@S, Processed=@P, ActualizadoEl=SYSDATETIME() WHERE Id=@Id",
                      new { S = r.IsSuccessStatusCode ? "PROCESSED" : "ERROR", P = r.IsSuccessStatusCode ? 1 : 0, Id = logId });
                }
                catch
                {
                    await con.ExecuteAsync(
                      "UPDATE WebhookLog SET Status='ERROR', Processed=0, ActualizadoEl=SYSDATETIME() WHERE Id=@Id",
                      new { Id = logId });
                }

            }

            return Ok(new { received = true, requestId });
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
