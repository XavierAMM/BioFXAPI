using BioFXAPI.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic; 
using System.Data.SqlClient;
using System.Linq;
using System.Security.Claims;


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

        private async Task<bool> IsAdmin(SqlConnection con, int userId)
        {
            return await con.ExecuteScalarAsync<int>(
                "SELECT CASE WHEN esAdministrador=1 THEN 1 ELSE 0 END FROM Usuario WHERE Id=@Id AND Activo=1",
                new { Id = userId }) == 1;
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetProductos()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Consulta modificada para incluir información de categoría
                var productos = (await connection.QueryAsync<Producto>(@"
                    SELECT *
                    FROM Producto
                    WHERE Activo = 1
                    ORDER BY Nombre")).ToList();

                // Cargar categorías de todos
                var ids = productos.Select(x => x.Id).ToArray();
                if (ids.Length > 0)
                {
                    var catRows = await connection.QueryAsync<(int ProductoId, int CategoriaId, string Descripcion)>(@"
                        SELECT cp.ProductoId, c.Id AS CategoriaId, c.Descripcion
                        FROM CategoriasProductos cp
                        INNER JOIN Categoria c ON c.Id = cp.CategoriaId
                        WHERE cp.Activo = 1 AND cp.ProductoId IN @ids", new { ids });

                    // Mapear
                    var byProd = catRows.GroupBy(r => r.ProductoId);
                    foreach (var g in byProd)
                    {
                        var p = productos.FirstOrDefault(x => x.Id == g.Key);
                        if (p != null)
                        {
                            p.Categorias = g
                                .Select(r => new CategoriaProducto { ProductoId = g.Key, CategoriaId = r.CategoriaId })
                                .ToList();
                        }
                    }
                }

                // Promocionados:
                if (ids.Length > 0)
                {
                    var promoRows = await connection.QueryAsync<(int ProductoId, int PromocionadoId)>(@"
                        SELECT ProductoId, PromocionadoId
                        FROM ProductoPromocionado
                        WHERE Activo = 1 AND ProductoId IN @ids", new { ids });

                    var promoByProd = promoRows.GroupBy(r => r.ProductoId)
                                               .ToDictionary(g => g.Key, g => g.Select(x => x.PromocionadoId).ToList());

                    foreach (var p in productos)
                        p.Promocionados = promoByProd.TryGetValue(p.Id, out var lista) ? lista : new List<int>();
                }

                return Ok(productos);

            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [AllowAnonymous]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProducto(int id)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var producto = await connection.QueryFirstOrDefaultAsync<Producto>(@"
                    SELECT *
                    FROM Producto
                    WHERE Id = @Id AND Activo = 1", new { Id = id });

                if (producto == null) return NotFound(new { message = "Producto no encontrado." });

                // Categorías
                var catRows = await connection.QueryAsync<(int CategoriaId, string Descripcion)>(@"
                    SELECT c.Id AS CategoriaId, c.Descripcion
                    FROM CategoriasProductos cp
                    INNER JOIN Categoria c ON c.Id = cp.CategoriaId
                    WHERE cp.Activo = 1 AND cp.ProductoId = @Id", new { Id = id });

                producto.Categorias = catRows
                    .Select(r => new CategoriaProducto { ProductoId = id, CategoriaId = r.CategoriaId })
                    .ToList();

                // Promocionados
                var promocionados = await connection.QueryAsync<int>(@"
                    SELECT PromocionadoId
                    FROM ProductoPromocionado
                    WHERE ProductoId = @ProductoId AND Activo = 1", new { ProductoId = id });
                producto.Promocionados = promocionados.ToList();

                return Ok(producto);

            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [AllowAnonymous]
        [HttpGet("categoria/{categoriaId}")]
        public async Task<IActionResult> GetProductosPorCategoria(int categoriaId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var productos = (await connection.QueryAsync<Producto>(@"
                    SELECT p.*
                    FROM Producto p
                    INNER JOIN CategoriasProductos cp ON cp.ProductoId = p.Id AND cp.Activo = 1
                    WHERE cp.CategoriaId = @CategoriaId AND p.Activo = 1
                    ORDER BY p.Nombre", new { CategoriaId = categoriaId })).ToList();

                // Cargar categorías
                var ids = productos.Select(x => x.Id).ToArray();
                if (ids.Length > 0)
                {
                    var catRows = await connection.QueryAsync<(int ProductoId, int CategoriaId, string Descripcion)>(@"
                        SELECT cp.ProductoId, c.Id AS CategoriaId, c.Descripcion
                        FROM CategoriasProductos cp
                        INNER JOIN Categoria c ON c.Id = cp.CategoriaId
                        WHERE cp.Activo = 1 AND cp.ProductoId IN @ids", new { ids });

                    foreach (var g in catRows.GroupBy(r => r.ProductoId))
                    {
                        var p = productos.FirstOrDefault(x => x.Id == g.Key);
                        if (p != null)
                        {
                            p.Categorias = g.Select(r => new CategoriaProducto { ProductoId = g.Key, CategoriaId = r.CategoriaId }).ToList();
                        }
                    }
                }

                // Promocionados
                if (ids.Length > 0)
                {
                    var promoRows = await connection.QueryAsync<(int ProductoId, int PromocionadoId)>(@"
                        SELECT ProductoId, PromocionadoId
                        FROM ProductoPromocionado
                        WHERE Activo = 1 AND ProductoId IN @ids", new { ids });

                    var promoByProd = promoRows.GroupBy(r => r.ProductoId)
                                               .ToDictionary(g => g.Key, g => g.Select(x => x.PromocionadoId).ToList());

                    foreach (var p in productos)
                        p.Promocionados = promoByProd.TryGetValue(p.Id, out var lista) ? lista : new List<int>();
                }

                return Ok(productos);

            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CrearProducto([FromBody] ProductoUpsertDto dto)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
                if (!await IsAdmin(connection, userId)) return Forbid();

                using var tx = connection.BeginTransaction();
                try
                {
                    var insertQuery = @"
                        INSERT INTO Producto
                        (Codigo, Disponible, Nombre, Precio, Imagen, Logo, Descripcion,
                         Desc_Principal, Desc_Otros, Descuento, Disclaimer, Activo,
                         Stock, StockReservado, Contraindicaciones)
                        OUTPUT INSERTED.Id
                        VALUES
                        (@Codigo, @Disponible, @Nombre, @Precio, @Imagen, @Logo, @Descripcion,
                         @Desc_Principal, @Desc_Otros, @Descuento, @Disclaimer, @Activo,
                         @Stock, @StockReservado, @Contraindicaciones)";

                    var productoId = await connection.ExecuteScalarAsync<int>(insertQuery, dto, tx);

                    // Categorías
                    if (dto.CategoriaIds?.Any() == true)
                    {
                        await connection.ExecuteAsync(@"
                            INSERT INTO CategoriasProductos (ProductoId, CategoriaId, Activo)
                            VALUES (@ProductoId, @CategoriaId, 1)",
                            dto.CategoriaIds.Select(id => new { ProductoId = productoId, CategoriaId = id }), tx);
                    }

                    // Promocionados
                    if (dto.Promocionados?.Any() == true)
                    {
                        await connection.ExecuteAsync(@"
                            INSERT INTO ProductoPromocionado (ProductoId, PromocionadoId, Activo)
                            VALUES (@ProductoId, @PromocionadoId, 1)",
                            dto.Promocionados.Select(pid => new { ProductoId = productoId, PromocionadoId = pid }), tx);
                    }

                    tx.Commit();
                    return Ok(new { message = "Producto creado.", id = productoId });
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [Authorize]
        [HttpPut("{id}")]
        public async Task<IActionResult> ActualizarProducto(int id, [FromBody] ProductoUpsertDto dto)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
                if (!await IsAdmin(connection, userId)) return Forbid();

                using var tx = connection.BeginTransaction();
                try
                {
                    var exists = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM Producto WHERE Id=@Id AND Activo=1",
                        new { Id = id }, tx);
                    if (exists == 0) return NotFound(new { message = "Producto no encontrado." });

                    var updateQuery = @"
                        UPDATE Producto SET 
                            Codigo=@Codigo, Disponible=@Disponible, Nombre=@Nombre,
                            Precio=@Precio, Imagen=@Imagen, Logo=@Logo, Descripcion=@Descripcion,
                            Desc_Principal=@Desc_Principal, Desc_Otros=@Desc_Otros,
                            Descuento=@Descuento, Disclaimer=@Disclaimer,
                            Contraindicaciones=@Contraindicaciones,
                            Stock=@Stock, StockReservado=@StockReservado,
                            ActualizadoEl = GETUTCDATETIME()
                        WHERE Id=@Id";

                    await connection.ExecuteAsync(updateQuery, new
                    {
                        Id = id,
                        dto.Codigo,
                        dto.Disponible,
                        dto.Nombre,
                        dto.Precio,
                        dto.Imagen,
                        dto.Logo,
                        dto.Descripcion,
                        dto.Desc_Principal,
                        dto.Desc_Otros,
                        dto.Descuento,
                        dto.Disclaimer,
                        dto.Contraindicaciones,
                        dto.Stock,
                        dto.StockReservado
                    }, tx);

                    // Reset de categorías actuales
                    await connection.ExecuteAsync(@"
                        UPDATE CategoriasProductos
                        SET Activo = 0, ActualizadoEl = GETUTCDATETIME()
                        WHERE ProductoId = @Id AND Activo = 1", new { Id = id }, tx);

                    if (dto.CategoriaIds?.Any() == true)
                    {
                        await connection.ExecuteAsync(@"
                            MERGE CategoriasProductos AS T
                            USING (SELECT @ProductoId AS ProductoId, @CategoriaId AS CategoriaId) AS S
                            ON T.ProductoId = S.ProductoId AND T.CategoriaId = S.CategoriaId
                            WHEN MATCHED THEN
                                UPDATE SET Activo = 1, ActualizadoEl = GETUTCDATETIME()
                            WHEN NOT MATCHED THEN
                                INSERT (ProductoId, CategoriaId, Activo) VALUES (S.ProductoId, S.CategoriaId, 1);",
                            dto.CategoriaIds.Select(cid => new { ProductoId = id, CategoriaId = cid }), tx);
                    }

                    // Promocionados: desactivar e insertar
                    await connection.ExecuteAsync(@"
                        UPDATE ProductoPromocionado SET Activo = 0, ActualizadoEl = GETUTCDATETIME()
                        WHERE ProductoId = @Id", new { Id = id }, tx);

                    if (dto.Promocionados?.Any() == true)
                    {
                        await connection.ExecuteAsync(@"
                            INSERT INTO ProductoPromocionado (ProductoId, PromocionadoId, Activo)
                            VALUES (@ProductoId, @PromocionadoId, 1)",
                            dto.Promocionados.Select(pid => new { ProductoId = id, PromocionadoId = pid }), tx);
                    }

                    tx.Commit();
                    return Ok(new { message = "Producto actualizado." });
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> EliminarProducto(int id)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
                if (!await IsAdmin(connection, userId)) return Forbid();

                var affected = await connection.ExecuteAsync(@"
                    UPDATE Producto SET Activo = 0, ActualizadoEl = GETUTCDATETIME()
                    WHERE Id = @Id", new { Id = id });

                if (affected == 0) return NotFound(new { message = "Producto no encontrado." });

                await connection.ExecuteAsync(@"
                    UPDATE ProductoPromocionado
                    SET Activo = 0, ActualizadoEl = GETUTCDATETIME()
                    WHERE ProductoId = @Id OR PromocionadoId = @Id", new { Id = id });

                await connection.ExecuteAsync(@"
                    UPDATE CategoriasProductos
                    SET Activo = 0, ActualizadoEl = GETUTCDATETIME()
                    WHERE ProductoId = @Id", new { Id = id });

                return Ok(new { message = "Producto eliminado." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Error de base de datos", details = ex.Message });
            }
        }

    }

    public class ProductoUpsertDto
    {
        public string Codigo { get; set; }
        public bool Disponible { get; set; } = true;
        public string Nombre { get; set; }
        public decimal Precio { get; set; }
        public string Imagen { get; set; }
        public string Logo { get; set; }
        public string Descripcion { get; set; }
        public string Desc_Principal { get; set; }
        public string Desc_Otros { get; set; }
        public int Descuento { get; set; } = 0;
        public string Disclaimer { get; set; }
        public string Contraindicaciones { get; set; }
        public int Stock { get; set; } = 0;
        public int StockReservado { get; set; } = 0;
        public bool Activo { get; set; } = true;
        public List<int> Promocionados { get; set; } = new();
        public List<int> CategoriaIds { get; set; } = new(); // NUEVO
    }

}