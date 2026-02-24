using BioFXAPI.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestimoniosController : ControllerBase
    {
        private readonly string _connectionString;

        public TestimoniosController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        public async Task<IActionResult> GetTestimonios()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"SELECT * FROM Testimonios WHERE Activo = 1 ORDER BY CreadoEl DESC";
                var testimonios = await connection.QueryAsync<Testimonios>(query);

                return Ok(testimonios);
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTestimonio(int id)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = "SELECT * FROM Testimonios WHERE Id = @Id AND Activo = 1";
                var testimonio = await connection.QueryFirstOrDefaultAsync<Testimonios>(query, new { Id = id });

                if (testimonio == null)
                    return NotFound(new { message = "Testimonio no encontrado." });

                return Ok(testimonio);
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CrearTestimonio([FromBody] Testimonios testimonio)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"INSERT INTO Testimonios 
                            (Nombre, Testimonio, Imagen, Valoracion, Activo, CreadoEl, ActualizadoEl)
                            OUTPUT INSERTED.Id
                            VALUES (@Nombre, @Testimonio, @Imagen, @Valoracion, 1, GETUTCDATE(), GETUTCDATE())";

                var testimonioId = await connection.ExecuteScalarAsync<int>(query, testimonio);

                return Ok(new
                {
                    message = "Testimonio creado exitosamente.",
                    id = testimonioId
                });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [Authorize]
        [HttpPut("{id}")]
        public async Task<IActionResult> ActualizarTestimonio(int id, [FromBody] Testimonios testimonio)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verificar si el testimonio existe
                var checkQuery = "SELECT COUNT(*) FROM Testimonios WHERE Id = @Id AND Activo = 1";
                var count = await connection.ExecuteScalarAsync<int>(checkQuery, new { Id = id });

                if (count == 0)
                    return NotFound(new { message = "Testimonio no encontrado." });

                // Actualizar testimonio
                var updateQuery = @"UPDATE Testimonios SET 
                                Nombre = @Nombre, 
                                Testimonio = @TestimonioTexto, 
                                Imagen = @Imagen, 
                                Valoracion = @Valoracion,
                                ActualizadoEl = GETUTCDATE()
                                WHERE Id = @Id";

                testimonio.Id = id;
                await connection.ExecuteAsync(updateQuery, testimonio);

                return Ok(new { message = "Testimonio actualizado exitosamente." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> EliminarTestimonio(int id)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Soft delete del testimonio
                var query = @"UPDATE Testimonios SET Activo = 0, ActualizadoEl = GETUTCDATE()
                            WHERE Id = @Id";
                var affectedRows = await connection.ExecuteAsync(query, new { Id = id });

                if (affectedRows == 0)
                    return NotFound(new { message = "Testimonio no encontrado." });

                return Ok(new { message = "Testimonio eliminado exitosamente." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }
    }
}