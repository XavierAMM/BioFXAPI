using BioFXAPI.Notifications;
using BioFXAPI.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
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
        private readonly ILogger<OrdersController> _logger;
        private readonly IFileStorageService _fileStorage;
        private readonly OrderNotificationService _orderNotificationService;
        private readonly PlacetoPayRefreshService _refresh;
        private readonly OrderCancellationService _cancelSvc;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _placetoPayBaseUrl;

        public OrdersController(IConfiguration cfg, ILogger<OrdersController> logger,
            IFileStorageService fileStorage, OrderNotificationService orderNotificationService,
            PlacetoPayRefreshService refresh, OrderCancellationService cancelSvc,
            IHttpClientFactory httpClientFactory) 
        {
            _cfg = cfg;
            _cs = cfg.GetConnectionString("DefaultConnection");
            _placetoPayBaseUrl = cfg["PlacetoPay:BaseUrl"]
                ?? throw new InvalidOperationException("PlacetoPay:BaseUrl no configurado.");
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _fileStorage = fileStorage;
            _orderNotificationService = orderNotificationService;
            _refresh = refresh;
            _cancelSvc = cancelSvc;
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

                    var items = await con.QueryAsync<(int ProductId, int Quantity, decimal UnitPrice, decimal DiscountPRC)>(
                        @"SELECT ci.ProductId,
                         ci.Quantity,
                         CAST(p.Precio AS decimal(18,2)) AS UnitPrice,
                         CAST(p.Descuento AS decimal(18,2)) AS DiscountPRC
                         FROM CartItem ci
                         INNER JOIN Producto p ON p.Id=ci.ProductId
                         WHERE ci.CartId=@Cart AND ci.Activo=1",
                        new { Cart = cartId.Value }, tx);

                    if (!items.Any())
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new { message = "Carrito sin items." });
                    }

                    var subtotalBase = Math.Round(items.Sum(i => i.Quantity * i.UnitPrice),2);
                    var descuentoProductos = Math.Round(items.Sum(i => (i.UnitPrice * (i.DiscountPRC / 100m)) * i.Quantity),2);
                    var subtotalNeto = Math.Round(Math.Max(0m, subtotalBase - descuentoProductos),2);
                    var costoEnvio = subtotalNeto >= 50 ? 0m : 3.50m; // CAMBIAR COSTO DE ENVÍO AQUÍ
                    costoEnvio = Math.Min(costoEnvio, subtotalNeto);
                    var descuentoRecetaUSD = req.tieneReceta? Math.Round(subtotalNeto * 0.02m, 2): 0m;                                         
                    var descuentoTotalUSD = Math.Round(descuentoProductos + descuentoRecetaUSD,2);
                    var totalFinal = Math.Round(Math.Max(0m, subtotalNeto - descuentoRecetaUSD + costoEnvio),2);
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
                                    Subtotal,
                                    CostoEnvio,
                                    DescuentoUSD,
                                    tieneReceta,
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
                                    @Subtotal,
                                    @CostoEnvio,
                                    @DescuentoUSD,
                                    @TieneReceta,
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
                                    Subtotal = subtotalNeto,
                                    CostoEnvio = costoEnvio,
                                    DescuentoUSD = descuentoTotalUSD,
                                    TieneReceta = req.tieneReceta ? 1 : 0,
                                    Total = totalFinal,
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
                        var unitNet = Math.Round(it.UnitPrice * (1m - (it.DiscountPRC / 100m)),2);
                        var lineTotal = Math.Round(unitNet * it.Quantity, 2);

                        await con.ExecuteAsync(
                            @"INSERT INTO OrderItem(OrderId, ProductId, Quantity, UnitPrice, TotalPrice, CreadoEl, Activo)
                            VALUES(@Oid, @Pid, @Qty, @Price, @Sub, GETUTCDATE(), 1)",
                            new
                            {
                                Oid = orderId,
                                Pid = it.ProductId,
                                Qty = it.Quantity,
                                Price = unitNet,
                                Sub = lineTotal
                            },
                            tx
                        );

                        var rows = await con.ExecuteAsync(@"
                            UPDATE Producto
                            SET StockReservado = COALESCE(StockReservado,0) + @Qty,
                                Disponible = CASE 
                                    WHEN (COALESCE(Stock,0) - (COALESCE(StockReservado,0) + @Qty)) > 0 THEN 1 
                                    ELSE 0 
                                END,
                                ActualizadoEl = GETUTCDATE()
                            WHERE Id = @Pid
                                AND (COALESCE(Stock,0) - COALESCE(StockReservado,0)) >= @Qty;",
                            new { Pid = it.ProductId, Qty = it.Quantity }, tx);

                        if (rows <= 0)
                        {
                            await tx.RollbackAsync();
                            return BadRequest(new { message = "Stock insuficiente para uno o más productos." });
                        }
                    }

                    await con.ExecuteAsync(
                        "UPDATE CartItem SET Activo=0 WHERE CartId=@Cart AND Activo=1",
                        new { Cart = cartId.Value }, tx);

                    await tx.CommitAsync();

                    _logger.LogInformation("Orden creada correctamente. OrderId={OrderId}, UserId={UserId}, Total={Total}",
                     orderId, userId, totalFinal);

                    return Ok(new
                    {
                        id = orderId,
                        orderId = orderId,
                        OrderNumber = orderNumber,
                        Reference = reference,
                        Total = totalFinal,
                        Currency = "USD"
                    });
                }
                catch (SqlException ex)
                {
                    try { await tx.RollbackAsync(); } catch { }
                    return StatusCode(500, new { message = "Error de base de datos." });
                }
                catch (Exception ex)
                {
                    try { await tx.RollbackAsync(); } catch { }
                    return StatusCode(500, new { message = "Error interno al crear la orden." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado en CreateFromCart.");
                return StatusCode(500, new
                {
                    message = "Error interno al procesar la orden. Por favor intente más tarde."
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

            // Solo aceptamos PDF o imágenes — validar magic bytes para no confiar solo en extensión/Content-Type
            byte[] headerBytes = new byte[4];
            await using (var headerStream = file.OpenReadStream())
                _ = await headerStream.ReadAsync(headerBytes, 0, 4);

            if (!EsAdjuntoValido(file.FileName, file.ContentType, headerBytes))
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

            // Derivar extensión de los magic bytes ya validados, no del nombre provisto por el cliente
            var ext = headerBytes[0] switch
            {
                0x25 => ".pdf",  // %PDF
                0x89 => ".png",  // PNG
                0xFF => ".jpg",  // JPEG
                _    => ".bin"
            };

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
                return StatusCode(500, new
                {
                    message = "Error al guardar la factura de la orden."
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

            var order = await con.QueryFirstOrDefaultAsync<(
                decimal TotalAmount,
                string Currency,
                string Reference,
                string Description,
                string DocumentType,
                string DocumentNumber
            )>(
                @"SELECT TotalAmount, Currency, Reference, Description, DocumentType, DocumentNumber
          FROM [Order]
          WHERE Id=@Id AND Activo=1",
                new { Id = orderId }
            );

            if (order == default) return NotFound(new { message = "Orden no encontrada." });

            var status = await con.ExecuteScalarAsync<string>(
                "SELECT Status FROM [Order] WHERE Id=@Id AND Activo=1", new { Id = orderId });

            if (string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "La orden ya está pagada." });

            // ===== NUEVO: revisar última transacción "abierta" =====
            var lastTx = await con.QueryFirstOrDefaultAsync<(int RequestId, string ProcessUrl, string Status)>(
                @"SELECT TOP 1 RequestId, ProcessUrl, Status
          FROM [Transaction]
          WHERE OrderId=@Id AND Activo=1
          ORDER BY Id DESC",
                new { Id = orderId });

            bool sameHost = false;
            if (lastTx != default && !string.IsNullOrWhiteSpace(lastTx.ProcessUrl))
            {
                var pu = new Uri(lastTx.ProcessUrl);
                var bu = new Uri(_cfg["PlacetoPay:BaseUrl"]);
                sameHost = string.Equals(pu.Host, bu.Host, StringComparison.OrdinalIgnoreCase);
            }

            // Si está en validación, NO crear una sesión nueva (bloquear reintento)
            if (lastTx != default &&
                sameHost &&
                string.Equals(lastTx.Status, "PENDING_VALIDATION", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(409, new
                {
                    message = "Existe un pago en validación. Intenta refrescar el estado antes de crear otra sesión.",
                    status = lastTx.Status,
                    requestId = lastTx.RequestId
                });
            }

            // Si está pendiente por abandono, reusar processUrl
            if (lastTx != default &&
                sameHost &&
                string.Equals(lastTx.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new { requestId = lastTx.RequestId, processUrl = lastTx.ProcessUrl, reused = true });
            }

            // ===== resto del método igual (crear sesión nueva) =====

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

            var buyer = await con.QueryFirstOrDefaultAsync<(string Nombre, string Apellido, string Email, string? Telefono)>(
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

            if (buyer == default)
                return BadRequest(new { message = "No se encontró información de Persona/Usuario para el comprador." });

            string? mobile = null;
            if (!string.IsNullOrWhiteSpace(buyer.Telefono))
            {
                var digits = new string(buyer.Telefono.Where(char.IsDigit).ToArray());
                mobile = string.IsNullOrWhiteSpace(digits) ? null : digits;
            }

            var docType = (order.DocumentType ?? "").Trim();
            var docNumber = (order.DocumentNumber ?? "").Trim();

            var expiration = DateTime.UtcNow
                .AddMinutes(int.Parse(_cfg["PlacetoPay:TimeoutMinutes"]!))
                .ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);

            var notificationUrl = _cfg["PlacetoPay:NotificationUrl"];

            var body = new
            {
                locale = "es_EC",
                buyer = new
                {
                    name = buyer.Nombre,
                    surname = buyer.Apellido,
                    email = buyer.Email,
                    document = docNumber,
                    documentType = docType,
                    mobile = mobile
                },
                payment = new
                {
                    reference = order.Reference,
                    description = order.Description,
                    amount = new
                    {
                        currency = (order.Currency ?? "USD").Trim().ToUpperInvariant(),
                        total = order.TotalAmount
                    }
                },
                expiration = expiration,
                ipAddress = ip,
                returnUrl = req.ReturnUrl,
                userAgent = Request.Headers["User-Agent"].ToString(),
                paymentMethod = (string?)null,
                notificationUrl = notificationUrl,
                auth = new
                {
                    login = _cfg["PlacetoPay:Login"],
                    tranKey,
                    nonce,
                    seed
                }
            };

            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            var http = _httpClientFactory.CreateClient();
            http.BaseAddress = new Uri(_placetoPayBaseUrl);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var resp = await http.PostAsync("api/session", new StringContent(json, Encoding.UTF8, "application/json"));
            var payload = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, payload);

            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("requestId", out var ridEl) || !ridEl.TryGetInt32(out var requestId))
                return StatusCode(502, new { message = "Respuesta sin requestId.", raw = payload });

            var processUrl = doc.RootElement.GetProperty("processUrl").GetString() ?? "";

            await con.ExecuteAsync(
                @"INSERT INTO [Transaction](OrderId, RequestId, InternalReference, ProcessUrl, Status, Reason, Message, PaymentMethod, PaymentMethodName, IssuerName, Refunded, RefundedAmount, CreadoEl, ActualizadoEl, Activo)
          VALUES(@OrderId, @RequestId, NULL, @ProcessUrl, 'PENDING', NULL, NULL, NULL, NULL, NULL, 0, NULL, GETUTCDATE(), GETUTCDATE(), 1)",
                new { OrderId = orderId, RequestId = requestId, ProcessUrl = processUrl });

            return Ok(new { requestId, processUrl, reused = false });
        }


        [HttpGet("{orderId:int}/status")]
        public async Task<IActionResult> RefreshStatus(int orderId, CancellationToken ct)
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync(ct);

            var (ok, userId, err) = await TryResolveUserIdAsync(con, null);
            if (!ok) return err!;

            var isMine = await con.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM [Order] WHERE Id=@Id AND UserId=@Uid",
                new { Id = orderId, Uid = userId });
            if (isMine == 0) return Forbid();

            var requestId = await con.ExecuteScalarAsync<int?>(
                @"SELECT TOP 1 RequestId
                  FROM [Transaction]
                  WHERE OrderId=@Id
                  ORDER BY Id DESC", new { Id = orderId });

            if (!requestId.HasValue || requestId.Value <= 0)
                return BadRequest(new { message = "Orden sin transacción." });

            var (error2, result) = await _refresh.RefreshByRequestIdAsync(
                requestId: requestId.Value,
                validateOwner: false,
                callerUser: null,
                ct: ct);

            if (error2 != null) return error2;

            var orderInfo = await con.QueryFirstOrDefaultAsync<(string Reference, decimal TotalAmount, string Currency)>(
                @"SELECT Reference, TotalAmount, Currency
                  FROM [Order]
                  WHERE Id = @Id AND Activo = 1;",
                new { Id = orderId });

            if (orderInfo == default)
                return NotFound(new { message = "Orden no encontrada." });

            return Ok(new
            {
                orderId = result!.OrderId,
                status = result.Status,
                reference = orderInfo.Reference,
                total = orderInfo.TotalAmount,
                currency = orderInfo.Currency
            });

        }

        [Authorize]
        [HttpPost("{orderId:int}/cancel")]
        public async Task<IActionResult> CancelPendingSession(int orderId, CancellationToken ct)
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync(ct);

            var (ok, userId, err) = await TryResolveUserIdAsync(con, null);
            if (!ok) return err!;

            var isMine = await con.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM [Order] WHERE Id=@Id AND UserId=@Uid AND Activo=1",
                new { Id = orderId, Uid = userId });
            if (isMine == 0) return Forbid();

            var (error, status) = await _cancelSvc.CancelOrderAsync(orderId, tryCancelGateway: true, ct);
            if (error != null) return error;

            return Ok(new { orderId, status });
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

            // Última transacción activa
            var tx = await con.QueryFirstOrDefaultAsync<(int TxId, int RequestId, string ProcessUrl)>(@"
        SELECT TOP 1 Id AS TxId, RequestId, ProcessUrl
        FROM [Transaction]
        WHERE OrderId=@Id AND Activo=1
        ORDER BY Id DESC", new { Id = orderId });

            if (tx.Equals(default) || tx.RequestId <= 0 || string.IsNullOrWhiteSpace(tx.ProcessUrl))
                return BadRequest(new { message = "No existe una sesión previa válida para esta orden." });

            // Si tu verdad de pagado es Order.Status, valida primero (rápido)
            var orderStatus = await con.ExecuteScalarAsync<string>(
                "SELECT Status FROM [Order] WHERE Id=@Id AND Activo=1", new { Id = orderId });

            if (string.Equals(orderStatus, "PAID", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "La orden ya está pagada." });

            // 1) Consultar estado real en PlacetoPay (GetRequestInformation)
            var info = await GetRequestInformationAsync(tx.RequestId);
            if (!info.ok)
                return StatusCode(info.statusCode, new { message = "No se pudo consultar la sesión en PlacetoPay.", raw = info.raw });

            using var doc = info.doc!;
            var root = doc.RootElement;

            // 2) Leer status remoto
            var statusEl = root.GetProperty("status");
            TryReadString(statusEl, "status", out var remoteStatus);
            TryReadString(statusEl, "reason", out var remoteReason);
            TryReadString(statusEl, "message", out var remoteMessage);

            // 3) Leer expiration remoto (si viene)
            DateTimeOffset? remoteExpiration = null;
            if (root.TryGetProperty("request", out var requestEl) &&
                requestEl.TryGetProperty("expiration", out var expEl) &&
                expEl.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(expEl.GetString(), out var expDto))
            {
                remoteExpiration = expDto;
            }

            var now = DateTimeOffset.UtcNow;

            var isExpired =
                string.Equals(remoteStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(remoteReason, "EX", StringComparison.OrdinalIgnoreCase) ||
                (remoteExpiration.HasValue && now >= remoteExpiration.Value.ToUniversalTime());

            // 4) Persistir status remoto en tu Transaction (recomendado)
            await con.ExecuteAsync(@"
        UPDATE [Transaction]
        SET Status=@Status,
            Reason=@Reason,
            Message=@Message,
            ActualizadoEl=GETUTCDATE()
        WHERE Id=@TxId", new
            {
                Status = remoteStatus,
                Reason = remoteReason,
                Message = remoteMessage,
                TxId = tx.TxId
            });

            if (isExpired)
            {
                return StatusCode(StatusCodes.Status410Gone, new
                {
                    message = "La sesión de pago ya expiró. Crea una nueva sesión para continuar.",
                    requestId = tx.RequestId,
                    expired = true,
                    status = remoteStatus,
                    reason = remoteReason
                });
            }

            // Opcional: si ya finalizó, no “reanudes”
            if (string.Equals(remoteStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                // aquí podrías llamar tu RefreshStatus/RefreshService si quieres sincronizar y marcar PAID
                return Conflict(new
                {
                    message = "La sesión ya finalizó como APROBADA. Refresca el estado de la orden.",
                    requestId = tx.RequestId,
                    status = remoteStatus
                });
            }

            if (string.Equals(remoteStatus, "REJECTED", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new
                {
                    message = "La sesión ya finalizó como RECHAZADA. Crea una nueva sesión si deseas reintentar.",
                    requestId = tx.RequestId,
                    status = remoteStatus,
                    reason = remoteReason
                });
            }

            // 5) Sigue abierta: reanudar en la misma sesión
            return Ok(new
            {
                requestId = tx.RequestId,
                processUrl = tx.ProcessUrl,
                reused = true,
                expired = false,
                status = remoteStatus
            });
        }

        [HttpGet("mine/history")]
        public async Task<IActionResult> GetMyPaidOrdersHistory()
        {
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            // 1) Resolver usuario autenticado (reutilizamos tu helper)
            var (ok, userId, err) = await TryResolveUserIdAsync(con, null);
            if (!ok) return err!;

            // 2) Traer órdenes del usuario (compactas) excepto Activo = 0 (Transaction.Status = 'CANCELLED') o EXPIRED
            var orders = (await con.QueryAsync<OrderHistoryRow>(@"
        SELECT 
            o.Id             AS OrderId,
            o.OrderNumber    AS OrderNumber,
            o.Reference      AS Reference,
            tx.InternalReference  AS InternalReference,
            o.TotalAmount    AS TotalAmount,
            o.Currency       AS Currency,
            o.Status         AS Status,
            o.CreadoEl       AS CreatedAt,
            o.DocumentType   AS DocumentType,
            o.DocumentNumber AS DocumentNumber,
            o.AddressLine    AS AddressLine,
            o.City           AS City,
            o.Province       AS Province,
            o.PostalCode     AS PostalCode,
            o.Country        AS Country,
            o.DoctorName     AS DoctorName,            
            CASE WHEN o.OrderAttachmentId IS NULL THEN 0 ELSE 1 END AS HasAttachment,
            tx.PaymentMethod      AS PaymentMethod,            
            tx.PaymentMethodName  AS PaymentMethodName,
            tx.IssuerName         AS IssuerName,
            tx.[Authorization]      AS [Authorization]
        FROM [Order] o
        OUTER APPLY (
            SELECT TOP 1 
                t.InternalReference,
                t.PaymentMethod,
                t.PaymentMethodName,
                t.IssuerName,
                t.[Authorization]
            FROM [Transaction] t
            WHERE t.OrderId = o.Id AND t.Activo = 1
            ORDER BY t.Id DESC
        ) tx
        WHERE 
            o.UserId = @Uid
            AND o.Activo = 1            
        ORDER BY o.CreadoEl DESC;", new { Uid = userId }))
                .ToList();

            if (orders.Count == 0)
                return Ok(Array.Empty<OrderHistoryDto>());

            // 3) Traer items de todas esas órdenes en una sola consulta
            var orderIds = orders.Select(o => o.OrderId).Distinct().ToArray();

            var items = (await con.QueryAsync<OrderItemHistoryRow>(@"
        SELECT 
            oi.OrderId      AS OrderId,
            oi.ProductId    AS ProductId,
            p.Nombre        AS ProductName,
            p.Imagen        AS ProductImage,
            oi.Quantity     AS Quantity,
            oi.UnitPrice    AS UnitPrice,
            oi.TotalPrice   AS TotalPrice
        FROM OrderItem oi
        INNER JOIN Producto p ON p.Id = oi.ProductId
        WHERE 
            oi.OrderId IN @Ids
            AND oi.Activo = 1
        ORDER BY oi.OrderId, oi.Id;", new { Ids = orderIds }))
                .ToList();

            // 4) Agrupar items por orden y proyectar al DTO final
            var itemsByOrder = items
                .GroupBy(i => i.OrderId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = orders.Select(o =>
            {
                itemsByOrder.TryGetValue(o.OrderId, out var oItems);
                oItems ??= new List<OrderItemHistoryRow>();

                return new OrderHistoryDto
                (
                    OrderId: o.OrderId,
                    OrderNumber: o.OrderNumber,
                    Reference: o.Reference,
                    InternalReference: o.InternalReference,
                    CreatedAt: o.CreatedAt,
                    TotalAmount: o.TotalAmount,
                    Currency: o.Currency,
                    Status: o.Status,
                    HasAttachment: o.HasAttachment != 0,
                    AddressLine: o.AddressLine,
                    City: o.City,
                    Province: o.Province,
                    PostalCode: o.PostalCode,
                    Country: o.Country,
                    DoctorName: o.DoctorName,                    
                    PaymentMethod: o.PaymentMethod,
                    PaymentMethodName: o.PaymentMethodName,
                    IssuerName: o.IssuerName,
                    Authorization: o.Authorization,                    
                    Items: oItems.Select(i => new OrderItemHistoryDto
                    (
                        ProductId: i.ProductId,
                        ProductName: i.ProductName,
                        ProductImage: i.ProductImage,
                        Quantity: i.Quantity,
                        UnitPrice: i.UnitPrice,
                        TotalPrice: i.TotalPrice
                    )).ToList()
                );
            }).ToList();


            return Ok(result);
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

        private static bool EsAdjuntoValido(string fileName, string contentType, ReadOnlySpan<byte> magicBytes)
        {
            if (string.IsNullOrWhiteSpace(fileName) && string.IsNullOrWhiteSpace(contentType))
                return false;

            if (magicBytes.Length < 4)
                return false;

            var name = fileName?.ToLowerInvariant() ?? string.Empty;
            var type = contentType?.ToLowerInvariant() ?? string.Empty;

            bool isPdf = type == "application/pdf" || name.EndsWith(".pdf");
            bool isImage = type.StartsWith("image/") || name.EndsWith(".png") || name.EndsWith(".jpg") || name.EndsWith(".jpeg");

            if (!isPdf && !isImage)
                return false;

            // PDF: %PDF (25 50 44 46)
            if (isPdf && magicBytes[0] == 0x25 && magicBytes[1] == 0x50 && magicBytes[2] == 0x44 && magicBytes[3] == 0x46)
                return true;

            // PNG: 89 50 4E 47
            if (isImage && magicBytes[0] == 0x89 && magicBytes[1] == 0x50 && magicBytes[2] == 0x4E && magicBytes[3] == 0x47)
                return true;

            // JPEG: FF D8 FF
            if (isImage && magicBytes[0] == 0xFF && magicBytes[1] == 0xD8 && magicBytes[2] == 0xFF)
                return true;

            return false;
        }

        private object BuildPlacetoPayAuth()
        {
            var seed = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);

            var nonceBytes = RandomNumberGenerator.GetBytes(16);
            var nonce = Convert.ToBase64String(nonceBytes);

            var secret = _cfg["PlacetoPay:SecretKey"]!.Trim();

            // tranKey = base64( SHA256( nonce + seed + secretKey ) )
            var seedPlusSecret = seed + secret;
            var seedSecretBytes = Encoding.UTF8.GetBytes(seedPlusSecret);

            var input = new byte[nonceBytes.Length + seedSecretBytes.Length];
            Buffer.BlockCopy(nonceBytes, 0, input, 0, nonceBytes.Length);
            Buffer.BlockCopy(seedSecretBytes, 0, input, nonceBytes.Length, seedSecretBytes.Length);

            var tranKey = Convert.ToBase64String(SHA256.HashData(input));

            return new
            {
                login = _cfg["PlacetoPay:Login"],
                tranKey,
                nonce,
                seed
            };
        }

        private async Task<(bool ok, JsonDocument? doc, int statusCode, string? raw)> GetRequestInformationAsync(int requestId)
        {
            var body = new
            {
                auth = BuildPlacetoPayAuth()
            };

            var json = JsonSerializer.Serialize(body);
            var http = _httpClientFactory.CreateClient();           
            http.BaseAddress = new Uri(_placetoPayBaseUrl);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            var resp = await http.PostAsync(
                $"api/session/{requestId}",
                new StringContent(json, Encoding.UTF8, "application/json"));
            var payload = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return (false, null, (int)resp.StatusCode, payload);

            return (true, JsonDocument.Parse(payload), 200, null);
        }

        private static bool TryReadString(JsonElement el, string prop, out string? value)
        {
            value = null;
            if (!el.TryGetProperty(prop, out var p)) return false;
            value = p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
            return true;
        }

        public record OrderHistoryDto(
            int OrderId,
            string OrderNumber,
            string Reference,
            int InternalReference,
            DateTime CreatedAt,
            decimal TotalAmount,
            string Currency,
            bool HasAttachment,
            string? AddressLine,
            string? City,
            string? Province,
            string? PostalCode,
            string? Country,
            string? DoctorName,
            string? PaymentMethod,
            string? PaymentMethodName,
            string? IssuerName,
            string? Authorization,
            string? Status,
            List<OrderItemHistoryDto> Items
);

        public record OrderItemHistoryDto(
            int ProductId,
            string ProductName,
            string ProductImage,
            int Quantity,
            decimal UnitPrice,
            decimal TotalPrice
        );

        public record OrderHistoryRow(
            int OrderId,
            string OrderNumber,
            string Reference,
            int InternalReference,
            decimal TotalAmount,
            string Currency,
            string Status,
            DateTime CreatedAt,
            string? DocumentType,
            string? DocumentNumber,
            string? AddressLine,
            string? City,
            string? Province,
            string? PostalCode,
            string? Country,
            string? DoctorName,
            int HasAttachment,
            string? PaymentMethod,
            string? PaymentMethodName,
            string? IssuerName,
            string? Authorization
        );

        public record OrderItemHistoryRow(
            int OrderId,
            int ProductId,
            string ProductName,
            string ProductImage,
            int Quantity,
            decimal UnitPrice,
            decimal TotalPrice
        );

        public record CreateOrderRequest(
            string? Reference,
            string? Description,
            string? DocumentType,
            string? DocumentNumber,
            string? AddressLine,
            string? City,
            string? Province,
            string? PostalCode,
            string? Country,
            string? DoctorName,
            bool tieneReceta
        );

        public record ReturnUrlRequest(string ReturnUrl);



    }
}
