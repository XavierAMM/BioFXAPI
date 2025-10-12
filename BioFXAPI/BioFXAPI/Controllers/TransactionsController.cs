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
			_http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
		}

		[HttpPost("refresh-by-request")]
		[AllowAnonymous]
		public async Task<IActionResult> RefreshByRequestId([FromBody] RefreshRequest r)
		{
			var seed = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz",System.Globalization.CultureInfo.InvariantCulture);

			var nonceBytes = RandomNumberGenerator.GetBytes(16);

			var nonce = Convert.ToBase64String(nonceBytes);

			var secret = _cfg["PlacetoPay:SecretKey"]!.Trim();
			var input = new byte[nonceBytes.Length + Encoding.UTF8.GetByteCount(seed + secret)];
			Buffer.BlockCopy(nonceBytes, 0, input, 0, nonceBytes.Length);
			Encoding.UTF8.GetBytes(seed + secret, 0, seed.Length + secret.Length, input, nonceBytes.Length);

			var tranKey = Convert.ToBase64String(SHA256.HashData(input));

			var auth = JsonSerializer.Serialize(new { auth = new { login = _cfg["PlacetoPay:Login"], tranKey, nonce, seed } });

			var resp = await _http.PostAsync($"api/session/{r.RequestId}", new StringContent(auth, Encoding.UTF8, "application/json"));
			var payload = await resp.Content.ReadAsStringAsync();
			if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, payload);

			using var con = new SqlConnection(_cs);
			await con.OpenAsync();

			var txRow = await con.QueryFirstOrDefaultAsync<(int Id, int OrderId, string Status)>(
				"SELECT Id, OrderId, Status FROM [Transaction] WHERE RequestId=@Rid AND Activo=1",
				new { Rid = r.RequestId });

			if (txRow == default) return Ok(new { requestId = r.RequestId, raw = JsonDocument.Parse(payload).RootElement });

			using var doc = JsonDocument.Parse(payload);
			var status = doc.RootElement.GetProperty("status").GetProperty("status").GetString();
            var mapped = status switch
            {
                "APPROVED" or "OK" => "PAID",
                "PENDING" or "PENDING_PAYMENT" or "PENDING_VALIDATION" => "PENDING",
                "REJECTED" or "FAILED" => "REJECTED",
                "EXPIRED" => "EXPIRED",
                _ => "PENDING"
            };

            using var dbtx = con.BeginTransaction();

			if (!string.Equals(txRow.Status, "PAID", StringComparison.OrdinalIgnoreCase) && mapped == "PAID")
			{
				await con.ExecuteAsync(@"
                    UPDATE p
                    SET p.Stock = p.Stock - oi.Quantity, p.ActualizadoEl = SYSDATETIME()
                    FROM Producto p
                    INNER JOIN OrderItem oi ON oi.ProductId = p.Id
                    WHERE oi.OrderId = @OrderId;",
					new { OrderId = txRow.OrderId }, dbtx);
			}

			await con.ExecuteAsync(
				@"UPDATE [Order] SET Status=@E, ActualizadoEl=SYSDATETIME() WHERE Id=@OrderId;
                  UPDATE [Transaction] SET Status=@E, ActualizadoEl=SYSDATETIME() WHERE Id=@TxId;",
				new { OrderId = txRow.OrderId, E = mapped, TxId = txRow.Id }, dbtx);

			dbtx.Commit();
			return Ok(new { orderId = txRow.OrderId, status = mapped });
		}

		public record RefreshRequest(int RequestId);
	}
}
