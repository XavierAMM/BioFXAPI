using BioFXAPI.Notifications;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TransactionsController : ControllerBase
    {
        private readonly string _cs;
        private readonly IConfiguration _cfg;
        private readonly HttpClient _http;
        private readonly OrderNotificationService _orderNotificationService;

        public TransactionsController(IConfiguration cfg, OrderNotificationService orderNotificationService)
        {
            _cfg = cfg;
            _cs = cfg.GetConnectionString("DefaultConnection");
            _http = new HttpClient { BaseAddress = new Uri(_cfg["PlacetoPay:BaseUrl"]) };
            _http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            _orderNotificationService = orderNotificationService;
        }

        // ========== 1) ENDPOINT USUARIO ==========
        [Authorize]
        [HttpPost("refresh-by-request")]
        public async Task<IActionResult> RefreshByRequestId([FromBody] RefreshRequest r)
        {
            return await RefreshCoreAsync(
                requestId: r.RequestId,
                validateOwner: true,
                callerUser: User);
        }

        // ========== 2) ENDPOINT INTERNO (WEBHOOK) ==========
        // Nota: este endpoint debe ser llamado solo desde tu webhook controller.
        [AllowAnonymous]
        [HttpPost("internal/refresh-by-request")]
        public async Task<IActionResult> RefreshByRequestIdInternal([FromBody] RefreshRequest r)
        {
            // Validar API key interna
            var configured = _cfg["InternalApiKey"];
            if (string.IsNullOrWhiteSpace(configured))
                return StatusCode(500, new { message = "InternalApiKey no configurada." });

            if (!Request.Headers.TryGetValue("X-Internal-Api-Key", out var provided) ||
                string.IsNullOrWhiteSpace(provided) ||
                !string.Equals(provided.ToString(), configured, StringComparison.Ordinal))
            {
                return Unauthorized(new { message = "Invalid internal key." });
            }

            return await RefreshCoreAsync(
                requestId: r.RequestId,
                validateOwner: false,  // interno: no hay usuario final
                callerUser: null);
        }

        // ========== LÓGICA COMÚN ==========
        private async Task<IActionResult> RefreshCoreAsync(int requestId, bool validateOwner, ClaimsPrincipal? callerUser)
        {
            var seed = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
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
                "SELECT TOP 1 ProcessUrl FROM [Transaction] WHERE RequestId=@Rid AND Activo=1 ORDER BY Id DESC",
                new { Rid = requestId });

            var baseUri = new Uri(_cfg["PlacetoPay:BaseUrl"]);
            if (!string.IsNullOrWhiteSpace(pUrl))
            {
                var u = new Uri(pUrl);
                baseUri = new Uri($"{u.Scheme}://{u.Host}/");
            }

            using var http = new HttpClient { BaseAddress = baseUri };
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var resp = await http.PostAsync($"api/session/{requestId}",
                new StringContent(auth, Encoding.UTF8, "application/json"));

            var payload = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, payload);

            var txRow = await con.QueryFirstOrDefaultAsync<(int Id, int OrderId, string Status)>(
                "SELECT TOP 1 Id, OrderId, Status FROM [Transaction] WHERE RequestId=@Rid AND Activo=1 ORDER BY Id DESC",
                new { Rid = requestId });
            if (txRow == default) return NotFound(new { message = "No existe transacción." });

            // Dueño (solo endpoint usuario)
            if (validateOwner)
            {
                var (ok, userId, err) = await TryResolveUserIdAsync(con, callerUser);
                if (!ok) return err!;

                var isMine = await con.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM [Order] WHERE Id=@Id AND UserId=@Uid AND Activo=1",
                    new { Id = txRow.OrderId, Uid = userId });

                if (isMine == 0) return Forbid();
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            // 1) status global
            var statusEl = root.GetProperty("status");
            var status = statusEl.GetProperty("status").GetString();

            var reason = statusEl.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String
                ? reasonEl.GetString()
                : null;

            var message = statusEl.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                ? msgEl.GetString()
                : null;

            // 2) payment[0]
            string? paymentMethod = null;
            string? paymentMethodName = null;
            string? issuerName = null;
            int? internalReference = null;
            bool? refunded = null;
            decimal? refundedAmount = null;
            string? authorization = null;

            if (root.TryGetProperty("payment", out var payArr) &&
                payArr.ValueKind == JsonValueKind.Array &&
                payArr.GetArrayLength() > 0)
            {
                var p0 = payArr[0];

                if (p0.TryGetProperty("internalReference", out var irEl))
                {
                    if (irEl.ValueKind == JsonValueKind.Number && irEl.TryGetInt32(out var irNum))
                        internalReference = irNum;
                    else if (irEl.ValueKind == JsonValueKind.String && int.TryParse(irEl.GetString(), out var irNum2))
                        internalReference = irNum2;
                }

                if (p0.TryGetProperty("paymentMethod", out var pmEl) && pmEl.ValueKind == JsonValueKind.String)
                    paymentMethod = pmEl.GetString();

                if (p0.TryGetProperty("paymentMethodName", out var pmnEl) && pmnEl.ValueKind == JsonValueKind.String)
                    paymentMethodName = pmnEl.GetString();

                if (p0.TryGetProperty("issuerName", out var issEl) && issEl.ValueKind == JsonValueKind.String)
                    issuerName = issEl.GetString();

                if (p0.TryGetProperty("refunded", out var refEl) &&
                    (refEl.ValueKind == JsonValueKind.True || refEl.ValueKind == JsonValueKind.False))
                {
                    refunded = refEl.GetBoolean();
                }

                if (p0.TryGetProperty("amount", out var amountEl) && amountEl.ValueKind == JsonValueKind.Object)
                {
                    if (amountEl.TryGetProperty("to", out var toEl) && toEl.ValueKind == JsonValueKind.Object)
                    {
                        if (toEl.TryGetProperty("total", out var totEl) && totEl.ValueKind == JsonValueKind.Number &&
                            totEl.TryGetDecimal(out var tot))
                        {
                            refundedAmount = tot;
                        }
                    }
                }

                if (p0.TryGetProperty("authorization", out var authEl) && authEl.ValueKind == JsonValueKind.String)
                    authorization = authEl.GetString();
            }

            // 3) mapping estado
            var mapped = status switch
            {
                "APPROVED" or "OK" => "PAID",
                "PENDING" or "PENDING_PAYMENT" or "PENDING_VALIDATION" => "PENDING",
                "REJECTED" or "FAILED" => "REJECTED",
                "EXPIRED" => "EXPIRED",
                _ => "PENDING"
            };

            var justPaid = !string.Equals(txRow.Status, "PAID", StringComparison.OrdinalIgnoreCase)
                           && mapped == "PAID";

            using var dbtx = con.BeginTransaction();

            // 4) Stock
            if (justPaid)
            {
                await con.ExecuteAsync(@"
                    UPDATE p
                    SET p.Stock = p.Stock - oi.Quantity,
                        p.StockReservado = CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END,
                        p.ActualizadoEl = GETUTCDATE()
                    FROM Producto p INNER JOIN OrderItem oi ON oi.ProductId = p.Id
                    WHERE oi.OrderId = @OrderId;",
                    new { OrderId = txRow.OrderId }, dbtx);
            }
            else if (mapped == "REJECTED" || mapped == "EXPIRED")
            {
                await con.ExecuteAsync(@"
                    UPDATE p
                    SET p.StockReservado = CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END,
                        p.ActualizadoEl = GETUTCDATE()
                    FROM Producto p INNER JOIN OrderItem oi ON oi.ProductId = p.Id
                    WHERE oi.OrderId = @OrderId;",
                    new { OrderId = txRow.OrderId }, dbtx);
            }

            // 5) Update Order + Transaction
            await con.ExecuteAsync(
                @"UPDATE [Order] 
                  SET Status=@E, ActualizadoEl=GETUTCDATE() 
                  WHERE Id=@Id;

                  UPDATE [Transaction]
                  SET Status=@E,
                      InternalReference = @InternalReference,
                      Reason           = @Reason,
                      Message          = @Message,
                      PaymentMethod    = @PaymentMethod,
                      PaymentMethodName= @PaymentMethodName,
                      IssuerName       = @IssuerName,
                      Refunded         = CASE WHEN @Refunded IS NULL THEN Refunded ELSE @Refunded END,
                      RefundedAmount   = CASE WHEN @RefundedAmount IS NULL THEN RefundedAmount ELSE @RefundedAmount END,
                      [Authorization]    = @Authorization,
                      ActualizadoEl    = GETUTCDATE()
                  WHERE Id=@TxId;",
                new
                {
                    Id = txRow.OrderId,
                    E = mapped,
                    TxId = txRow.Id,
                    InternalReference = (object?)internalReference ?? DBNull.Value,
                    Reason = (object?)reason ?? DBNull.Value,
                    Message = (object?)message ?? DBNull.Value,
                    PaymentMethod = (object?)paymentMethod ?? DBNull.Value,
                    PaymentMethodName = (object?)paymentMethodName ?? DBNull.Value,
                    IssuerName = (object?)issuerName ?? DBNull.Value,
                    Refunded = refunded.HasValue ? (refunded.Value ? 1 : 0) : (int?)null,
                    RefundedAmount = (object?)refundedAmount ?? DBNull.Value,
                    Authorization = (object?)authorization ?? DBNull.Value
                }, dbtx);

            dbtx.Commit();

            // 6) Notificaciones por email solo al pasar a PAID
            if (justPaid)
            {
                try
                {
                    await _orderNotificationService.SendOrderPaidNotificationsAsync(
                        txRow.OrderId,
                        requestId,
                        HttpContext.RequestAborted);
                }
                catch
                {
                    // no romper la respuesta
                }
            }

            return Ok(new { orderId = txRow.OrderId, status = mapped });
        }

        // helper consistente con OrdersController
        private static async Task<(bool ok, int userId, IActionResult? error)> TryResolveUserIdAsync(
            SqlConnection con, ClaimsPrincipal? user)
        {
            if (user is null) return (false, 0, new UnauthorizedResult());

            var idStr = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst("nameid")?.Value
                     ?? user.FindFirst("uid")?.Value;
            if (int.TryParse(idStr, out var uid1))
                return (true, uid1, null);

            var email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var uid2 = await con.ExecuteScalarAsync<int?>(
                    "SELECT TOP 1 id FROM Usuario WHERE email=@Email AND Activo=1",
                    new { Email = email });
                if (uid2.HasValue) return (true, uid2.Value, null);
            }

            return (false, 0, new UnauthorizedObjectResult(new { message = "Usuario no identificado." }));
        }

        // ===== CancelByRequest (lo dejamos consistente más abajo) =====
        [Authorize]
        [HttpPost("cancel-by-request")]
        public async Task<IActionResult> CancelByRequest([FromBody] RefreshRequest r)
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var orderId = await con.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 OrderId FROM [Transaction] WHERE RequestId=@Rid AND Activo=1 ORDER BY Id DESC",
                new { Rid = r.RequestId });
            if (orderId is null) return NotFound(new { message = "Transacción no encontrada." });

            var (ok, userId, err) = await TryResolveUserIdAsync(con, User);
            if (!ok) return err!;

            var isMine = await con.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM [Order] WHERE Id=@Id AND UserId=@Uid AND Activo=1",
                new { Id = orderId.Value, Uid = userId });
            if (isMine == 0) return Forbid();

            var last = await con.QueryFirstOrDefaultAsync<(int TxId, string Status)>(
                @"SELECT TOP 1 Id, Status FROM [Transaction]
                  WHERE OrderId=@Id AND Activo=1 ORDER BY Id DESC",
                new { Id = orderId.Value });
            if (last == default) return BadRequest(new { message = "Orden sin transacción." });
            if (string.Equals(last.Status, "PAID", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "La orden ya está pagada." });

            using var tx = con.BeginTransaction();

            await con.ExecuteAsync(@"
                UPDATE p
                SET p.StockReservado = CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END,
                    p.ActualizadoEl = GETUTCDATE()
                FROM Producto p INNER JOIN OrderItem oi ON oi.ProductId = p.Id
                WHERE oi.OrderId = @OrderId;", new { OrderId = orderId.Value }, tx);

            await con.ExecuteAsync(@"
                UPDATE [Order] SET Status='CANCELLED', ActualizadoEl=GETUTCDATE() WHERE Id=@Id;
                UPDATE [Transaction] SET Status='CANCELLED', ActualizadoEl=GETUTCDATE()
                WHERE OrderId=@Id AND Activo=1;", new { Id = orderId.Value }, tx);

            tx.Commit();
            return Ok(new { orderId = orderId.Value, status = "CANCELLED" });
        }

        public record RefreshRequest(int RequestId);
    }
}
