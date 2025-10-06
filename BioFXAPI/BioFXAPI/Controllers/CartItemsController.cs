using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Security.Claims;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CartItemsController : ControllerBase
    {
        private readonly string _connectionString;
        public CartItemsController(IConfiguration cfg) => _connectionString = cfg.GetConnectionString("DefaultConnection");

        // Agrega o incrementa un item en el carrito activo
        [HttpPost("add")]
        public async Task<IActionResult> Add([FromBody] AddItemRequest req)
        {
            if (req.ProductoId <= 0 || req.Cantidad <= 0) return BadRequest(new { message = "Datos inválidos." });
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();

            var cartId = await con.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 Id FROM ShoppingCart WHERE UsuarioId=@UserId AND Activo=1 ORDER BY Id DESC",
                new { UserId = userId });

            if (!cartId.HasValue)
                cartId = await con.ExecuteScalarAsync<int>(
                    @"INSERT INTO ShoppingCart(UsuarioId, Activo, CreadoEl, ActualizadoEl)
                      OUTPUT INSERTED.Id VALUES(@UserId,1,GETUTCDATE(),GETUTCDATE())",
                    new { UserId = userId });

            // Precio actual del producto
            var prod = await con.QueryFirstOrDefaultAsync<(decimal Precio, bool Disponible, int Stock)>(
                "SELECT Precio, Disponible, ISNULL(Stock,0) AS Stock FROM Producto WHERE Id=@Id AND Activo=1",
                new { Id = req.ProductoId });
            if (prod == default || !prod.Disponible) return BadRequest(new { message = "Producto no disponible." });
            if (prod.Stock < req.Cantidad) return BadRequest(new { message = "Stock insuficiente." });

            // Si existe item, incrementa; si no, inserta
            var existingId = await con.ExecuteScalarAsync<int?>(
                "SELECT Id FROM CartItem WHERE CartId=@CartId AND ProductoId=@Pid AND Activo=1",
                new { CartId = cartId, Pid = req.ProductoId });

            if (existingId.HasValue)
            {
                await con.ExecuteAsync(
                    @"UPDATE CartItem SET Cantidad = Cantidad + @Cant, PrecioUnitario=@Price, ActualizadoEl=GETUTCDATE()
                      WHERE Id=@Id",
                    new { Cant = req.Cantidad, Price = prod.Precio, Id = existingId.Value });
            }
            else
            {
                await con.ExecuteAsync(
                    @"INSERT INTO CartItem(CartId, ProductoId, Cantidad, PrecioUnitario, Activo, CreadoEl, ActualizadoEl)
                      VALUES(@CartId,@Pid,@Cant,@Price,1,GETUTCDATE(),GETUTCDATE())",
                    new { CartId = cartId, Pid = req.ProductoId, Cant = req.Cantidad, Price = prod.Precio });
            }

            return Ok(new { message = "Item agregado." });
        }

        [HttpPut("{itemId}")]
        public async Task<IActionResult> UpdateQty(int itemId, [FromBody] UpdateQtyRequest req)
        {
            if (req.Cantidad <= 0) return BadRequest(new { message = "Cantidad inválida." });

            using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();

            // Verifica stock
            var p = await con.QueryFirstOrDefaultAsync<(int ProductoId, int Stock)>(
                @"SELECT ci.ProductoId, ISNULL(p.Stock,0) AS Stock
                  FROM CartItem ci INNER JOIN Producto p ON p.Id=ci.ProductoId WHERE ci.Id=@Id AND ci.Activo=1",
                new { Id = itemId });

            if (p == default) return NotFound(new { message = "Item no encontrado." });
            if (p.Stock < req.Cantidad) return BadRequest(new { message = "Stock insuficiente." });

            await con.ExecuteAsync(
                "UPDATE CartItem SET Cantidad=@Cant, ActualizadoEl=GETUTCDATE() WHERE Id=@Id AND Activo=1",
                new { Cant = req.Cantidad, Id = itemId });

            return Ok(new { message = "Cantidad actualizada." });
        }

        [HttpDelete("{itemId}")]
        public async Task<IActionResult> Remove(int itemId)
        {
            using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            var rows = await con.ExecuteAsync(
                "UPDATE CartItem SET Activo=0, ActualizadoEl=GETUTCDATE() WHERE Id=@Id",
                new { Id = itemId });
            if (rows == 0) return NotFound(new { message = "Item no encontrado." });
            return Ok(new { message = "Item eliminado." });
        }

        public record AddItemRequest(int ProductoId, int Cantidad);
        public record UpdateQtyRequest(int Cantidad);
    }
}
