using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OrdersController : ControllerBase
    {
        private readonly string _cs;
        private readonly IConfiguration _cfg;
        private readonly HttpClient _http;

        public OrdersController(IConfiguration cfg)
        {
            _cfg = cfg;
            _cs = cfg.GetConnectionString("DefaultConnection");
            _http = new HttpClient { BaseAddress = new Uri(_cfg["PlacetoPay:BaseUrl"]) };
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateFromCart([FromBody] CreateOrderRequest req)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            using var tx = con.BeginTransaction();

            // Carrito activo
            var cartId = await con.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 Id FROM ShoppingCart WHERE UsuarioId=@UserId AND Activo=1 ORDER BY Id DESC",
                new { UserId = userId }, tx);
            if (!cartId.HasValue) return BadRequest(new { message = "Carrito vacío." });

            // Items
            var items = await con.QueryAsync<(int ProductoId, int Cantidad, decimal PrecioUnitario)>(
                @"SELECT ProductoId, Cantidad, PrecioUnitario 
                  FROM CartItem WHERE CartId=@Cart AND Activo=1",
                new { Cart = cartId }, tx);

            if (!items.Any()) return BadRequest(new { message = "Carrito sin items." });

            // Total
            var total = items.Sum(i => i.Cantidad * i.PrecioUnitario);

            // Crea orden
            var orderId = await con.ExecuteScalarAsync<int>(
                @"INSERT INTO [Order](UsuarioId, CartId, Total, Moneda, Estado, Referencia, Descripcion, Activo, CreadoEl, ActualizadoEl)
                  OUTPUT INSERTED.Id
                  VALUES(@Uid,@Cart,@Total,'USD','PENDING',@Ref,@Desc,1,GETUTCDATE(),GETUTCDATE())",
                new
                {
                    Uid = userId,
                    Cart = cartId,
                    Total = total,
                    Ref = req.Referencia ?? $"BIO-{DateTime.UtcNow:yyyyMMddHHmmss}-{userId}",
                    Desc = req.Descripcion ?? "Compra BioFX"
                }, tx);

            foreach (var it in items)
            {
                await con.ExecuteAsync(
                    @"INSERT INTO OrderItem(OrderId, ProductoId, Cantidad, PrecioUnitario, Subtotal, Activo, CreadoEl, ActualizadoEl)
                      VALUES(@Oid,@Pid,@Cant,@Precio, @Sub, 1, GETUTCDATE(), GETUTCDATE())",
                    new
                    {
                        Oid = orderId,
                        Pid = it.ProductoId,
                        Cant = it.Cantidad,
                        Precio = it.PrecioUnitario,
                        Sub = it.Cantidad * it.PrecioUnitario
                    }, tx);
            }

            tx.Commit();
            return Ok(new { OrderId = orderId, Total = total, Currency = "USD" });
        }

        // Crea sesión en PlacetoPay para la orden
        [HttpPost("{orderId:int}/placetopay/session")]
        public async Task<IActionResult> CreatePlacetoPaySession(int orderId, [FromBody] ReturnUrlRequest req)
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var order = await con.QueryFirstOrDefaultAsync<(
                decimal Total, string Moneda, string Referencia, string Descripcion,
                int? RequestId, string Estado, string ProcessUrl)>(
                @"SELECT Total, Moneda, Referencia, Descripcion, RequestId, Estado, ProcessUrl
          FROM [Order] WHERE Id=@Id AND Activo=1", new { Id = orderId });

            if (order == default) return NotFound(new { message = "Orden no encontrada." });
            if (string.Equals(order.Estado, "PAID", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "La orden ya está pagada." });

            // Idempotencia: si hay sesión previa pendiente, reusar
            if (order.RequestId.HasValue && string.Equals(order.Estado, "PENDING", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(order.ProcessUrl))
            {
                return Ok(new { requestId = order.RequestId.Value, processUrl = order.ProcessUrl });
            }

            var seed = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var nonceBytes = RandomNumberGenerator.GetBytes(16);
            var nonce = Convert.ToBase64String(nonceBytes);
            var tranKey = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(nonce + seed + _cfg["PlacetoPay:SecretKey"]!)));

            var body = new
            {
                locale = "es_EC",
                auth = new
                {
                    login = _cfg["PlacetoPay:Login"],
                    tranKey,
                    nonce,
                    seed
                },
                payment = new
                {
                    reference = order.Referencia,
                    description = order.Descripcion,
                    amount = new { currency = "USD", total = order.Total }
                },
                expiration = DateTime.UtcNow.AddMinutes(int.Parse(_cfg["PlacetoPay:TimeoutMinutes"]!)).ToString("yyyy-MM-ddTHH:mm:sszzz"),
                returnUrl = req.ReturnUrl,
                ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent = Request.Headers["User-Agent"].ToString()
            };

            var json = JsonSerializer.Serialize(body);
            var resp = await _http.PostAsync("api/session", new StringContent(json, Encoding.UTF8, "application/json"));
            var payload = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, payload);

            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("requestId", out var ridEl) || !ridEl.TryGetInt32(out var requestId))
                return StatusCode(502, new { message = "Respuesta sin requestId.", raw = payload });

            var processUrl = doc.RootElement.GetProperty("processUrl").GetString() ?? string.Empty;

            await con.ExecuteAsync(
                @"UPDATE [Order] SET RequestId=@Rid, ProcessUrl=@Url, ActualizadoEl=GETUTCDATE() WHERE Id=@Id;
                  INSERT INTO [Transaction](OrderId, RequestId, Status, CreatedAt, RawStatusJson)
                  VALUES(@Id, @Rid, 'PENDING', GETUTCDATE(), @Raw)",
                new { Id = orderId, Rid = requestId, Url = processUrl, Raw = payload });

            return Ok(new { requestId, processUrl });
        }

        // Consulta estado de la orden contra PlacetoPay
        [HttpGet("{orderId:int}/status")]
        public async Task<IActionResult> RefreshStatus(int orderId)
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var data = await con.QueryFirstOrDefaultAsync<(int RequestId, string Estado)>(
                "SELECT ISNULL(RequestId,0) AS RequestId, Estado FROM [Order] WHERE Id=@Id AND Activo=1",
                new { Id = orderId });

            if (data == default || data.RequestId == 0)
                return BadRequest(new { message = "Orden sin sesión creada." });

            var seed = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var nonceBytes = RandomNumberGenerator.GetBytes(16);
            var nonce = Convert.ToBase64String(nonceBytes);
            var tranKey = Convert.ToBase64String(
                SHA256.HashData(Encoding.UTF8.GetBytes(nonce + seed + _cfg["PlacetoPay:SecretKey"]!)));

            var auth = JsonSerializer.Serialize(new
            {
                auth = new { login = _cfg["PlacetoPay:Login"], tranKey, nonce, seed }
            });

            var resp = await _http.PostAsync($"api/session/{data.RequestId}",
                new StringContent(auth, Encoding.UTF8, "application/json"));
            var payload = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, payload);

            using var doc = JsonDocument.Parse(payload);
            var status = doc.RootElement.GetProperty("status").GetProperty("status").GetString();
            var mapped = status switch { "APPROVED" => "PAID", "REJECTED" => "REJECTED", _ => "PENDING" };

            using var tx = con.BeginTransaction();

            // transición a PAID → descontar stock una sola vez
            if (!string.Equals(data.Estado, "PAID", StringComparison.OrdinalIgnoreCase)
                && mapped == "PAID")
            {
                // Descontar stock según renglones de la orden
                await con.ExecuteAsync(@"
            UPDATE p SET p.Stock = p.Stock - oi.Cantidad, p.ActualizadoEl = GETUTCDATE()
            FROM Producto p
            INNER JOIN OrderItem oi ON oi.ProductoId = p.Id
            WHERE oi.OrderId = @OrderId;", new { OrderId = orderId }, tx);
            }

            await con.ExecuteAsync(@"
        UPDATE [Order] SET Estado=@E, ActualizadoEl=GETUTCDATE() WHERE Id=@Id;
        UPDATE [Transaction] SET Status=@E, UpdatedAt=GETUTCDATE(), RawStatusJson=@Raw WHERE OrderId=@Id;",
                new { Id = orderId, E = mapped, Raw = payload }, tx);

            tx.Commit();
            return Ok(new { orderId, status = mapped });
        }
        public record CreateOrderRequest(string? Referencia, string? Descripcion);
        public record ReturnUrlRequest(string ReturnUrl);
    }
}
