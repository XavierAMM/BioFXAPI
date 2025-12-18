namespace BioFXAPI.Models
{
    public class Producto
    {
        public int Id { get; set; }
        public string Codigo { get; set; }
        public bool Disponible { get; set; } = true;
        public string Nombre { get; set; }
        public decimal Precio { get; set; }
        public string Imagen { get; set; }
        public string Logo { get; set; }
        public string Descripcion { get; set; }
        
        // Descripciones
        public string Desc_Principal { get; set; }
        public string Desc_Otros { get; set; }

        // Promociones
        public List<int> Promocionados { get; set; } = new List<int>();

        // Comerciales
        public int Descuento { get; set; } = 0;
        public string Disclaimer { get; set; }
        public string Contraindicaciones { get; set; }
        public decimal PrecioFinal { get; set; }

        // Stock
        public int Stock { get; set; } = 0;
        public int StockReservado { get; set; } = 0;
        
        // Auditoría
        public bool Activo { get; set; } = true;
        public DateTime CreadoEl { get; set; } = DateTime.UtcNow;
        public DateTime ActualizadoEl { get; set; } = DateTime.UtcNow;

        // Relaciones
        public ICollection<CategoriaProducto> Categorias { get; set; } = new List<CategoriaProducto>(); 
    }
    public class ProductoPromocionado
    {
        public int Id { get; set; }
        public int ProductoId { get; set; }
        public int PromocionadoId { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime CreadoEl { get; set; } = DateTime.UtcNow;
        public DateTime ActualizadoEl { get; set; } = DateTime.UtcNow;

        public Producto Producto { get; set; }
        public Producto ProductoPromocionadoInfo { get; set; }
    }

}
