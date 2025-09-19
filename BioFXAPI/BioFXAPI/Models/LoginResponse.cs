namespace BioFXAPI.Models
{
    public class LoginResponse
    {
        public string Message { get; set; }
        public int UserId { get; set; }
        public string Email { get; set; }
        public string Token { get; set; }
        public PersonaInfo Persona { get; set; }
        public DateTime FechaCreacion { get; set; }
        public bool EsAdministrador { get; set; }
    }

    public class PersonaInfo
    {
        public string Nombre { get; set; }
        public string Apellido { get; set; }
        public string Telefono { get; set; }
    }
}