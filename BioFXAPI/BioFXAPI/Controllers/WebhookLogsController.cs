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
        private readonly IHttpClientFactory _httpFactory; 

        public WebhookLogsController(IConfiguration cfg, IHttpClientFactory? factory = null)
        {
            _cs = cfg.GetConnectionString("DefaultConnection");
            _httpFactory = factory!;
        }

        // Endpoint para recibir notificaciones de PlacetoPay
        // Configura esta URL en tu panel/solicitud de sesión
        [HttpPost("placetopay")]
        [AllowAnonymous]
        public async Task<IActionResult> ReceivePlacetoPay()
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            // Guarda log del webhook
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await con.ExecuteAsync(
                @"INSERT INTO WebhookLog(Event, Payload, Headers, ReceivedAt)
                  VALUES(@Ev, @Payload, @Headers, GETUTCDATE())",
                new
                {
                    Ev = "PLACETOPAY",
                    Payload = body,
                    Headers = JsonSerializer.Serialize(Request.Headers.ToDictionary(k => k.Key, v => v.Value.ToString()))
                });

            // Intenta extraer requestId y reconsultar vía TransactionsController
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("requestId", out var ridEl) && ridEl.TryGetInt32(out var rid))
                {
                    // Reusar el flujo de reconsulta (opcional: llamar al endpoint interno)
                    
                    return Ok(new { received = true, requestId = rid, action = "reconsult" });
                }
            }
            catch { /* payload no JSON o sin requestId, igual lo registramos */ }

            return Ok(new { received = true });
        }

        // Solo admin: ver últimos logs
        [Authorize]
        [HttpGet("last")]
        public async Task<IActionResult> Last([FromQuery] int take = 50)
        {
            using var con = new SqlConnection(_cs);
            var rows = await con.QueryAsync<dynamic>(
                @"SELECT TOP (@Take) Id, Event, ReceivedAt FROM WebhookLog ORDER BY Id DESC",
                new { Take = take });
            return Ok(rows);
        }
    }
}
