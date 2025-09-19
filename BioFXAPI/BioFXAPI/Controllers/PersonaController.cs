using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Security.Claims;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PersonaController : ControllerBase
    {
        private readonly string _connectionString;

        public PersonaController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet("mi-perfil")]
        public async Task<IActionResult> GetMiPerfil()
        {
            // Obtener userId del token JWT
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"SELECT Id, Nombre, Apellido, Telefono, UsuarioId, Activo, CreadoEl, ActualizadoEl
                              FROM Persona WHERE UsuarioId = @UsuarioId AND Activo = 1";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UsuarioId", userId);

                using var reader = await command.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return NotFound(new { message = "Datos personales no encontrados." });

                var persona = new
                {
                    Id = reader.GetInt32(0),
                    Nombre = reader.GetString(1),
                    Apellido = reader.GetString(2),
                    Telefono = reader.IsDBNull(3) ? null : reader.GetString(3),
                    UsuarioId = reader.GetInt32(4),
                    Activo = reader.GetBoolean(5),
                    CreadoEl = reader.GetDateTime(6),
                    ActualizadoEl = reader.GetDateTime(7)
                };

                return Ok(persona);
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [HttpPut("actualizar")]
        public async Task<IActionResult> Actualizar([FromBody] PersonaUpdateRequest request)
        {
            // Obtener userId del token JWT
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"UPDATE Persona 
                              SET Nombre = @Nombre, Apellido = @Apellido, 
                                  Telefono = @Telefono, ActualizadoEl = GETUTCDATE()
                              WHERE UsuarioId = @UsuarioId AND Activo = 1";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Nombre", request.Nombre);
                command.Parameters.AddWithValue("@Apellido", request.Apellido);
                command.Parameters.AddWithValue("@Telefono", request.Telefono ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@UsuarioId", userId);

                var affectedRows = await command.ExecuteNonQueryAsync();

                if (affectedRows == 0)
                    return NotFound(new { message = "Datos personales no encontrados." });

                return Ok(new { message = "Datos actualizados correctamente." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }
    }

    public class PersonaUpdateRequest
    {
        public string Nombre { get; set; }
        public string Apellido { get; set; }
        public string Telefono { get; set; }
    }
}