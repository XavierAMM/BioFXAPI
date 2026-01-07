namespace BioFXAPI.Models
{
    public class Transaction
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int RequestId { get; set; }
        public int? InternalReference { get; set; }
        public string ProcessUrl { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
        public string Message { get; set; }
        public string PaymentMethod { get; set; }
        public string PaymentMethodName { get; set; }
        public string IssuerName { get; set; }
        public bool Refunded { get; set; }
        public decimal? RefundedAmount { get; set; }
        public bool Activo { get; set; } = true;
        public string Authorization { get; set; }

        public DateTime CreadoEl { get; set; } = DateTime.UtcNow;
        public DateTime ActualizadoEl { get; set; } = DateTime.UtcNow;
    }
}
