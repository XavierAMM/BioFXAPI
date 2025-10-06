namespace BioFXAPI.Models
{
    public class WebhookLog
    {
        public int Id { get; set; }
        public int RequestId { get; set; }
        public string Payload { get; set; }
        public string Signature { get; set; }
        public string Status { get; set; }
        public bool Processed { get; set; }
        public DateTime CreadoEl { get; set; } = DateTime.UtcNow;
    }
}
