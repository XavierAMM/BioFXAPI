using BioFXAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Security.Claims;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DatosFacturacionController : ControllerBase
    {
        private readonly string _connectionString;

        public DatosFacturacionController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return Unauthorized(new { message = "Usuario no identificado." });

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"SELECT Id, UsuarioId, Nombre_Razon_Social, RUC_Cedula, Direccion, Telefono, Email, Activo, CreadoEl, ActualizadoEl
                              FROM DatosFacturacion 
                              WHERE UsuarioId = @UsuarioId AND Activo = 1";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UsuarioId", userId);

                using var reader = await command.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return NotFound(new { message = "Datos de facturación no encontrados." });

                var datosFacturacion = new
                {
                    Id = reader.GetInt32(0),
                    UsuarioId = reader.GetInt32(1),
                    Nombre_Razon_Social = reader.IsDBNull(2) ? null : reader.GetString(2),
                    RUC_Cedula = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Direccion = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Telefono = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Email = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Activo = reader.GetBoolean(7),
                    CreadoEl = reader.GetDateTime(8),
                    ActualizadoEl = reader.GetDateTime(9)
                };

                return Ok(datosFacturacion);
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] DatosFacturacionRequest request)
        {
            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return Unauthorized(new { message = "Usuario no identificado." });

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verificar si ya existe un registro activo para el usuario
                var checkQuery = "SELECT COUNT(*) FROM DatosFacturacion WHERE UsuarioId = @UsuarioId AND Activo = 1";
                using var checkCommand = new SqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@UsuarioId", userId);
                var count = (int)await checkCommand.ExecuteScalarAsync();

                if (count > 0)
                    return BadRequest(new { message = "Ya existen datos de facturación activos para este usuario. Use PUT para actualizar." });

                var query = @"INSERT INTO DatosFacturacion (UsuarioId, Nombre_Razon_Social, RUC_Cedula, Direccion, Telefono, Email, Activo, CreadoEl, ActualizadoEl)
                              VALUES (@UsuarioId, @NombreRazonSocial, @RucCedula, @Direccion, @Telefono, @Email, 1, GETUTCDATE(), GETUTCDATE())";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UsuarioId", userId);
                command.Parameters.AddWithValue("@NombreRazonSocial", (object)request.Nombre_Razon_Social ?? DBNull.Value);
                command.Parameters.AddWithValue("@RucCedula", (object)request.RUC_Cedula ?? DBNull.Value);
                command.Parameters.AddWithValue("@Direccion", (object)request.Direccion ?? DBNull.Value);
                command.Parameters.AddWithValue("@Telefono", (object)request.Telefono ?? DBNull.Value);
                command.Parameters.AddWithValue("@Email", (object)request.Email ?? DBNull.Value);

                await command.ExecuteNonQueryAsync();

                return Ok(new { message = "Datos de facturación creados exitosamente." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos" });
            }
        }

        [HttpPut]
        public async Task<IActionResult> Put([FromBody] DatosFacturacionRequest request)
        {
            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return Unauthorized(new { message = "Usuario no identificado." });

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"UPDATE DatosFacturacion 
                              SET Nombre_Razon_Social = @NombreRazonSocial, 
                                  RUC_Cedula = @RucCedula, 
                                  Direccion = @Direccion, 
                                  Telefono = @Telefono, 
                                  Email = @Email, 
                                  ActualizadoEl = GETUTCDATE()
                              WHERE UsuarioId = @UsuarioId AND Activo = 1";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UsuarioId", userId);
                command.Parameters.AddWithValue("@NombreRazonSocial", (object)request.Nombre_Razon_Social ?? DBNull.Value);
                command.Parameters.AddWithValue("@RucCedula", (object)request.RUC_Cedula ?? DBNull.Value);
                command.Parameters.AddWithValue("@Direccion", (object)request.Direccion ?? DBNull.Value);
                command.Parameters.AddWithValue("@Telefono", (object)request.Telefono ?? DBNull.Value);
                command.Parameters.AddWithValue("@Email", (object)request.Email ?? DBNull.Value);

                var affectedRows = await command.ExecuteNonQueryAsync();

                if (affectedRows == 0)
                    return NotFound(new { message = "Datos de facturación no encontrados para actualizar." });

                return Ok(new { message = "Datos de facturación actualizados exitosamente." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos" });
            }
        }
    }

    public class DatosFacturacionRequest
    {
        public string Nombre_Razon_Social { get; set; }
        public string RUC_Cedula { get; set; }
        public string Direccion { get; set; }
        public string Telefono { get; set; }
        public string Email { get; set; }
    }
}