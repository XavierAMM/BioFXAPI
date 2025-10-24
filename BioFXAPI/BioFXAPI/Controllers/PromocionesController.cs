using BioFXAPI.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PromocionesController : ControllerBase
    {
        private readonly string _connectionString;

        public PromocionesController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        public IActionResult GetPromociones()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var query = @"
                SELECT 
                    p.id as Id,
                    p.titulo as Titulo,
                    p.descripcion as Descripcion,
                    p.botonTexto as BotonTexto,
                    p.botonUrl as BotonUrl,
                    p.imagen as Imagen,
                    at.descripcion as TextoAlineacion,
                    ai.descripcion as ImagenAlineacion,
                    tf.descripcion as Fondo,
                    p.background as Background,
                    ap.descripcion as TextoPosicion,
                    p.colorTexto as ColorTexto,
                    p.activa as Activa,
                    p.fechaInicio as FechaInicio,
                    p.fechaFin as FechaFin,
                    p.orden as Orden,
                    p.creadoEl as CreadoEl,
                    p.actualizadoEl as ActualizadoEl
                FROM Promocion p
                INNER JOIN Alineacion at ON p.textoAlineacionId = at.id
                INNER JOIN Alineacion ai ON p.imagenAlineacionId = ai.id
                INNER JOIN Fondo tf ON p.fondoId = tf.id
                LEFT JOIN Alineacion ap ON p.textoPosicionId = ap.id
                WHERE p.activa = 1
                ORDER BY p.orden, p.creadoEl DESC";

                var promociones = connection.Query<Promocion>(query);
                return Ok(promociones);
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }
    }
}