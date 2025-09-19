using System;

namespace BioFXAPI.Models
{
    public class Promocion
    {
        public int Id { get; set; }
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        public string BotonTexto { get; set; }
        public string BotonUrl { get; set; }
        public string Imagen { get; set; }

        public string TextoAlineacion { get; set; }
        public string ImagenAlineacion { get; set; }
        public string Fondo { get; set; }

        public string ColorTexto { get; set; }
        public bool Activa { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public int Orden { get; set; }
        public DateTime CreadoEl { get; set; }
        public DateTime ActualizadoEl { get; set; }
    }
}