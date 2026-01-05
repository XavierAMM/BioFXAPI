using BioFXAPI.Notifications;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BioFXAPI.Services
{
    public class PlacetoPayRefreshService
    {
        private readonly string _cs;
        private readonly IConfiguration _cfg;
        private readonly OrderNotificationService _orderNotificationService;

        public PlacetoPayRefreshService(IConfiguration cfg, OrderNotificationService orderNotificationService)
        {
            _cfg = cfg;
            _cs = cfg.GetConnectionString("DefaultConnection");
            _orderNotificationService = orderNotificationService;
        }

        public async Task<(IActionResult? error, RefreshResult? result)> RefreshByRequestIdAsync(
            int requestId,
            bool validateOwner,
            ClaimsPrincipal? callerUser,
            CancellationToken ct)
        {
            // ===== 0) Build auth =====
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

            await using var con = new SqlConnection(_cs);
            await con.OpenAsync(ct);

            // ===== 1) Load latest tx for requestId (needed for ProcessUrl + OrderId + current Status) =====
            var txRow = await con.QueryFirstOrDefaultAsync<(int Id, int OrderId, string Status, string ProcessUrl)>(
                @"SELECT TOP 1 Id, OrderId, Status, ProcessUrl
          FROM [Transaction]
          WHERE RequestId=@Rid
          ORDER BY Id DESC",
                new { Rid = requestId });

            if (txRow == default)
                return (new NotFoundObjectResult(new { message = "No existe transacción." }), null);

            // ===== 2) Owner-check (usuario) =====
            if (validateOwner)
            {
                var (ok, userId, err) = await TryResolveUserIdAsync(con, callerUser);
                if (!ok) return (err!, null);

                var isMine = await con.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM [Order] WHERE Id=@Id AND UserId=@Uid AND Activo=1",
                    new { Id = txRow.OrderId, Uid = userId });

                if (isMine == 0) return (new ForbidResult(), null);
            }

            // ===== 3) Determine baseUri from ProcessUrl =====
            var baseUri = new Uri(_cfg["PlacetoPay:BaseUrl"]);
            if (!string.IsNullOrWhiteSpace(txRow.ProcessUrl))
            {
                var u = new Uri(txRow.ProcessUrl);
                baseUri = new Uri($"{u.Scheme}://{u.Host}/");
            }

            using var http = new HttpClient { BaseAddress = baseUri };
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            // ===== 4) Call getRequestInformation =====
            var resp = await http.PostAsync(
                $"api/session/{requestId}",
                new StringContent(authJson, Encoding.UTF8, "application/json"),
                ct);

            var payload = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (new ObjectResult(payload) { StatusCode = (int)resp.StatusCode }, null);

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            // ===== 5) Session/global status (puede quedarse PENDING aunque haya pagos REJECTED) =====
            var statusEl = root.GetProperty("status");
            var sessionStatus = statusEl.GetProperty("status").GetString() ?? "PENDING";

            var sessionReason = statusEl.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String
                ? reasonEl.GetString()
                : null;

            var sessionMessage = statusEl.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                ? msgEl.GetString()
                : null;

            // ===== 6) Select the "winning" payment (iterate all payments) =====
            JsonElement? selectedPayment = null;
            string? selectedPaymentStatus = null;
            DateTime? selectedPaymentDate = null;

            if (root.TryGetProperty("payment", out var payArr) &&
                payArr.ValueKind == JsonValueKind.Array &&
                payArr.GetArrayLength() > 0)
            {
                selectedPayment = SelectBestPayment(payArr, out selectedPaymentStatus, out selectedPaymentDate);
            }

            // ===== 7) Extract fields from selected payment (if any) =====
            string? paymentMethod = null;
            string? paymentMethodName = null;
            string? issuerName = null;
            int? internalReference = null;
            bool? refunded = null;
            decimal? refundedAmount = null; // corregido: NO inferir desde amount.total
            string? authorization = null;

            // Para reason/message: si hay payment seleccionado, usar su status como fuente primaria
            string? reason = sessionReason;
            string? message = sessionMessage;

            if (selectedPayment.HasValue)
            {
                var p0 = selectedPayment.Value;

                // payment.status (objeto)
                if (p0.TryGetProperty("status", out var pStatusObj) && pStatusObj.ValueKind == JsonValueKind.Object)
                {
                    if (pStatusObj.TryGetProperty("reason", out var pr) && pr.ValueKind == JsonValueKind.String)
                        reason = pr.GetString();
                    if (pStatusObj.TryGetProperty("message", out var pm) && pm.ValueKind == JsonValueKind.String)
                        message = pm.GetString();
                }

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
                    refunded = refEl.GetBoolean();

                if (p0.TryGetProperty("authorization", out var authEl) && authEl.ValueKind == JsonValueKind.String)
                    authorization = authEl.GetString();

                // refundedAmount: solo si viene explícito (propiedad o processorFields), NO desde amount.total
                if (p0.TryGetProperty("refundedAmount", out var raEl))
                {
                    if (raEl.ValueKind == JsonValueKind.Number && raEl.TryGetDecimal(out var raNum))
                        refundedAmount = raNum;
                    else if (raEl.ValueKind == JsonValueKind.String && decimal.TryParse(raEl.GetString(), out var raNum2))
                        refundedAmount = raNum2;
                }

                if (!refundedAmount.HasValue &&
                    p0.TryGetProperty("processorFields", out var pfArr) &&
                    pfArr.ValueKind == JsonValueKind.Array)
                {
                    refundedAmount = TryReadDecimalProcessorField(pfArr, "refundedAmount");
                }

                if (refunded.HasValue && refunded.Value == false)
                    refundedAmount = null;
            }

            // ===== 8) Compute target status using payment priority first, then session =====
            var hasPendingAttempt = false;
            if (root.TryGetProperty("payment", out var payArr2) &&
                payArr2.ValueKind == JsonValueKind.Array &&
                payArr2.GetArrayLength() > 0)
            {
                hasPendingAttempt = HasPendingPaymentAttempt(payArr2);
            }

            var target = ComputeTargetStatus(sessionStatus, selectedPaymentStatus, hasPendingAttempt, reason, message);

            // ===== 9) Idempotency + concurrency-safe apply (webhook vs refresh) =====
            // Abrimos transacción DB y bloqueamos la fila de Transaction para evitar doble stock/emails.
            await using var dbtx = con.BeginTransaction();

            // Bloquea estado actual (evita carreras)
            var currentTxStatus = await con.ExecuteScalarAsync<string>(
                @"SELECT Status
          FROM [Transaction] WITH (UPDLOCK, ROWLOCK)
          WHERE Id=@Id;",
                new { Id = txRow.Id }, dbtx);

            currentTxStatus ??= txRow.Status;

            var effective = ApplyFinalStateIdempotency(currentTxStatus, target);

            // Detectar transición a PAID (solo una vez debido al lock)
            var justPaid = !string.Equals(currentTxStatus, "PAID", StringComparison.OrdinalIgnoreCase)
                           && string.Equals(effective, "PAID", StringComparison.OrdinalIgnoreCase);

            // Detectar transición a estado final negativo (para liberar reservado una sola vez)
            var justFinalNegative =
                !IsFinalNegative(currentTxStatus) &&
                IsFinalNegative(effective);

            // ===== 10) Stock side-effects (idempotent) =====
            if (justPaid)
            {
                await con.ExecuteAsync(@"
                UPDATE p
                SET 
                    -- 1) Stock nunca negativo (defensivo)
                    p.Stock = CASE 
                                WHEN COALESCE(p.Stock,0) >= oi.Quantity THEN COALESCE(p.Stock,0) - oi.Quantity
                                ELSE 0
                              END,

                    -- 2) Reservado nunca negativo (defensivo)
                    p.StockReservado = CASE 
                                        WHEN COALESCE(p.StockReservado,0) >= oi.Quantity THEN COALESCE(p.StockReservado,0) - oi.Quantity
                                        ELSE 0 
                                      END,

                    -- 3) Disponible se calcula usando los RESULTADOS (mismo cálculo que 1 y 2)
                    p.Disponible = CASE 
                        WHEN
                          (
                            CASE 
                              WHEN COALESCE(p.Stock,0) >= oi.Quantity THEN COALESCE(p.Stock,0) - oi.Quantity
                              ELSE 0
                            END
                          )
                          -
                          (
                            CASE 
                              WHEN COALESCE(p.StockReservado,0) >= oi.Quantity THEN COALESCE(p.StockReservado,0) - oi.Quantity
                              ELSE 0 
                            END
                          ) > 0
                        THEN 1 ELSE 0 END,

                    p.ActualizadoEl = GETUTCDATE()
                FROM Producto p
                INNER JOIN OrderItem oi ON oi.ProductId = p.Id
                WHERE oi.OrderId = @OrderId;",
                 new { OrderId = txRow.OrderId }, dbtx);

            }
            else if (justFinalNegative) // REJECTED/EXPIRED/CANCELLED desde no-final
            {
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
                    WHERE oi.OrderId = @OrderId;", new { OrderId = txRow.OrderId }, dbtx);
            }

            // ===== 11) Update Order + Transaction (status + fields) =====
            await con.ExecuteAsync(
                @"UPDATE [Order]
          SET Status=@E, ActualizadoEl=GETUTCDATE()
          WHERE Id=@Id;

          UPDATE [Transaction]
          SET Status=@E,
              Activo = CASE WHEN @E = 'CANCELLED' THEN 0 ELSE Activo END,
              InternalReference = COALESCE(@InternalReference, InternalReference),
              Reason            = COALESCE(@Reason, Reason),
              Message           = COALESCE(@Message, Message),
              PaymentMethod     = COALESCE(@PaymentMethod, PaymentMethod),
              PaymentMethodName = COALESCE(@PaymentMethodName, PaymentMethodName),
              IssuerName        = COALESCE(@IssuerName, IssuerName),
              Refunded          = CASE WHEN @Refunded IS NULL THEN Refunded ELSE @Refunded END,
              RefundedAmount    = CASE WHEN @RefundedAmount IS NULL THEN RefundedAmount ELSE @RefundedAmount END,
              [Authorization]   = COALESCE(@Authorization, [Authorization]),
              ActualizadoEl     = GETUTCDATE()
          WHERE Id=@TxId;",
                new
                {
                    Id = txRow.OrderId,
                    E = effective,
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

            await dbtx.CommitAsync(ct);

            // ===== 12) Email side-effect (idempotent) =====
            if (justPaid)
            {
                try
                {
                    await _orderNotificationService.SendOrderPaidNotificationsAsync(
                        txRow.OrderId, requestId, ct);
                }
                catch
                {
                    // no romper
                }
            }

            return (null, new RefreshResult(txRow.OrderId, requestId, effective, justPaid));
        }


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

        private static JsonElement SelectBestPayment(JsonElement payArr, out string? status, out DateTime? date)
        {
            // Preferencia: APPROVED más reciente; luego REJECTED/FAILED más reciente; si no, más reciente por fecha; si no, primero.
            JsonElement? bestApproved = null;
            DateTime? bestApprovedDate = null;

            JsonElement? bestRejected = null;
            DateTime? bestRejectedDate = null;

            JsonElement? bestAny = null;
            DateTime? bestAnyDate = null;

            foreach (var p in payArr.EnumerateArray())
            {
                var (ps, pd) = ReadPaymentStatusAndDate(p);

                if (pd.HasValue)
                {
                    if (bestAnyDate == null || pd > bestAnyDate)
                    {
                        bestAnyDate = pd;
                        bestAny = p;
                    }
                }
                else if (bestAny == null)
                {
                    bestAny = p;
                }

                if (string.Equals(ps, "APPROVED", StringComparison.OrdinalIgnoreCase))
                {
                    if (bestApprovedDate == null || (pd.HasValue && pd > bestApprovedDate))
                    {
                        bestApprovedDate = pd;
                        bestApproved = p;
                    }
                }

                if (string.Equals(ps, "REJECTED", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ps, "FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    if (bestRejectedDate == null || (pd.HasValue && pd > bestRejectedDate))
                    {
                        bestRejectedDate = pd;
                        bestRejected = p;
                    }
                }
            }

            JsonElement selected;
            if (bestApproved.HasValue)
            {
                selected = bestApproved.Value;
                status = "APPROVED";
                date = bestApprovedDate;
                return selected;
            }

            if (bestRejected.HasValue)
            {
                selected = bestRejected.Value;
                status = ReadPaymentStatusAndDate(selected).status;
                date = bestRejectedDate;
                return selected;
            }

            selected = bestAny ?? payArr[0];
            (status, date) = ReadPaymentStatusAndDate(selected);
            return selected;
        }

        private static (string? status, DateTime? date) ReadPaymentStatusAndDate(JsonElement payment)
        {
            string? st = null;
            DateTime? dt = null;

            if (payment.TryGetProperty("status", out var psObj) && psObj.ValueKind == JsonValueKind.Object)
            {
                if (psObj.TryGetProperty("status", out var stEl) && stEl.ValueKind == JsonValueKind.String)
                    st = stEl.GetString();

                if (psObj.TryGetProperty("date", out var dEl) && dEl.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(dEl.GetString(), out var parsed))
                    dt = parsed;
            }

            return (st, dt);
        }

        private static bool HasPendingPaymentAttempt(JsonElement payArr)
        {
            foreach (var p in payArr.EnumerateArray())
            {
                // 1) Si el payment ya es final, no cuenta como "pendiente en validación"
                var (ps, _) = ReadPaymentStatusAndDate(p);
                var ups = (ps ?? "").Trim().ToUpperInvariant();

                if (ups is "APPROVED" or "REJECTED" or "FAILED")
                    continue;

                // 2) Señal directa: status del payment indica proceso/validación
                if (ups is "PENDING" or "PENDING_VALIDATION" or "PENDING_PROCESS" or "PROCESSING")
                    return true;

                // 3) Señal indirecta fuerte: hubo intento real (internalReference)
                if (p.TryGetProperty("internalReference", out var irEl))
                {
                    if (irEl.ValueKind == JsonValueKind.Number)
                        return true;

                    if (irEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(irEl.GetString()))
                        return true;
                }

            }

            return false;
        }

        private static string ComputeTargetStatus(string sessionStatus, string? paymentStatus, bool hasPendingPaymentAttempt, string? reason, string? message)
        {
            // 1) Payment manda (resultado financiero) - APPROVED debe ganar siempre
            if (!string.IsNullOrWhiteSpace(paymentStatus))
            {
                var ps = paymentStatus.Trim().ToUpperInvariant();
                if (ps == "APPROVED") return "PAID";
            }

            // 0) Cancelación por usuario
            if (IsUserCancelled(reason, message))
                return "CANCELLED";

            if (IsExpired(reason, message))
                return "EXPIRED";

            // 2) Payment manda - resto
            if (!string.IsNullOrWhiteSpace(paymentStatus))
            {
                var ps = paymentStatus.Trim().ToUpperInvariant();
                if (ps is "REJECTED" or "FAILED") return "REJECTED";

                if (ps is "PENDING" or "PENDING_VALIDATION" or "PENDING_PROCESS" or "PROCESSING")
                    return "PENDING_VALIDATION";
            }

            // 3) Session status (defensivo)
            var ss = (sessionStatus ?? "PENDING").Trim().ToUpperInvariant();

            if (ss is "APPROVED" or "OK") return "PAID";
            if (ss is "REJECTED" or "FAILED") return "REJECTED";
            if (ss == "EXPIRED") return "EXPIRED";
            if (ss is "PENDING_VALIDATION" or "PENDING_PROCESS") return "PENDING_VALIDATION";

            // 4) ss = PENDING pero hay intentos en curso
            if (ss == "PENDING" && hasPendingPaymentAttempt) return "PENDING_VALIDATION";

            // 5) Pendiente por usuario/abandono
            return "PENDING";
        }

        private static bool IsExpired(string? reason, string? message)
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                var r = reason.Trim().ToUpperInvariant();
                if (r == "EX") return true;
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                var m = message.Trim().ToLowerInvariant();
                // Mensajes típicos: "La petición ha expirado"
                if (m.Contains("expir"))
                    return true;
            }

            return false;
        }

        private static bool IsUserCancelled(string? reason, string? message)
        {
            // 1) Señal primaria: reason explícito observado en tu ambiente
            if (!string.IsNullOrWhiteSpace(reason))
            {
                var r = reason.Trim().ToUpperInvariant();
                if (r == "?C" || r == "UC" || r == "CANCELLED" || r == "CANCELED")
                    return true;
            }

            // 2) Fallback: mensaje explícito de cancelación por usuario
            if (!string.IsNullOrWhiteSpace(message))
            {
                var m = message.Trim().ToLowerInvariant();
                var hasCancelWord =
                    m.Contains("cancelada") || m.Contains("cancelado") || m.Contains("cancelación") || m.Contains("cancelacion");

                // hacerlo más específico: "por el usuario" (no solo "usuario")
                var byUser =
                    m.Contains("por el usuario") || m.Contains("por usuario");

                if (hasCancelWord && byUser)
                    return true;
            }

            return false;
        }

        private static bool IsFinal(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.ToUpperInvariant();
            return s is "PAID" or "REJECTED" or "EXPIRED" or "CANCELLED";
        }

        private static bool IsFinalNegative(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.ToUpperInvariant();
            return s is "REJECTED" or "EXPIRED" or "CANCELLED";
        }

        private static string ApplyFinalStateIdempotency(string current, string target)
        {
            current = (current ?? "PENDING").ToUpperInvariant();
            target = (target ?? "PENDING").ToUpperInvariant();

            // 1) PAID nunca se sobreescribe
            if (current == "PAID") return "PAID";

            // 2) CANCELLED/EXPIRED no se reviven (evita conflictos con cancelación local)
            if (current is "CANCELLED" or "EXPIRED") return current;

            // 3) No permitir downgrade a pendiente desde estados finales
            if ((target == "PENDING" || target == "PENDING_VALIDATION") && IsFinal(current))
                return current;

            // 4) Permitir upgrade REJECTED->PAID si llega un APPROVED posterior
            if (current == "REJECTED" && target == "PAID")
                return "PAID";

            return target;
        }

        private static decimal? TryReadDecimalProcessorField(JsonElement processorFields, string keyword)
        {
            foreach (var pf in processorFields.EnumerateArray())
            {
                if (!pf.TryGetProperty("keyword", out var kEl) || kEl.ValueKind != JsonValueKind.String)
                    continue;

                if (!string.Equals(kEl.GetString(), keyword, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!pf.TryGetProperty("value", out var vEl))
                    continue;

                if (vEl.ValueKind == JsonValueKind.Number && vEl.TryGetDecimal(out var vNum))
                    return vNum;

                if (vEl.ValueKind == JsonValueKind.String && decimal.TryParse(vEl.GetString(), out var vNum2))
                    return vNum2;
            }

            return null;
        }


        public record RefreshResult(int OrderId, int RequestId, string Status, bool JustPaid);
    }
}
