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
    public class ShoppingCartController : ControllerBase
    {
        private readonly string _connectionString;

        public ShoppingCartController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // Obtiene o crea el carrito activo del usuario
        [HttpGet("mine")]
        public async Task<IActionResult> GetOrCreateMyCart()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();

            var cartId = await con.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 Id FROM ShoppingCart WHERE UsuarioId=@UserId AND Activo=1 ORDER BY Id DESC",
                new { UserId = userId });

            if (!cartId.HasValue)
            {
                cartId = await con.ExecuteScalarAsync<int>(
                    @"INSERT INTO ShoppingCart(UsuarioId, Activo, CreadoEl, ActualizadoEl)
                      OUTPUT INSERTED.Id VALUES(@UserId,1,GETUTCDATE(),GETUTCDATE())",
                    new { UserId = userId });
            }

            var items = await con.QueryAsync<dynamic>(
                @"SELECT ci.Id, ci.ProductoId, p.Nombre, ci.Cantidad, ci.PrecioUnitario, 
                         (ci.Cantidad*ci.PrecioUnitario) AS Subtotal
                  FROM CartItem ci
                  INNER JOIN Producto p ON p.Id=ci.ProductoId
                  WHERE ci.CartId=@CartId AND ci.Activo=1",
                new { CartId = cartId });

            var total = items.Sum(i => (decimal)i.Subtotal);

            return Ok(new { CartId = cartId, Items = items, Total = total, Currency = "USD" });
        }

        // Vacía el carrito activo
        [HttpPost("clear")]
        public async Task<IActionResult> ClearMyCart()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();

            var cartId = await con.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 Id FROM ShoppingCart WHERE UsuarioId=@UserId AND Activo=1 ORDER BY Id DESC",
                new { UserId = userId });

            if (!cartId.HasValue) return Ok(new { message = "Carrito ya vacío." });

            await con.ExecuteAsync(
                @"UPDATE CartItem SET Activo=0, ActualizadoEl=GETUTCDATE() WHERE CartId=@CartId;
                  UPDATE ShoppingCart SET ActualizadoEl=GETUTCDATE() WHERE Id=@CartId;",
                new { CartId = cartId });

            return Ok(new { message = "Carrito vaciado." });
        }
    }
}
