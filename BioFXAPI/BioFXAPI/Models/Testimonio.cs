namespace BioFXAPI.Models
{
    public class Testimonio
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Texto { get; set; }
        public string Imagen { get; set; }
        public int Valoracion { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime CreadoEl { get; set; } = DateTime.UtcNow;
        public DateTime ActualizadoEl { get; set; } = DateTime.UtcNow;
    }
}