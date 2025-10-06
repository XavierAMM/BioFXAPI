namespace BioFXAPI.Models
{
    public class CartItem
    {
        public int Id { get; set; }
        public int CartId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public DateTime AgregadoEl { get; set; } = DateTime.UtcNow;
    }
}
