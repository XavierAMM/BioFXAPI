namespace BioFXAPI.Models
{
    public class Categoria
    {
        public int Id { get; set; }
        public string Descripcion { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime CreadoEl { get; set; } = DateTime.UtcNow;
        public DateTime ActualizadoEl { get; set; } = DateTime.UtcNow;
    }
}