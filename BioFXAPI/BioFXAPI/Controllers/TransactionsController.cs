using BioFXAPI.Notifications;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BioFXAPI.Services;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TransactionsController : ControllerBase
    {
        private readonly string _cs;
        private readonly IConfiguration _cfg;        
        private readonly PlacetoPayRefreshService _refresh;

        public TransactionsController(IConfiguration cfg,  PlacetoPayRefreshService refresh)
        {
            _cfg = cfg;
            _cs = cfg.GetConnectionString("DefaultConnection");            
            _refresh = refresh;
        }

        [Authorize]
        [HttpPost("refresh-by-request")]
        public async Task<IActionResult> RefreshByRequestId([FromBody] RefreshRequest r, CancellationToken ct)
        {
            var (error, result) = await _refresh.RefreshByRequestIdAsync(
            requestId: r.RequestId,
            validateOwner: true,
            callerUser: User,
            ct: ct);

            if (error != null) return error;
            return Ok(new { orderId = result!.OrderId, status = result.Status });
        }

        [AllowAnonymous]
        [HttpPost("internal/refresh-by-request")]
        public async Task<IActionResult> RefreshByRequestIdInternal([FromBody] RefreshRequest r, CancellationToken ct)
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

            var (error, result) = await _refresh.RefreshByRequestIdAsync(
            requestId: r.RequestId,
            validateOwner: false,
            callerUser: null,
            ct: ct);

            if (error != null) return error;
            return Ok(new { orderId = result!.OrderId, status = result.Status });
        }

        public record RefreshRequest(int RequestId);

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

        [Authorize]
        [HttpPost("cancel-by-request")]
        public async Task<IActionResult> CancelByRequest([FromBody] RefreshRequest r)
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var orderId = await con.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 OrderId FROM [Transaction] WHERE RequestId=@Rid ORDER BY Id DESC",
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

            // ✅ NUEVO: no permitir cancelar si está en validación/processing
            if (string.Equals(last.Status, "PENDING_VALIDATION", StringComparison.OrdinalIgnoreCase))
                return StatusCode(409, new
                {
                    message = "Existe un pago en validación. No se puede cancelar en este momento. Refresca el estado.",
                    status = last.Status
                });

            using var tx = con.BeginTransaction();

            // Lock del Order.Status para idempotencia
            var currentOrderStatus = await con.ExecuteScalarAsync<string>(@"
                SELECT Status
                FROM [Order] WITH (UPDLOCK, ROWLOCK)
                WHERE Id=@Id AND Activo=1;",
                new { Id = orderId.Value }, tx);

            currentOrderStatus ??= "PENDING";

            bool isFinal =
                currentOrderStatus.Equals("PAID", StringComparison.OrdinalIgnoreCase) ||
                currentOrderStatus.Equals("REJECTED", StringComparison.OrdinalIgnoreCase) ||
                currentOrderStatus.Equals("EXPIRED", StringComparison.OrdinalIgnoreCase) ||
                currentOrderStatus.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);

            if (isFinal)
            {
                if (currentOrderStatus.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
                {
                    await con.ExecuteAsync(@"
                        UPDATE [Transaction]
                        SET Activo = 0,
                            ActualizadoEl = GETUTCDATE()
                        WHERE OrderId = @Id
                          AND Status = 'CANCELLED'
                          AND Activo = 1;",
                    new { Id = orderId.Value }, tx);
                }

                tx.Commit();
                return Ok(new { orderId = orderId.Value, status = currentOrderStatus, idempotent = true });
            }


            // Side-effect único
            await con.ExecuteAsync(@"
                UPDATE p
                SET p.StockReservado = CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END,
                    p.Disponible = CASE 
                        WHEN (COALESCE(p.Stock,0) - 
                              (CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END)
                             ) > 0 THEN 1 ELSE 0 END,
                    p.ActualizadoEl = GETUTCDATE()
                FROM Producto p 
                INNER JOIN OrderItem oi ON oi.ProductId = p.Id
                WHERE oi.OrderId = @OrderId;",
                new { OrderId = orderId.Value }, tx);

            // Marcar cancel
            await con.ExecuteAsync(@"
                UPDATE [Order] SET Status='CANCELLED', ActualizadoEl=GETUTCDATE() WHERE Id=@Id;

                UPDATE [Transaction]
                    SET Status='CANCELLED',
                        Activo=0,
                        ActualizadoEl=GETUTCDATE()
                    WHERE OrderId=@Id AND Activo=1;",
                new { Id = orderId.Value }, tx);

            tx.Commit();
            return Ok(new { orderId = orderId.Value, status = "CANCELLED" });

        }


    }
}
