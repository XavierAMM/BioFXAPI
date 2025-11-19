namespace BioFXAPI.Models
{
    public class OrderAttachment
    {
        public int Id { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        /// <summary>
        /// Clave del objeto en S3 (por ejemplo: "orders/{orderId}/attachments/{guid}.pdf").
        /// </summary>
        public string StorageKey { get; set; } = string.Empty;

        /// <summary>
        /// Tipo de adjunto. En tu caso típico: "FACTURA".
        /// </summary>
        public string Tipo { get; set; } = string.Empty;

        public bool Activo { get; set; }

        public DateTime CreadoEl { get; set; }

        public DateTime ActualizadoEl { get; set; }
    }
}
