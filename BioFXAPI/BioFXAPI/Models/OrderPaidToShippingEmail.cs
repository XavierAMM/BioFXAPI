namespace BioFXAPI.Models
{
    public sealed class OrderPaidToShippingEmail
    {
        public string ToEmail { get; set; } = string.Empty;

        // Orden
        public string OrderReference { get; set; } = string.Empty;
        public int RequestId { get; set; }
        public string OrderStatus { get; set; } = string.Empty;
        public DateTime OrderCreatedAt { get; set; }

        // Montos
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = "USD";

        // Cliente
        public string CustomerFullName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string? CustomerPhone { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public string DocumentNumber { get; set; } = string.Empty;
        public string? DoctorName { get; set; }

        // Dirección
        public string AddressLine { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string? PostalCode { get; set; }

        // Pago
        public string PaymentStatus { get; set; } = string.Empty;
        public string? PaymentMethod { get; set; }
        public string? PaymentMethodName { get; set; }
        public string? IssuerName { get; set; }

        // Items
        public List<OrderPaidItemModel> Items { get; set; } = new();

        // Adjunto
        public byte[]? AttachmentBytes { get; set; }
        public string? AttachmentFileName { get; set; }
        public string? AttachmentContentType { get; set; }
    }

    public sealed class OrderPaidItemModel
    {
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }

}
