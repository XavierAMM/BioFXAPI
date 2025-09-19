namespace BioFXAPI.Models
{
    public class Persona
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Apellido { get; set; }
        public string Telefono { get; set; }
        public int UsuarioId { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime CreadoEl { get; set; } = DateTime.UtcNow;
        public DateTime ActualizadoEl { get; set; } = DateTime.UtcNow;
    }
}