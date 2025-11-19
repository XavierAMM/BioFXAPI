namespace BioFXAPI.Models
{
    public class Order
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string OrderNumber { get; set; }
        public string Reference { get; set; }
        public string Description { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }

        // NUEVOS CAMPOS
        public int? OrderAttachmentId { get; set; }
        public string? DocumentType { get; set; }      // CI / RUC / PASAPORTE
        public string? DocumentNumber { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public string? DoctorName { get; set; }

        public DateTime CreadoEl { get; set; } = DateTime.UtcNow;
        public DateTime ActualizadoEl { get; set; } = DateTime.UtcNow;
    }
}
