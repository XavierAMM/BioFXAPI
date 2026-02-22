using System.Data;
using System.Data.SqlClient;
using System.IO;
using Dapper;
using BioFXAPI.Services;
using BioFXAPI.Models;
using System.Linq;


namespace BioFXAPI.Notifications
{
    public class OrderNotificationService
    {
        private readonly string _connectionString;
        private readonly string _shippingEmail;
        private readonly EmailService _emailService;
        private readonly IFileStorageService _fileStorage;
        private readonly ILogger<OrderNotificationService> _logger;

        public OrderNotificationService(IConfiguration configuration, EmailService emailService, IFileStorageService fileStorage, ILogger<OrderNotificationService> logger)
        {
            _emailService = emailService;
            _fileStorage = fileStorage;
            _logger = logger;

            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection no está configurado.");

            _shippingEmail = configuration["OrderNotifications:ShippingEmail"]
                ?? "envios@biofx.com.ec";                // ✅ fallback en constructor, no en el método

            if (configuration["OrderNotifications:ShippingEmail"] is null)
                _logger.LogWarning(
                    "OrderNotifications:ShippingEmail no configurado. Usando valor por defecto: {Email}",
                    _shippingEmail);
        }

        public async Task SendOrderPaidNotificationsAsync(int orderId, int requestId, CancellationToken ct = default)
        {
            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync(ct);

            _logger.LogInformation("OrderNotificationService: iniciando notificación de orden pagada. OrderId={OrderId}, RequestId={RequestId}", orderId, requestId);

            // 1) Cargar Order, Transaction, Usuario, Persona, OrderAttachment, Items
            const string sql = @"
        SELECT TOP 1
            o.Id              AS OrderId,
            o.OrderNumber,
            o.[Reference],
            o.[Status]        AS OrderStatus,
            o.TotalAmount,
            o.Currency,
            o.CreadoEl        AS OrderCreatedAt,
            o.AddressLine,
            o.City,
            o.Province,
            o.PostalCode,
            o.Country,
            o.DocumentType,
            o.DocumentNumber,
            o.DoctorName,
            o.OrderAttachmentId,

            u.Id              AS UserId,
            u.Email           AS UserEmail,

            p.Nombre          AS FirstName,
            p.Apellido        AS LastName,
            p.Telefono         AS PhoneNumber,

            t.Id              AS TransactionId,
            t.RequestId,
            t.InternalReference,
            t.[Status]        AS TxStatus,
            t.Reason          AS TxReason,
            t.[Message]       AS TxMessage,
            t.PaymentMethod,
            t.PaymentMethodName,
            t.IssuerName,

            oa.FileName       AS AttachmentFileName,
            oa.ContentType    AS AttachmentContentType,
            oa.FileSize       AS AttachmentFileSize,
            oa.StorageKey     AS AttachmentStorageKey,
            oa.Tipo           AS AttachmentTipo
        FROM [Order] o
        INNER JOIN [Usuario] u       ON u.Id = o.UserId
        LEFT JOIN  [Persona] p       ON p.UsuarioId = u.Id AND p.Activo = 1
        LEFT JOIN  [Transaction] t   ON t.OrderId = o.Id AND t.Activo = 1
        LEFT JOIN  [OrderAttachment] oa ON oa.Id = o.OrderAttachmentId AND oa.Activo = 1
        WHERE o.Id = @OrderId;

        SELECT
            oi.Quantity,
            oi.UnitPrice,
            oi.TotalPrice,
            pr.Nombre AS ProductName
        FROM [OrderItem] oi
        INNER JOIN [Producto] pr ON pr.Id = oi.ProductId
        WHERE oi.OrderId = @OrderId
        ORDER BY oi.Id;
        ";

            using var multi = await con.QueryMultipleAsync(sql, new { OrderId = orderId }, commandType: CommandType.Text);

            var header = await multi.ReadFirstOrDefaultAsync<OrderEmailHeader>();
            if (header == null)
            {
                _logger.LogWarning("OrderNotificationService: no se encontró la orden {OrderId}", orderId);
                return;
            }

            var items = (await multi.ReadAsync<OrderEmailItem>()).ToList();

            // 2) Obtener y preparar adjunto, si existe
            byte[]? attachmentBytes = null;
            string? attachmentFileName = null;
            string? attachmentContentType = null;

            if (!string.IsNullOrWhiteSpace(header.AttachmentStorageKey))
            {
                try
                {
                    _logger.LogInformation("OrderNotificationService: descargando adjunto S3 key={Key} para OrderId={OrderId}",
                        header.AttachmentStorageKey, orderId);

                    await using var stream = await _fileStorage.GetAsync(header.AttachmentStorageKey, ct);
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, ct);

                    attachmentBytes = ms.ToArray();
                    attachmentFileName = header.AttachmentFileName;
                    attachmentContentType = string.IsNullOrWhiteSpace(header.AttachmentContentType)
                        ? "application/octet-stream"
                        : header.AttachmentContentType;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "OrderNotificationService: error al obtener adjunto de S3 (key={Key}) para OrderId={OrderId}",
                        header.AttachmentStorageKey,
                        orderId);
                }
            }

            // 3) Email SOLO a envios@biofx.com.ec
            var shippingEmail = _shippingEmail;

            if (string.IsNullOrWhiteSpace(shippingEmail))
            {
                shippingEmail = "envios@biofx.com.ec";
                _logger.LogWarning("OrderNotifications:ShippingEmail no configurado. Usando valor por defecto: {Email}", shippingEmail);
            }

