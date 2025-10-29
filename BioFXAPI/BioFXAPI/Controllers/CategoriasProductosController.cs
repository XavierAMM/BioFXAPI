using BioFXAPI.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CategoriasProductosController : ControllerBase
    {
        private readonly string _connectionString;

        public CategoriasProductosController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetTodos()
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = @"
                SELECT Id, ProductoId, CategoriaId, Activo, CreadoEl, ActualizadoEl
                FROM dbo.CategoriasProductos
                WHERE Activo = 1
                ORDER BY ProductoId, CategoriaId;";

            var rows = await connection.QueryAsync<CategoriaProducto>(sql);
            return Ok(rows);
        }

        [AllowAnonymous]
        [HttpGet("por-producto/{productoId:int}")]
        public async Task<IActionResult> GetPorProducto(int productoId)
        {
            if (productoId <= 0) return BadRequest(new { message = "productoId inválido." });

            using var connection = new SqlConnection(_connectionString);
            var sql = @"
                SELECT Id, ProductoId, CategoriaId, Activo, CreadoEl, ActualizadoEl
                FROM dbo.CategoriasProductos
                WHERE Activo = 1 AND ProductoId = @productoId
                ORDER BY CategoriaId;";

            var rows = await connection.QueryAsync<CategoriaProducto>(sql, new { productoId });
            return Ok(rows);
        }

        [AllowAnonymous]
        [HttpGet("por-categoria/{categoriaId:int}")]
        public async Task<IActionResult> GetPorCategoria(int categoriaId)
        {
            if (categoriaId <= 0) return BadRequest(new { message = "categoriaId inválido." });

            using var connection = new SqlConnection(_connectionString);
            var sql = @"
                SELECT Id, ProductoId, CategoriaId, Activo, CreadoEl, ActualizadoEl
                FROM dbo.CategoriasProductos
                WHERE Activo = 1 AND CategoriaId = @categoriaId
                ORDER BY ProductoId;";

            var rows = await connection.QueryAsync<CategoriaProducto>(sql, new { categoriaId });
            return Ok(rows);
        }
    }
}
