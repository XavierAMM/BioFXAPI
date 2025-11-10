using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;

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

            var cartId = await con.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 Id FROM ShoppingCart WHERE UserId=@UserId AND Activo=1 ORDER BY Id DESC",
                new { UserId = userId }, tx);
            if (!cartId.HasValue) return BadRequest(new { message = "Carrito vacío." });

            var items = await con.QueryAsync<(int ProductId, int Quantity, decimal UnitPrice)>(
                @"SELECT ci.ProductId, ci.Quantity, p.Precio AS UnitPrice
                  FROM CartItem ci
                  INNER JOIN Producto p ON p.Id=ci.ProductId
                  WHERE ci.CartId=@Cart AND ci.Activo=1",
                new { Cart = cartId }, tx);
            if (!items.Any()) return BadRequest(new { message = "Carrito sin items." });

            var total = items.Sum(i => i.Quantity * i.UnitPrice);
            var tax = 0m;
            var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{userId}";
            var referenceRaw = req.Reference ?? $"BIO-{DateTime.UtcNow:yyyyMMddHHmmss}-{userId}";
            var reference = referenceRaw.Length > 32 ? referenceRaw[..32] : referenceRaw;

            var orderId = await con.ExecuteScalarAsync<int>(
                @"INSERT INTO [Order](UserId, OrderNumber, Reference, Description, TotalAmount, TaxAmount, Currency, Status, CreadoEl, ActualizadoEl, Activo)
                  OUTPUT INSERTED.Id
                  VALUES(@Uid, @OrderNumber, @Reference, @Desc, @Total, @Tax, 'USD', 'PENDING', GETUTCDATETIME(), GETUTCDATETIME(), 1)",
                new { Uid = userId, OrderNumber = orderNumber, Reference = reference, Desc = req.Description ?? "Compra BioFX", Total = total, Tax = tax }, tx);

            foreach (var it in items)
            {
                await con.ExecuteAsync(
                    @"INSERT INTO OrderItem(OrderId, ProductId, Quantity, UnitPrice, TotalPrice, CreadoEl, Activo)
                      VALUES(@Oid, @Pid, @Qty, @Price, @Sub, GETUTCDATETIME(), 1)",
                    new
                    {
                        Oid = orderId,
                        Pid = it.ProductId,
                        Qty = it.Quantity,
                        Price = it.UnitPrice,
                        Sub = it.Quantity * it.UnitPrice
                    }, tx);

                await con.ExecuteAsync(
                    @"UPDATE Producto
                      SET StockReservado = COALESCE(StockReservado,0) + @Qty, ActualizadoEl = GETUTCDATETIME()
                      WHERE Id = @Pid", new { Pid = it.ProductId, Qty = it.Quantity }, tx);
            }

            await con.ExecuteAsync(
                "UPDATE CartItem SET Activo=0, ActualizadoEl=GETUTCDATETIME() WHERE CartId=@Cart AND Activo=1",
                new { Cart = cartId }, tx);

            tx.Commit();
            return Ok(new { OrderId = orderId, OrderNumber = orderNumber, Reference = reference, Total = total, Currency = "USD" });
        }

        
        [HttpPost("{orderId:int}/placetopay/session")]
        public async Task<IActionResult> CreatePlacetoPaySession(int orderId, [FromBody] ReturnUrlRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ReturnUrl))
                return BadRequest(new { message = "returnUrl requerido." });

            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var isMine = await con.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM [Order] WHERE Id=@Id AND UserId=@Uid AND Activo=1",
                new { Id = orderId, Uid = userId });
            if (isMine == 0) return Forbid();

            var order = await con.QueryFirstOrDefaultAsync<(decimal TotalAmount, string Currency, string Reference, string Description)>(
                "SELECT TotalAmount, Currency, Reference, Description FROM [Order] WHERE Id=@Id AND Activo=1",
                new { Id = orderId });
            if (order == default) return NotFound(new { message = "Orden no encontrada." });

            var status = await con.ExecuteScalarAsync<string>(
                "SELECT Status FROM [Order] WHERE Id=@Id AND Activo=1", new { Id = orderId });
            if (string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "La orden ya está pagada." });

            // Idempotencia por Transaction pendiente
            var pendingTx = await con.QueryFirstOrDefaultAsync<(int RequestId, string ProcessUrl)>(
            @"SELECT TOP 1 RequestId, ProcessUrl
              FROM [Transaction]
              WHERE OrderId=@Id AND Activo=1 AND Status='PENDING'
              ORDER BY Id DESC",
            new { Id = orderId });

            bool sameHost = false;
            if (pendingTx != default && !string.IsNullOrWhiteSpace(pendingTx.ProcessUrl))
            {
                var pu = new Uri(pendingTx.ProcessUrl);
                var bu = new Uri(_cfg["PlacetoPay:BaseUrl"]);
                sameHost = string.Equals(pu.Host, bu.Host, StringComparison.OrdinalIgnoreCase);
            }
            if (pendingTx != default && sameHost)
                return Ok(new { requestId = pendingTx.RequestId, processUrl = pendingTx.ProcessUrl });

            var seed = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz",System.Globalization.CultureInfo.InvariantCulture);

            var nonceBytes = RandomNumberGenerator.GetBytes(16);

            var nonce = Convert.ToBase64String(nonceBytes);

            var secret = _cfg["PlacetoPay:SecretKey"]!.Trim();
            var input = new byte[nonceBytes.Length + Encoding.UTF8.GetByteCount(seed + secret)];
            Buffer.BlockCopy(nonceBytes, 0, input, 0, nonceBytes.Length);
            Encoding.UTF8.GetBytes(seed + secret, 0, seed.Length + secret.Length, input, nonceBytes.Length);

            var tranKey = Convert.ToBase64String(SHA256.HashData(input));

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (string.IsNullOrWhiteSpace(ip)) ip = "127.0.0.1";

            var notificationUrl = _cfg["PlacetoPay:NotificationUrl"];
            var buyer = await con.QueryFirstOrDefaultAsync<(string Nombre, string Apellido, string Email, string Telefono)>(
                @"SELECT TOP 1 Nombre, Apellido, Email, Telefono FROM Persona WHERE UsuarioId=
                  (SELECT UserId FROM [Order] WHERE Id=@Id)", new { Id = orderId });

            var body = new
            {
                locale = "es_EC",
                auth = new { login = _cfg["PlacetoPay:Login"], tranKey, nonce, seed },
                payment = new
                {
                    reference = order.Reference,
                    description = order.Description,
                    amount = new { currency = (order.Currency ?? "USD").Trim().ToUpperInvariant(), total = order.TotalAmount },
                    buyer = new
                    {
                        name = buyer.Nombre,
                        surname = buyer.Apellido,
                        email = buyer.Email,
                        mobile = buyer.Telefono
                    }
                },
                expiration = DateTime.UtcNow.AddMinutes(int.Parse(_cfg["PlacetoPay:TimeoutMinutes"]!))
                             .ToString("yyyy-MM-ddTHH:mm:sszzz"),
                returnUrl = req.ReturnUrl,
                notificationUrl,                
                ipAddress = ip,
                userAgent = Request.Headers["User-Agent"].ToString()
            };

            var json = JsonSerializer.Serialize(body);
            var resp = await _http.PostAsync("api/session", new StringContent(json, Encoding.UTF8, "application/json"));
            var payload = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, payload);

            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("requestId", out var ridEl) || !ridEl.TryGetInt32(out var requestId))
                return StatusCode(502, new { message = "Respuesta sin requestId.", raw = payload });
            var processUrl = doc.RootElement.GetProperty("processUrl").GetString() ?? "";

            await con.ExecuteAsync(
                @"INSERT INTO [Transaction](OrderId, RequestId, InternalReference, ProcessUrl, Status, Reason, Message, PaymentMethod, PaymentMethodName, IssuerName, Refunded, RefundedAmount, CreadoEl, ActualizadoEl, Activo)
                  VALUES(@OrderId, @RequestId, NULL, @ProcessUrl, 'PENDING', NULL, NULL, NULL, NULL, NULL, 0, NULL, GETUTCDATETIME(), GETUTCDATETIME(), 1)",
                new { OrderId = orderId, RequestId = requestId, ProcessUrl = processUrl });

            return Ok(new { requestId, processUrl });
        }

        [HttpGet("{orderId:int}/status")]
        public async Task<IActionResult> RefreshStatus(int orderId)
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var isMine = await con.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM [Order] WHERE Id=@Id AND UserId=@Uid AND Activo=1",
                new { Id = orderId, Uid = userId });
            if (isMine == 0) return Forbid();

            var txRow = await con.QueryFirstOrDefaultAsync<(int Id, int RequestId, string Status)>(
                @"SELECT TOP 1 Id, RequestId, Status
                  FROM [Transaction]
                  WHERE OrderId=@Id AND Activo=1
                  ORDER BY Id DESC", new { Id = orderId });
            if (txRow == default) return BadRequest(new { message = "Orden sin transacción." });

            var seed = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz",System.Globalization.CultureInfo.InvariantCulture);

            var nonceBytes = RandomNumberGenerator.GetBytes(16);
            var nonce = Convert.ToBase64String(nonceBytes);

            var secret = _cfg["PlacetoPay:SecretKey"]!.Trim();
            var input = new byte[nonceBytes.Length + Encoding.UTF8.GetByteCount(seed + secret)];
            Buffer.BlockCopy(nonceBytes, 0, input, 0, nonceBytes.Length);
            Encoding.UTF8.GetBytes(seed + secret, 0, seed.Length + secret.Length, input, nonceBytes.Length);

            var tranKey = Convert.ToBase64String(SHA256.HashData(input));

            var authJson = JsonSerializer.Serialize(new
            {
                auth = new { login = _cfg["PlacetoPay:Login"], tranKey, nonce, seed }
            });

            var pUrl = await con.ExecuteScalarAsync<string>(
                "SELECT TOP 1 ProcessUrl FROM [Transaction] WHERE Id=@TxId",
                new { TxId = txRow.Id });

            var baseUri = _http.BaseAddress!;
            if (!string.IsNullOrWhiteSpace(pUrl))
            {
                var u = new Uri(pUrl);
                baseUri = new Uri($"{u.Scheme}://{u.Host}/");
            }
            using var http = new HttpClient { BaseAddress = baseUri };
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var resp = await http.PostAsync($"api/session/{txRow.RequestId}",
                new StringContent(authJson, Encoding.UTF8, "application/json"));

            var payload = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, payload);

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

            // Descontar stock al pasar a PAID
            if (!string.Equals(txRow.Status, "PAID", StringComparison.OrdinalIgnoreCase) && mapped == "PAID")
            {
                await con.ExecuteAsync(@"
                UPDATE p
                SET p.Stock = p.Stock - oi.Quantity,
                    p.StockReservado = CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END,
                    p.ActualizadoEl = GETUTCDATETIME()
                FROM Producto p INNER JOIN OrderItem oi ON oi.ProductId = p.Id
                WHERE oi.OrderId = @OrderId;", new { OrderId = orderId }, dbtx);
            }
            else if (mapped == "REJECTED" || mapped == "EXPIRED")
            {
                await con.ExecuteAsync(@"
                UPDATE p
                SET p.StockReservado = CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END,
                    p.ActualizadoEl = GETUTCDATETIME()
                FROM Producto p INNER JOIN OrderItem oi ON oi.ProductId = p.Id
                WHERE oi.OrderId = @OrderId;", new { OrderId = orderId }, dbtx);
            }

            await con.ExecuteAsync(
                @"UPDATE [Order] SET Status=@E, ActualizadoEl=GETUTCDATETIME() WHERE Id=@Id;
                  UPDATE [Transaction] SET Status=@E, ActualizadoEl=GETUTCDATETIME() WHERE Id=@TxId;",
                            new { Id = orderId, E = mapped, TxId = txRow.Id }, dbtx);

            dbtx.Commit();
            return Ok(new { orderId, status = mapped });
        }

        [Authorize]
        [HttpPost("{orderId:int}/cancel")]
        public async Task<IActionResult> CancelPendingSession(int orderId)
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            // 1) Dueño
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var isMine = await con.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM [Order] WHERE Id=@Id AND UserId=@Uid AND Activo=1",
                new { Id = orderId, Uid = userId });
            if (isMine == 0) return Forbid();

            // 2) Última transacción
            var last = await con.QueryFirstOrDefaultAsync<(int TxId, int? RequestId, string Status)>(
                @"SELECT TOP 1 Id, RequestId, Status
          FROM [Transaction] WHERE OrderId=@Id AND Activo=1 ORDER BY Id DESC",
                new { Id = orderId });
            if (last == default) return BadRequest(new { message = "Orden sin transacción." });
            if (string.Equals(last.Status, "PAID", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "La orden ya está pagada." });

            // 3) Reconsulta rápida a P2P si hay requestId
            if (last.RequestId is int rid && rid > 0)
            {
                // auth
                var seed = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
                var nonceBytes = RandomNumberGenerator.GetBytes(16);
                var nonce = Convert.ToBase64String(nonceBytes);
                var secret = _cfg["PlacetoPay:SecretKey"]!.Trim();
                var input = new byte[nonceBytes.Length + Encoding.UTF8.GetByteCount(seed + secret)];
                Buffer.BlockCopy(nonceBytes, 0, input, 0, nonceBytes.Length);
                Encoding.UTF8.GetBytes(seed + secret, 0, seed.Length + secret.Length, input, nonceBytes.Length);
                var tranKey = Convert.ToBase64String(SHA256.HashData(input));
                var authJson = JsonSerializer.Serialize(new { auth = new { login = _cfg["PlacetoPay:Login"], tranKey, nonce, seed } });

                // baseUri según ProcessUrl
                var pUrl = await con.ExecuteScalarAsync<string>("SELECT TOP 1 ProcessUrl FROM [Transaction] WHERE Id=@TxId", new { TxId = last.TxId });
                var baseUri = _http.BaseAddress!;
                if (!string.IsNullOrWhiteSpace(pUrl))
                {
                    var u = new Uri(pUrl);
                    baseUri = new Uri($"{u.Scheme}://{u.Host}/");
                }
                using var http = new HttpClient { BaseAddress = baseUri };
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

                var resp = await http.PostAsync($"api/session/{rid}", new StringContent(authJson, Encoding.UTF8, "application/json"));
                if (resp.IsSuccessStatusCode)
                {
                    var payload = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(payload);
                    var gw = doc.RootElement.GetProperty("status").GetProperty("status").GetString();
                    if (gw is "APPROVED" or "OK")
                        return StatusCode(409, new { message = "La sesión fue aprobada por la pasarela." });
                }
            }

            // 4) Cancelación local y liberar reserva
            using var tx = con.BeginTransaction();

            await con.ExecuteAsync(@"
        UPDATE p
        SET p.StockReservado = CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END,
            p.ActualizadoEl = GETUTCDATETIME()
        FROM Producto p INNER JOIN OrderItem oi ON oi.ProductId = p.Id
        WHERE oi.OrderId = @OrderId;", new { OrderId = orderId }, tx);

            await con.ExecuteAsync(@"
        UPDATE [Order] SET Status='CANCELLED', ActualizadoEl=GETUTCDATETIME() WHERE Id=@Id;
        UPDATE [Transaction] SET Status='CANCELLED', ActualizadoEl=GETUTCDATETIME()
        WHERE OrderId=@Id AND Activo=1;", new { Id = orderId }, tx);

            tx.Commit();
            return Ok(new { orderId, status = "CANCELLED" });
        }


        [Authorize]
        [HttpPost("{orderId:int}/placetopay/retry")]
        public async Task<IActionResult> RetryPlacetoPaySession(int orderId, [FromBody] ReturnUrlRequest req)
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var mine = await con.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM [Order] WHERE Id=@Id AND UserId=@Uid AND Activo=1",
                new { Id = orderId, Uid = userId });
            if (mine == 0) return Forbid();

            var txRow = await con.QueryFirstOrDefaultAsync<(string Status, DateTime CreadoEl)>(
                "SELECT TOP 1 Status, CreadoEl FROM [Transaction] WHERE OrderId=@Id AND Activo=1 ORDER BY Id DESC",
                new { Id = orderId });

            if (txRow.Status.Equals("PAID", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "La orden ya está pagada." });

            var tooOld = (DateTime.UtcNow - txRow.CreadoEl).TotalMinutes > 15;
            if (!(tooOld || txRow.Status.Equals("EXPIRED", StringComparison.OrdinalIgnoreCase) || txRow.Status.Equals("REJECTED", StringComparison.OrdinalIgnoreCase)))
                return BadRequest(new { message = "La sesión actual aún es válida." });

            // Reutiliza tu POST api/session (CreatePlacetoPaySession) y devuelve { processUrl, requestId }
            return await CreatePlacetoPaySession(orderId, req);
        }


        public record CreateOrderRequest(string? Reference, string? Description);
        public record ReturnUrlRequest(string ReturnUrl);
    }
}