            try
            {

                _logger.LogInformation("OrderNotificationService: enviando correo de orden pagada a {Email} para OrderId={OrderId}, RequestId={RequestId}",
                    shippingEmail, orderId, requestId);

                var model = new OrderPaidToShippingEmail
                {
                    ToEmail = shippingEmail,

                    CustomerFullName = $"{header.FirstName} {header.LastName}".Trim(),
                    CustomerEmail = header.UserEmail!,
                    CustomerPhone = header.PhoneNumber,

                    OrderReference = header.Reference,
                    RequestId = header.RequestId,
                    OrderStatus = header.OrderStatus,
                    OrderCreatedAt = EnsureUtc(header.OrderCreatedAt),

                    TotalAmount = header.TotalAmount,
                    Currency = header.Currency,

                    AddressLine = header.AddressLine,
                    City = header.City,
                    Province = header.Province,
                    Country = header.Country,
                    PostalCode = header.PostalCode,

                    DocumentType = header.DocumentType,
                    DocumentNumber = header.DocumentNumber,
                    DoctorName = header.DoctorName,

                    PaymentStatus = header.TxStatus,
                    PaymentMethod = header.PaymentMethod,
                    PaymentMethodName = header.PaymentMethodName,
                    IssuerName = header.IssuerName,

                    Items = items.Select(i => new OrderPaidItemModel
                    {
                        ProductName = i.ProductName,
                        Quantity = i.Quantity,
                        UnitPrice = i.UnitPrice,
                        TotalPrice = i.TotalPrice
                    }).ToList(),

                    AttachmentBytes = attachmentBytes,
                    AttachmentFileName = attachmentFileName,
                    AttachmentContentType = attachmentContentType
                };

                await _emailService.SendOrderPaidToShippingAsync(model);


                _logger.LogInformation("OrderNotificationService: correo a envíos enviado correctamente para OrderId={OrderId}", orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "OrderNotificationService: error enviando correo a envíos para OrderId={OrderId}",
                    orderId);
                return; // No borrar adjunto si falló el correo
            }

            // 4) Si se envió el correo, borrar adjunto en S3 y limpiar BD
            if (header.OrderAttachmentId.HasValue &&
                !string.IsNullOrWhiteSpace(header.AttachmentStorageKey))
            {
                _logger.LogInformation("OrderNotificationService: limpiando adjunto de OrderId={OrderId}", orderId);

                try { await _fileStorage.DeleteAsync(header.AttachmentStorageKey, ct); }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "OrderNotificationService: error eliminando objeto S3 key={Key} para OrderId={OrderId}",
                        header.AttachmentStorageKey,
                        orderId);
                }

                using var tx = con.BeginTransaction();

                await con.ExecuteAsync(
                    "UPDATE [Order] SET OrderAttachmentId = NULL WHERE Id = @OrderId;",
                    new { OrderId = orderId }, tx);

                await con.ExecuteAsync(
                    "DELETE FROM [OrderAttachment] WHERE Id = @AttachmentId;",
                    new { AttachmentId = header.OrderAttachmentId.Value }, tx);

                tx.Commit();

                _logger.LogInformation("OrderNotificationService: adjunto eliminado y BD actualizada para OrderId={OrderId}", orderId);
            }
            else
            {
                _logger.LogInformation("OrderNotificationService: no había adjunto para limpiar en OrderId={OrderId}", orderId);
            }
        }



        // Clases internas para mapear el SQL

        private static DateTime EnsureUtc(DateTime dt)
        {
            return dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc) // si viene Unspecified pero sabes que es UTC desde BD
            };
        }

        private sealed class OrderEmailHeader
        {
            public int OrderId { get; set; }
            public string OrderNumber { get; set; } = string.Empty;
            public string Reference { get; set; } = string.Empty;
            public string OrderStatus { get; set; } = string.Empty;
            public decimal TotalAmount { get; set; }
            public string Currency { get; set; } = "USD";
            public DateTime OrderCreatedAt { get; set; }

            public string AddressLine { get; set; } = string.Empty;
            public string City { get; set; } = string.Empty;
            public string Province { get; set; } = string.Empty;
            public string? PostalCode { get; set; }
            public string Country { get; set; } = string.Empty;

            public string DocumentType { get; set; } = string.Empty;
            public string DocumentNumber { get; set; } = string.Empty;
            public string? DoctorName { get; set; }

            public int? OrderAttachmentId { get; set; }

            public int UserId { get; set; }
            public string? UserEmail { get; set; }

            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string? PhoneNumber { get; set; }

            public int? TransactionId { get; set; }
            public int RequestId { get; set; }
            public int? InternalReference { get; set; }
            public string TxStatus { get; set; } = string.Empty;
            public string? TxReason { get; set; }
            public string? TxMessage { get; set; }
            public string? PaymentMethod { get; set; }
            public string? PaymentMethodName { get; set; }
            public string? IssuerName { get; set; }

            public string? AttachmentFileName { get; set; }
            public string? AttachmentContentType { get; set; }
            public long? AttachmentFileSize { get; set; }
            public string? AttachmentStorageKey { get; set; }
            public string? AttachmentTipo { get; set; }
        }

        public sealed class OrderEmailItem
        {
            public int Quantity { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal TotalPrice { get; set; }
            public string ProductName { get; set; } = string.Empty;
        }
    }
}
