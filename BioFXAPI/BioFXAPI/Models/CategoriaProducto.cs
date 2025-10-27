namespace BioFXAPI.Models
{
    public class CategoriaProducto
    {
        public int Id { get; set; }

        public int ProductoId { get; set; }
        public int CategoriaId { get; set; }

        public bool Activo { get; set; } = true;
        public DateTime CreadoEl { get; set; } = DateTime.UtcNow;
        public DateTime ActualizadoEl { get; set; } = DateTime.UtcNow;

        // Navegación
        public Producto Producto { get; set; }
        public Categoria Categoria { get; set; }
    }
}
