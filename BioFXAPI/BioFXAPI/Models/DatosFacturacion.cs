namespace BioFXAPI.Models
{
    public class DatosFacturacion
    {
        public int Id { get; set; }
        public int UsuarioId { get; set; }
        public string Nombre_Razon_Social { get; set; }
        public string RUC_Cedula { get; set; }
        public string Direccion { get; set; }
        public string Telefono { get; set; }
        public string Email { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime CreadoEl { get; set; } = DateTime.UtcNow;
        public DateTime ActualizadoEl { get; set; } = DateTime.UtcNow;
    }
}