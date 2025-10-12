using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
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
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            int requestId = -1;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("requestId", out var ridEl) && ridEl.TryGetInt32(out var rid))
                    requestId = rid;
            }
            catch { /* no JSON */ }

            var signature = Request.Headers.TryGetValue("Signature", out var sig) ? sig.ToString()
                           : Request.Headers.TryGetValue("X-PTP-Signature", out var xsig) ? xsig.ToString()
                           : "";

            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            await con.ExecuteAsync(
                @"INSERT INTO WebhookLog(RequestId, Payload, Signature, Status, Processed, CreadoEl, ActualizadoEl, Activo)
                  VALUES(@RequestId, @Payload, @Signature, @Status, @Processed, SYSDATETIME(), SYSDATETIME(), 1)",
                new { RequestId = requestId, Payload = body, Signature = signature, Status = "RECEIVED", Processed = 0 });

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
