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
        [Authorize]
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

            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var pUrl = await con.ExecuteScalarAsync<string>(
                "SELECT ProcessUrl FROM [Transaction] WHERE RequestId=@Rid AND Activo=1",
                new { Rid = r.RequestId });

            var baseUri = new Uri(_cfg["PlacetoPay:BaseUrl"]);
            if (!string.IsNullOrWhiteSpace(pUrl))
            {
                var u = new Uri(pUrl);
                baseUri = new Uri($"{u.Scheme}://{u.Host}/");
            }

            using var http = new HttpClient { BaseAddress = baseUri };
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var resp = await http.PostAsync($"api/session/{r.RequestId}",
                new StringContent(auth, Encoding.UTF8, "application/json"));

            var payload = await resp.Content.ReadAsStringAsync();
			if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, payload);

            var txRow = await con.QueryFirstOrDefaultAsync<(int Id, int OrderId, string Status)>(
				"SELECT TOP 1 Id, OrderId, Status FROM [Transaction] WHERE RequestId=@Rid AND Activo=1 ORDER BY Id DESC",
				new { Rid = r.RequestId });
            if (txRow == default) return NotFound(new { message = "No existe transacción." });

            // propietario
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var isMine = await con.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM [Order] WHERE Id=@Id AND UserId=@Uid AND Activo=1",
                new { Id = txRow.OrderId, Uid = userId });
            if (isMine == 0) return Forbid();

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
                SET p.Stock = p.Stock - oi.Quantity,
                    p.StockReservado = CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END,
                    p.ActualizadoEl = GETUTCDATETIME()
                FROM Producto p INNER JOIN OrderItem oi ON oi.ProductId = p.Id
                WHERE oi.OrderId = @OrderId;", new { OrderId = txRow.OrderId }, dbtx);
            }
            else if (mapped == "REJECTED" || mapped == "EXPIRED")
            {
                await con.ExecuteAsync(@"
                UPDATE p
                SET p.StockReservado = CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END,
                    p.ActualizadoEl = GETUTCDATETIME()
                FROM Producto p INNER JOIN OrderItem oi ON oi.ProductId = p.Id
                WHERE oi.OrderId = @OrderId;", new { OrderId = txRow.OrderId }, dbtx);
            }

            await con.ExecuteAsync(
                @"UPDATE [Order] SET Status=@E, ActualizadoEl=GETUTCDATETIME() WHERE Id=@Id;
                UPDATE [Transaction] SET Status=@E, ActualizadoEl=GETUTCDATETIME() WHERE Id=@TxId;",
                new { Id = txRow.OrderId, E = mapped, TxId = txRow.Id }, dbtx);

            dbtx.Commit();

            return Ok(new { orderId = txRow.OrderId, status = mapped });
		}


        [Authorize]
        [HttpPost("cancel-by-request")]
        public async Task<IActionResult> CancelByRequest([FromBody] RefreshRequest r)
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            // 1) Buscar orden por requestId
            var orderId = await con.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 OrderId FROM [Transaction] WHERE RequestId=@Rid AND Activo=1 ORDER BY Id DESC",
                new { Rid = r.RequestId });
            if (orderId is null) return NotFound(new { message = "Transacción no encontrada." });

            // 2) Dueño
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var isMine = await con.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM [Order] WHERE Id=@Id AND UserId=@Uid AND Activo=1",
                new { Id = orderId.Value, Uid = userId });
            if (isMine == 0) return Forbid();

            // 3) Estado actual
            var last = await con.QueryFirstOrDefaultAsync<(int TxId, string Status)>(
                @"SELECT TOP 1 Id, Status FROM [Transaction]
          WHERE OrderId=@Id AND Activo=1 ORDER BY Id DESC",
                new { Id = orderId.Value });
            if (last == default) return BadRequest(new { message = "Orden sin transacción." });
            if (string.Equals(last.Status, "PAID", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "La orden ya está pagada." });

            // 4) Cancelación local + liberar reserva
            using var tx = con.BeginTransaction();

            await con.ExecuteAsync(@"
        UPDATE p
        SET p.StockReservado = CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END,
            p.ActualizadoEl = GETUTCDATETIME()
        FROM Producto p INNER JOIN OrderItem oi ON oi.ProductId = p.Id
        WHERE oi.OrderId = @OrderId;", new { OrderId = orderId.Value }, tx);

            await con.ExecuteAsync(@"
        UPDATE [Order] SET Status='CANCELLED', ActualizadoEl=GETUTCDATETIME() WHERE Id=@Id;
        UPDATE [Transaction] SET Status='CANCELLED', ActualizadoEl=GETUTCDATETIME()
        WHERE OrderId=@Id AND Activo=1;", new { Id = orderId.Value }, tx);

            tx.Commit();
            return Ok(new { orderId = orderId.Value, status = "CANCELLED" });
        }



        public record RefreshRequest(int RequestId);
	}
}
