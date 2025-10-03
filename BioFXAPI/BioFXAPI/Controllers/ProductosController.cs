using BioFXAPI.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using Dapper;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductosController : ControllerBase
    {
        private readonly string _connectionString;

        public ProductosController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        public async Task<IActionResult> GetProductos()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Consulta modificada para incluir información de categoría
                var query = @"
                    SELECT 
                        p.*, 
                        c.Descripcion as CategoriaDescripcion
                    FROM Producto p
                    INNER JOIN Categoria c ON p.CategoriaId = c.Id
                    WHERE p.Activo = 1 
                    ORDER BY p.Nombre";

                var productos = await connection.QueryAsync<Producto>(query);

                // Para cada producto, obtener los IDs de productos promocionados
                foreach (var producto in productos)
                {
                    var promocionadosQuery = @"SELECT PromocionadoId FROM ProductoPromocionado 
                                              WHERE ProductoId = @ProductoId AND Activo = 1";
                    var promocionados = await connection.QueryAsync<int>(promocionadosQuery,
                        new { ProductoId = producto.Id });

                    producto.Promocionados = promocionados.ToList();
                }

                return Ok(productos);
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProducto(int id)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        p.*, 
                        c.Descripcion as CategoriaDescripcion
                    FROM Producto p
                    INNER JOIN Categoria c ON p.CategoriaId = c.Id
                    WHERE p.Id = @Id AND p.Activo = 1";

                var producto = await connection.QueryFirstOrDefaultAsync<Producto>(query, new { Id = id });

                if (producto == null)
                    return NotFound(new { message = "Producto no encontrado." });

                // Obtener IDs de productos promocionados
                var promocionadosQuery = @"SELECT PromocionadoId FROM ProductoPromocionado 
                                          WHERE ProductoId = @ProductoId AND Activo = 1";
                var promocionados = await connection.QueryAsync<int>(promocionadosQuery,
                    new { ProductoId = id });

                producto.Promocionados = promocionados.ToList();

                return Ok(producto);
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [HttpGet("categoria/{categoriaId}")]
        public async Task<IActionResult> GetProductosPorCategoria(int categoriaId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        p.*, 
                        c.Descripcion as CategoriaDescripcion
                    FROM Producto p
                    INNER JOIN Categoria c ON p.CategoriaId = c.Id
                    WHERE p.CategoriaId = @CategoriaId AND p.Activo = 1 
                    ORDER BY p.Nombre";

                var productos = await connection.QueryAsync<Producto>(query, new { CategoriaId = categoriaId });

                foreach (var producto in productos)
                {
                    var promocionadosQuery = @"SELECT PromocionadoId FROM ProductoPromocionado 
                                              WHERE ProductoId = @ProductoId AND Activo = 1";
                    var promocionados = await connection.QueryAsync<int>(promocionadosQuery,
                        new { ProductoId = producto.Id });

                    producto.Promocionados = promocionados.ToList();
                }

                return Ok(productos);
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CrearProducto([FromBody] Producto producto)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = connection.BeginTransaction();
                try
                {
                    // Insertar producto
                    var insertQuery = @"INSERT INTO Producto 
                        (Codigo, Disponible, Nombre, Precio, Imagen, Logo, Descripcion, 
                         CategoriaId, Desc_Principal, Desc_Otros, Descuento, Disclaimer, Activo, Stock, StockReservado)
                        OUTPUT INSERTED.Id
                        VALUES (@Codigo, @Disponible, @Nombre, @Precio, @Imagen, @Logo, 
                                @Descripcion, @CategoriaId, @Desc_Principal, @Desc_Otros, 
                                @Descuento, @Disclaimer, @Activo, @Stock, @StockReservado)";

                    var productoId = await connection.ExecuteScalarAsync<int>(insertQuery, producto, transaction);

                    // Insertar productos promocionados
                    if (producto.Promocionados != null && producto.Promocionados.Any())
                    {
                        var promocionadosQuery = @"INSERT INTO ProductoPromocionado 
                            (ProductoId, PromocionadoId, Activo)
                            VALUES (@ProductoId, @PromocionadoId, 1)";

                        foreach (var promocionadoId in producto.Promocionados)
                        {
                            await connection.ExecuteAsync(promocionadosQuery,
                                new { ProductoId = productoId, PromocionadoId = promocionadoId },
                                transaction);
                        }
                    }

                    transaction.Commit();

                    return Ok(new
                    {
                        message = "Producto creado exitosamente.",
                        id = productoId
                    });
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> ActualizarProducto(int id, [FromBody] Producto producto)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = connection.BeginTransaction();
                try
                {
                    // Verificar si el producto existe
                    var checkQuery = "SELECT COUNT(*) FROM Producto WHERE Id = @Id AND Activo = 1";
                    var count = await connection.ExecuteScalarAsync<int>(checkQuery, new { Id = id }, transaction);

                    if (count == 0)
                        return NotFound(new { message = "Producto no encontrado." });

                    // Actualizar producto
                    var updateQuery = @"UPDATE Producto SET 
                        Codigo = @Codigo, Disponible = @Disponible, Nombre = @Nombre, 
                        Precio = @Precio, Imagen = @Imagen, Logo = @Logo, Descripcion = @Descripcion,
                        CategoriaId = @CategoriaId, Desc_Principal = @Desc_Principal, 
                        Desc_Otros = @Desc_Otros, Descuento = @Descuento, Disclaimer = @Disclaimer,
                        ActualizadoEl = GETUTCDATE(), Stock = @Stock, StockReservado = @StockReservado
                        WHERE Id = @Id";

                    producto.Id = id;
                    await connection.ExecuteAsync(updateQuery, producto, transaction);

                    // Eliminar promocionados existentes
                    var deletePromocionadosQuery = @"UPDATE ProductoPromocionado SET Activo = 0, 
                                                   ActualizadoEl = GETUTCDATE()
                                                   WHERE ProductoId = @ProductoId";
                    await connection.ExecuteAsync(deletePromocionadosQuery,
                        new { ProductoId = id }, transaction);

                    // Insertar nuevos productos promocionados
                    if (producto.Promocionados != null && producto.Promocionados.Any())
                    {
                        var insertPromocionadosQuery = @"INSERT INTO ProductoPromocionado 
                            (ProductoId, PromocionadoId, Activo)
                            VALUES (@ProductoId, @PromocionadoId, 1)";

                        foreach (var promocionadoId in producto.Promocionados)
                        {
                            await connection.ExecuteAsync(insertPromocionadosQuery,
                                new { ProductoId = id, PromocionadoId = promocionadoId },
                                transaction);
                        }
                    }

                    transaction.Commit();

                    return Ok(new { message = "Producto actualizado exitosamente." });
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> EliminarProducto(int id)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Soft delete del producto
                var query = @"UPDATE Producto SET Activo = 0, ActualizadoEl = GETUTCDATE()
                             WHERE Id = @Id";
                var affectedRows = await connection.ExecuteAsync(query, new { Id = id });

                if (affectedRows == 0)
                    return NotFound(new { message = "Producto no encontrado." });

                // También desactivar las relaciones de promoción
                var promocionadosQuery = @"UPDATE ProductoPromocionado SET Activo = 0, 
                                         ActualizadoEl = GETUTCDATE()
                                         WHERE ProductoId = @ProductoId OR PromocionadoId = @ProductoId";
                await connection.ExecuteAsync(promocionadosQuery, new { ProductoId = id });

                return Ok(new { message = "Producto eliminado exitosamente." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }
    }
}