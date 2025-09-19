using BioFXAPI.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using Dapper;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CategoriasController : ControllerBase
    {
        private readonly string _connectionString;

        public CategoriasController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        public async Task<IActionResult> GetCategorias()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = "SELECT * FROM Categoria WHERE Activo = 1 ORDER BY Descripcion";
                var categorias = await connection.QueryAsync<Categoria>(query);

                return Ok(categorias);
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }
    }
}