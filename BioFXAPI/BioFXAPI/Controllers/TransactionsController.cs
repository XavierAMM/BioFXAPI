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
    [Authorize]
    public class TransactionsController : ControllerBase
    {
        private readonly string _cs;
        private readonly IConfiguration _cfg;
        private readonly HttpClient _http;

        public TransactionsController(IConfiguration cfg)
        {
            _cfg = cfg;
            _cs = cfg.GetConnectionString("DefaultConnection");
            _http = new HttpClient { BaseAddress = new Uri(_cfg["PlacetoPay:BaseUrl"]) };
        }

        // Reconsulta por requestId directo
        [HttpPost("refresh-by-request")]
        [AllowAnonymous]
        public async Task<IActionResult> RefreshByRequestId([FromBody] RefreshRequest r)
        {
            var seed = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var nonceBytes = RandomNumberGenerator.GetBytes(16);
            var nonce = Convert.ToBase64String(nonceBytes);
            var tranKey = Convert.ToBase64String(
                SHA256.HashData(Encoding.UTF8.GetBytes(nonce + seed + _cfg["PlacetoPay:SecretKey"]!)));

            var auth = JsonSerializer.Serialize(new
            {
                auth = new { login = _cfg["PlacetoPay:Login"], tranKey, nonce, seed }
            });

            var resp = await _http.PostAsync($"api/session/{r.RequestId}",
                new StringContent(auth, Encoding.UTF8, "application/json"));
            var payload = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, payload);

            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var order = await con.QueryFirstOrDefaultAsync<(int Id, string Estado)>(
                "SELECT Id, Estado FROM [Order] WHERE RequestId=@Rid", new { Rid = r.RequestId });

            if (order == default) return Ok(new { requestId = r.RequestId, raw = JsonDocument.Parse(payload).RootElement });

            using var doc = JsonDocument.Parse(payload);
            var status = doc.RootElement.GetProperty("status").GetProperty("status").GetString();
            var mapped = status == "APPROVED" ? "PAID" : status == "REJECTED" ? "REJECTED" : "PENDING";

            using var tx = con.BeginTransaction();

            if (!string.Equals(order.Estado, "PAID", StringComparison.OrdinalIgnoreCase)
                && mapped == "PAID")
            {
                await con.ExecuteAsync(@"
            UPDATE p SET p.Stock = p.Stock - oi.Cantidad, p.ActualizadoEl = GETUTCDATE()
            FROM Producto p
            INNER JOIN OrderItem oi ON oi.ProductoId = p.Id
            WHERE oi.OrderId = @OrderId;", new { OrderId = order.Id }, tx);
            }

            await con.ExecuteAsync(@"
        UPDATE [Order] SET Estado=@E, ActualizadoEl=GETUTCDATE() WHERE Id=@Id;
        UPDATE [Transaction] SET Status=@E, UpdatedAt=GETUTCDATE(), RawStatusJson=@Raw WHERE OrderId=@Id;",
                new { Id = order.Id, E = mapped, Raw = payload }, tx);

            tx.Commit();
            return Ok(new { orderId = order.Id, status = mapped });
        }



        public record RefreshRequest(int RequestId);
    }
}
