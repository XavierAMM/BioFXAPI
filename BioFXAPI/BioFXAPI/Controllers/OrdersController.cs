using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BioFXAPI.Services;
using Microsoft.AspNetCore.Http;


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
        private readonly ILogger<OrdersController> _logger;
        private readonly IFileStorageService _fileStorage;

        public OrdersController(IConfiguration cfg, ILogger<OrdersController> logger, IFileStorageService fileStorage)
        {
            _cfg = cfg;
            _cs = cfg.GetConnectionString("DefaultConnection");
            _http = new HttpClient { BaseAddress = new Uri(_cfg["PlacetoPay:BaseUrl"]) };
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _logger = logger;
            _fileStorage = fileStorage;
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateFromCart([FromBody] CreateOrderRequest req)
        {
            if (req is null) return BadRequest(new { message = "Cuerpo inválido." });
            _logger.LogInformation("CreateFromCart llamado. Reference={Reference}, Desc={Desc}",
             req.Reference, req.Description);

            try
            {
                await using var con = new SqlConnection(_cs);
                await con.OpenAsync();

                await using var tx = con.BeginTransaction();
                var (ok, userId, err) = await TryResolveUserIdAsync(con, tx);
                if (!ok)
                {
                    try { await tx.RollbackAsync(); } catch { }
                    return err!;
                }

                try
                {
                    var cartId = await con.ExecuteScalarAsync<int?>(
                        "SELECT TOP 1 Id FROM ShoppingCart WHERE UserId=@UserId AND Activo=1 ORDER BY Id DESC",
                        new { UserId = userId }, tx);
                    if (!cartId.HasValue)
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new { message = "Carrito vacío." });
                    }

                    var items = await con.QueryAsync<(int ProductId, int Quantity, decimal UnitPrice)>(
                        @"SELECT ci.ProductId,
                         ci.Quantity,
                         CAST(p.Precio AS decimal(18,2)) AS UnitPrice
                  FROM CartItem ci
                  INNER JOIN Producto p ON p.Id=ci.ProductId
                  WHERE ci.CartId=@Cart AND ci.Activo=1",
                        new { Cart = cartId.Value }, tx);

                    if (!items.Any())
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new { message = "Carrito sin items." });
                    }

                    var total = items.Sum(i => i.Quantity * i.UnitPrice);
                    var tax = 0m;
                    var referenceRaw = req.Reference ?? $"BIO-{DateTime.UtcNow:yyyyMMddHHmmss}-{userId}";
                    var reference = referenceRaw.Length > 32 ? referenceRaw[..32] : referenceRaw;

                    string NewOrderNumber(int uid) =>
                        $"ORD-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{uid}-{RandomNumberGenerator.GetInt32(1000, 9999)}";

                    var attempts = 0;
                    int orderId = 0;
                    string orderNumber = "";

                    // Normalizar campos de documento/dirección/médico
                    var docType = string.IsNullOrWhiteSpace(req.DocumentType) ? "" : req.DocumentType.Trim();
                    var docNumber = string.IsNullOrWhiteSpace(req.DocumentNumber) ? "" : req.DocumentNumber.Trim();
                    var address = string.IsNullOrWhiteSpace(req.AddressLine) ? "" : req.AddressLine.Trim();
                    var city = string.IsNullOrWhiteSpace(req.City) ? "" : req.City.Trim();
                    var province = string.IsNullOrWhiteSpace(req.Province) ? "" : req.Province.Trim();
                    var country = string.IsNullOrWhiteSpace(req.Country) ? "" : req.Country.Trim();

                    // PostalCode y DoctorName sí los podemos dejar en NULL si quieres
                    var postalCode = string.IsNullOrWhiteSpace(req.PostalCode) ? null : req.PostalCode.Trim();
                    var doctorName = string.IsNullOrWhiteSpace(req.DoctorName) ? null : req.DoctorName.Trim();

                    while (true)
                    {
                        attempts++;
                        orderNumber = NewOrderNumber(userId);

                        try
                        {
                            orderId = await con.ExecuteScalarAsync<int>(
                                @"INSERT INTO [Order](
                                    UserId,
                                    OrderNumber,
                                    Reference,
                                    Description,
                                    TotalAmount,
                                    TaxAmount,
                                    Currency,
                                    Status,
                                    DocumentType,
                                    DocumentNumber,
                                    AddressLine,
                                    City,
                                    Province,
                                    PostalCode,
                                    Country,
                                    DoctorName,
                                    CreadoEl,
                                    ActualizadoEl,
                                    Activo
                              )
                              OUTPUT INSERTED.Id
                              VALUES(
                                    @Uid,
                                    @OrderNumber,
                                    @Reference,
                                    @Desc,
                                    @Total,
                                    @Tax,
                                    'USD',
                                    'PENDING',
                                    @DocumentType,
                                    @DocumentNumber,
                                    @AddressLine,
                                    @City,
                                    @Province,
                                    @PostalCode,
                                    @Country,
                                    @DoctorName,
                                    GETUTCDATE(),
                                    GETUTCDATE(),
                                    1
                              )",
                                new
                                {
                                    Uid = userId,
                                    OrderNumber = orderNumber,
                                    Reference = reference,
                                    Desc = req.Description ?? "Compra BioFX",
                                    Total = total,
                                    Tax = tax,
                                    DocumentType = docType,
                                    DocumentNumber = docNumber,
                                    AddressLine = address,
                                    City = city,
                                    Province = province,
                                    PostalCode = (object?)postalCode ?? DBNull.Value,
                                    Country = country,
                                    DoctorName = (object?)doctorName ?? DBNull.Value
                                },
                                tx);

                            break; // insert OK
                        }
                        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                        {
                            if (attempts >= 3) throw;
                        }
                    }


                    foreach (var it in items)
                    {
                        await con.ExecuteAsync(
                            @"INSERT INTO OrderItem(OrderId, ProductId, Quantity, UnitPrice, TotalPrice, CreadoEl, Activo)
                      VALUES(@Oid, @Pid, @Qty, @Price, @Sub, GETUTCDATE(), 1)",
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
                      SET StockReservado = COALESCE(StockReservado,0) + @Qty,
                          ActualizadoEl = GETUTCDATE()
                      WHERE Id = @Pid",
                            new { Pid = it.ProductId, Qty = it.Quantity }, tx);
                    }

                    await con.ExecuteAsync(
                        "UPDATE CartItem SET Activo=0 WHERE CartId=@Cart AND Activo=1",
                        new { Cart = cartId.Value }, tx);

                    await tx.CommitAsync();

                    _logger.LogInformation("Orden creada correctamente. OrderId={OrderId}, UserId={UserId}, Total={Total}",
                     orderId, userId, total);

                    return Ok(new
                    {
                        id = orderId,
                        orderId = orderId,
                        OrderNumber = orderNumber,
                        Reference = reference,
                        Total = total,
                        Currency = "USD"
                    });
                }
                catch (SqlException ex)
                {
                    try { await tx.RollbackAsync(); } catch { }
                    return StatusCode(500, new { message = "Error de base de datos.", code = ex.Number, error = ex.Message });
                }
                catch (Exception ex)
                {
                    try { await tx.RollbackAsync(); } catch { }
                    return StatusCode(500, new { message = "Error interno al crear la orden.", error = ex.Message });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error interno (CreateFromCart outer).",
                    error = ex.Message,
                    stack = ex.StackTrace
                });
            }
        }

        [HttpPost("{orderId:int}/attachment")]
        [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
        public async Task<IActionResult> UploadOrderAttachment(int orderId, IFormFile file, CancellationToken ct)
        {
            _logger.LogInformation("UploadOrderAttachment llamado para OrderId={OrderId}. FileName={FileName}, Length={Length}, ContentType={ContentType}",
                orderId,
                file?.FileName,
                file?.Length ?? 0,
                file?.ContentType);
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Archivo requerido." });

            // Solo aceptamos PDF o imágenes
            if (!EsAdjuntoValido(file.FileName, file.ContentType))
            {
                return BadRequest(new
                {
                    message = "Solo se permiten archivos PDF o imágenes (PNG, JPG, JPEG) para la receta médica."
                });
            }


            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            // Resolver usuario autenticado
            var (ok, userId, err) = await TryResolveUserIdAsync(con, null);
            if (!ok) return err!;

            // Verificar que la orden existe, está activa y es del usuario
            var orderRow = await con.QueryFirstOrDefaultAsync<(int UserId, int? AttachmentId)>(
                @"SELECT UserId, OrderAttachmentId
          FROM [Order]
          WHERE Id = @Id AND Activo = 1",
                new { Id = orderId });

            if (orderRow == default)
                return NotFound(new { message = "Orden no encontrada." });

            if (orderRow.UserId != userId)
                return Forbid();

            if (orderRow.AttachmentId.HasValue)
            {
                return BadRequest(new { message = "La orden ya tiene una factura asociada." });
            }

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".bin";
            }

            var key = $"orders/{orderId}/attachments/{Guid.NewGuid():N}{ext}";
            var uploadedToS3 = false;


            try
            {
                // 1) Subir a S3
                await using (var stream = file.OpenReadStream())
                {
                    await _fileStorage.UploadAsync(stream, key, file.ContentType, ct);
                    uploadedToS3 = true;
                }

                // 2) Insertar en OrderAttachment + actualizar Order.OrderAttachmentId
                using var tx = con.BeginTransaction();

                var attachmentId = await con.ExecuteScalarAsync<int>(
                    @"INSERT INTO [OrderAttachment](
                    FileName,
                    ContentType,
                    FileSize,
                    StorageKey,
                    Tipo,
                    Activo,
                    CreadoEl,
                    ActualizadoEl
              )
              OUTPUT INSERTED.Id
              VALUES(
                    @FileName,
                    @ContentType,
                    @FileSize,
                    @StorageKey,
                    @Tipo,
                    1,
                    GETUTCDATE(),
                    GETUTCDATE()
              );",
                    new
                    {
                        FileName = file.FileName,
                        ContentType = file.ContentType,
                        FileSize = file.Length,
                        StorageKey = key,
                        Tipo = "FACTURA"
                    },
                    tx
                );

                await con.ExecuteAsync(
                    @"UPDATE [Order]
              SET OrderAttachmentId = @AttachmentId,
                  ActualizadoEl = GETUTCDATE()
              WHERE Id = @OrderId AND Activo = 1;",
                    new { AttachmentId = attachmentId, OrderId = orderId },
                    tx
                );

                await tx.CommitAsync(ct);

                _logger.LogInformation(
                "Adjunto guardado correctamente. OrderId={OrderId}, AttachmentId={AttachmentId}, Key={Key}",
                orderId, attachmentId, key);

                return Ok(new
                {
                    orderId,
                    attachmentId,
                    fileName = file.FileName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al asociar factura a la orden {OrderId}.", orderId);

                if (uploadedToS3)
                {
                    try
                    {
                        await _fileStorage.DeleteAsync(key, ct);
                        _logger.LogInformation(
                            "Se eliminó de S3 el archivo {Key} tras fallo al guardar en BD para la orden {OrderId}.",
                            key, orderId);
                    }
                    catch (Exception delEx)
                    {
                        _logger.LogError(delEx,
                            "Error al intentar eliminar de S3 el archivo {Key} tras fallo para la orden {OrderId}.",
                            key, orderId);
                    }
                }

                return StatusCode(500, new
                {
                    message = "Error al guardar la factura de la orden.",
                    error = ex.Message
                });
            }
        }


        [HttpPost("{orderId:int}/placetopay/session")]
        public async Task<IActionResult> CreatePlacetoPaySession(int orderId, [FromBody] ReturnUrlRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ReturnUrl))
                return BadRequest(new { message = "returnUrl requerido." });

            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var (ok, userId, err) = await TryResolveUserIdAsync(con, null);
            if (!ok) return err!;

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

            var seed = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);

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
                @"SELECT TOP 1 p.Nombre,
                         p.Apellido,
                         u.email AS Email,
                         p.Telefono
                  FROM Persona p
                  INNER JOIN Usuario u ON u.id = p.UsuarioId
                  WHERE p.UsuarioId = (SELECT UserId FROM [Order] WHERE Id=@Id)
                    AND p.Activo = 1
                    AND u.Activo = 1",
                new { Id = orderId });

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
            _logger.LogInformation("PlacetoPay CreateSession para Order {OrderId}. JSON: {Json}", orderId, json);
            var resp = await _http.PostAsync("api/session", new StringContent(json, Encoding.UTF8, "application/json"));
            var payload = await resp.Content.ReadAsStringAsync();

            _logger.LogInformation("PlacetoPay respuesta para Order {OrderId}. StatusCode={StatusCode}, Payload={Payload}",
                orderId, (int)resp.StatusCode, payload);

            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, payload);

            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("requestId", out var ridEl) || !ridEl.TryGetInt32(out var requestId))
                return StatusCode(502, new { message = "Respuesta sin requestId.", raw = payload });
            var processUrl = doc.RootElement.GetProperty("processUrl").GetString() ?? "";

            await con.ExecuteAsync(
                @"INSERT INTO [Transaction](OrderId, RequestId, InternalReference, ProcessUrl, Status, Reason, Message, PaymentMethod, PaymentMethodName, IssuerName, Refunded, RefundedAmount, CreadoEl, ActualizadoEl, Activo)
                  VALUES(@OrderId, @RequestId, NULL, @ProcessUrl, 'PENDING', NULL, NULL, NULL, NULL, NULL, 0, NULL, GETUTCDATE(), GETUTCDATE(), 1)",
                new { OrderId = orderId, RequestId = requestId, ProcessUrl = processUrl });

            return Ok(new { requestId, processUrl });
        }

        [HttpGet("{orderId:int}/status")]
        public async Task<IActionResult> RefreshStatus(int orderId)
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var (ok, userId, err) = await TryResolveUserIdAsync(con, null);
            if (!ok) return err!;

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
            var root = doc.RootElement;

            // 1) status global de la solicitud
            var statusEl = root.GetProperty("status");
            var status = statusEl.GetProperty("status").GetString();

            var reason = statusEl.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String
                ? reasonEl.GetString()
                : null;

            var message = statusEl.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                ? msgEl.GetString()
                : null;

            // 2) datos específicos del pago (primer elemento de payment[])
            string? paymentMethod = null;
            string? paymentMethodName = null;
            string? issuerName = null;
            int? internalReference = null;
            bool? refunded = null;
            decimal? refundedAmount = null;

            if (root.TryGetProperty("payment", out var payArr) && payArr.ValueKind == JsonValueKind.Array && payArr.GetArrayLength() > 0)
            {
                var p0 = payArr[0];

                // estado del pago (puedes usarlo si quieres diferenciar)
                var payStatus = p0.TryGetProperty("status", out var payStEl) && payStEl.ValueKind == JsonValueKind.Object
                    ? payStEl.GetProperty("status").GetString()
                    : null;

                // internalReference (viene como string en tu ejemplo)
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


                // refundedAmount: si quisieras tomar el total como "monto afectado" en caso de reembolso
                if (p0.TryGetProperty("amount", out var amountEl) && amountEl.ValueKind == JsonValueKind.Object)
                {
                    if (amountEl.TryGetProperty("to", out var toEl) && toEl.ValueKind == JsonValueKind.Object)
                    {
                        if (toEl.TryGetProperty("total", out var totEl) && totEl.ValueKind == JsonValueKind.Number && totEl.TryGetDecimal(out var tot))
                        {
                            refundedAmount = tot;
                        }
                    }
                }
            }

            // 3) mapping del estado a tu modelo local
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
            p.ActualizadoEl = GETUTCDATE()
        FROM Producto p INNER JOIN OrderItem oi ON oi.ProductId = p.Id
        WHERE oi.OrderId = @OrderId;", new { OrderId = orderId }, dbtx);
            }
            else if (mapped == "REJECTED" || mapped == "EXPIRED")
            {
                await con.ExecuteAsync(@"
        UPDATE p
        SET p.StockReservado = CASE WHEN p.StockReservado >= oi.Quantity THEN p.StockReservado - oi.Quantity ELSE 0 END,
            p.ActualizadoEl = GETUTCDATE()
        FROM Producto p INNER JOIN OrderItem oi ON oi.ProductId = p.Id
        WHERE oi.OrderId = @OrderId;", new { OrderId = orderId }, dbtx);
            }

            // 4) Actualizar Order + Transaction con datos enriquecidos
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
          ActualizadoEl    = GETUTCDATE()
      WHERE Id=@TxId;",
                new
                {
                    Id = orderId,
                    E = mapped,
                    TxId = txRow.Id,
                    InternalReference = (object?)internalReference ?? DBNull.Value,
                    Reason = (object?)reason ?? DBNull.Value,
                    Message = (object?)message ?? DBNull.Value,
                    PaymentMethod = (object?)paymentMethod ?? DBNull.Value,
                    PaymentMethodName = (object?)paymentMethodName ?? DBNull.Value,
                    IssuerName = (object?)issuerName ?? DBNull.Value,
                    Refunded = refunded.HasValue ? (refunded.Value ? 1 : 0) : (int?)null,
                    RefundedAmount = (object?)refundedAmount ?? DBNull.Value
                }, dbtx);

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
            var (ok, userId, err) = await TryResolveUserIdAsync(con, null);
            if (!ok) return err!;

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
            p.ActualizadoEl = GETUTCDATE()
        FROM Producto p INNER JOIN OrderItem oi ON oi.ProductId = p.Id
        WHERE oi.OrderId = @OrderId;", new { OrderId = orderId }, tx);

            await con.ExecuteAsync(@"
        UPDATE [Order] SET Status='CANCELLED', ActualizadoEl=GETUTCDATE() WHERE Id=@Id;
        UPDATE [Transaction] SET Status='CANCELLED', ActualizadoEl=GETUTCDATE()
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

            var (ok, userId, err) = await TryResolveUserIdAsync(con, null);
            if (!ok) return err!;

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

        private async Task<(bool ok, int userId, IActionResult? error)> TryResolveUserIdAsync(SqlConnection con, SqlTransaction? tx)
        {
            var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("nameid")?.Value
                     ?? User.FindFirst("uid")?.Value;
            if (int.TryParse(idStr, out var uid1))
                return (true, uid1, null);

            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var uid2 = await con.ExecuteScalarAsync<int?>(
                    "SELECT TOP 1 id FROM Usuario WHERE email=@Email AND Activo=1",
                    new { Email = email }, tx);
                if (uid2.HasValue) return (true, uid2.Value, null);
            }

            return (false, 0, Unauthorized(new { message = "Usuario no identificado." }));
        }

        private static bool EsAdjuntoValido(string fileName, string contentType)
        {
            if (string.IsNullOrWhiteSpace(fileName) && string.IsNullOrWhiteSpace(contentType))
                return false;

            var name = fileName?.ToLowerInvariant() ?? string.Empty;
            var type = contentType?.ToLowerInvariant() ?? string.Empty;

            var isPdf = type == "application/pdf" || name.EndsWith(".pdf");

            var isImage =
                type.StartsWith("image/") ||
                name.EndsWith(".png") ||
                name.EndsWith(".jpg") ||
                name.EndsWith(".jpeg");

            return isPdf || isImage;
        }



        public record CreateOrderRequest(
            string? Reference,
            string? Description,
            string? DocumentType,    // CI, RUC, PASAPORTE
            string? DocumentNumber,
            string? AddressLine,
            string? City,
            string? Province,
            string? PostalCode,
            string? Country,
            string? DoctorName
        );

        public record ReturnUrlRequest(string ReturnUrl);

    }
}
