using BioFXAPI.Controllers;

namespace BioFXAPI.Models
{
    public class OrderInfo
    {
        public Order Order { get; set; }
        public User User { get; set; }
        public List<OrderItem> Items { get; set; }
        public Transaction Transaction { get; set; }
        public OrderAttachment? Attachment { get; set; }
        public byte[]? AttachmentBytes { get; set; } 
    }

}
