using Dapper;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BioFXAPI.Services
{
    public class OrderCancellationService
    {
        private readonly string _cs;
        private readonly IConfiguration _cfg;

        public OrderCancellationService(IConfiguration cfg)
        {
            _cfg = cfg;
            _cs = cfg.GetConnectionString("DefaultConnection");
        }

        private static bool IsFinalNegative(string s) =>
            s.Equals("REJECTED", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("EXPIRED", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Cancela localmente de forma idempotente (stock solo una vez).
        /// Opcionalmente intenta cancelar en PlacetoPay (si requestId existe).
        /// </summary>
        public async Task<(IActionResult? error, string? status)> CancelOrderAsync(
            int orderId,
            bool tryCancelGateway,
            CancellationToken ct)
        {
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync(ct);

            // Tomar la última transacción y bloquearla para evitar doble liberación/condiciones de carrera
            await using var tx = con.BeginTransaction();

            var last = await con.QueryFirstOrDefaultAsync<(int TxId, int? RequestId, string Status, string ProcessUrl)>(
                @"SELECT TOP 1 Id, RequestId, Status, ProcessUrl
                  FROM [Transaction] WITH (UPDLOCK, ROWLOCK)
                  WHERE OrderId=@Id AND Activo=1
                  ORDER BY Id DESC",
                new { Id = orderId }, tx);

            if (last == default)
            {
                await tx.RollbackAsync(ct);
                return (new BadRequestObjectResult(new { message = "Orden sin transacción." }), null);
            }

            if (last.Status.Equals("PAID", StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(ct);
                return (new BadRequestObjectResult(new { message = "La orden ya está pagada." }), null);
            }

            if (last.Status.Equals("PENDING_VALIDATION", StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(ct);
                return (new ObjectResult(new { message = "Existe un pago en validación. No se puede cancelar.", status = last.Status }) { StatusCode = 409 }, null);
            }

            // Idempotencia: si ya está final negativo, no tocar stock ni estados
            if (IsFinalNegative(last.Status))
            {
                await tx.RollbackAsync(ct);
                return (null, "CANCELLED"); // ya está cancelada/rechazada/expirada => no-op
            }

            // Intentar cancelar en PlacetoPay para cerrar la sesión en gateway
            if (tryCancelGateway && last.RequestId is int rid && rid > 0)
            {
                var gatewayOk = await TryCancelPlacetoPaySessionAsync(rid, last.ProcessUrl, ct);

                // Si gateway responde “ya aprobado”/no cancelable, es más seguro no cancelar localmente
                if (!gatewayOk)
                {
                    await tx.RollbackAsync(ct);
                    return (new ObjectResult(new { message = "No se pudo cancelar la sesión en PlacetoPay (posible aprobación o no cancelable). Refresca el estado." }) { StatusCode = 409 }, null);
                }
            }

            // Transición atómica de estado: solo procede si la orden sigue en estado cancelable.
            // Esto actúa como gate contra cancelaciones concurrentes — solo una request puede
            // ganar este UPDATE; las demás obtendrán 0 filas afectadas.
            var rowsUpdated = await con.ExecuteAsync(@"
                UPDATE [Order]
                SET Status='CANCELLED', ActualizadoEl=GETUTCDATE()
                WHERE Id=@Id
                  AND Status NOT IN ('CANCELLED','PAID','REJECTED','EXPIRED')",
                new { Id = orderId }, tx);

            if (rowsUpdated == 0)
            {
                await tx.RollbackAsync(ct);
                return (null, "CANCELLED"); // otra request ganó la carrera — idempotente
            }

            // Liberar reserva SOLO si ganamos el UPDATE de estado (garantía de una sola vez)
            await con.ExecuteAsync(@"
                UPDATE p
                SET p.StockReservado = CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END,
                    p.ActualizadoEl = GETUTCDATE()
                FROM Producto p
                INNER JOIN OrderItem oi ON oi.ProductId = p.Id
                WHERE oi.OrderId = @OrderId;", new { OrderId = orderId }, tx);

            // Marcar transacciones activas como CANCELLED
            await con.ExecuteAsync(@"
                UPDATE [Transaction]
                SET Status='CANCELLED', ActualizadoEl=GETUTCDATE()
                WHERE OrderId=@Id AND Activo=1;", new { Id = orderId }, tx);

            await tx.CommitAsync(ct);
            return (null, "CANCELLED");
        }

        private async Task<bool> TryCancelPlacetoPaySessionAsync(int requestId, string processUrl, CancellationToken ct)
        {
            // Build auth (igual que en RefreshService)
            var seed = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
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

            // BaseUri desde processUrl (como ya haces)
            var baseUri = new Uri(_cfg["PlacetoPay:BaseUrl"]);
            if (!string.IsNullOrWhiteSpace(processUrl))
            {
                var u = new Uri(processUrl);
                baseUri = new Uri($"{u.Scheme}://{u.Host}/");
            }

            using var http = new HttpClient { BaseAddress = baseUri };
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Endpoint de PlacetoPay: POST /api/session/:requestId/cancel
            var resp = await http.PostAsync(
                $"api/session/{requestId}/cancel",
                new StringContent(authJson, Encoding.UTF8, "application/json"),
                ct);

            return resp.IsSuccessStatusCode;
        }
    }
}
