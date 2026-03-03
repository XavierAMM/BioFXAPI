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
		private readonly string _cs;
		public CartItemsController(IConfiguration cfg) => _cs = cfg.GetConnectionString("DefaultConnection");

		[HttpPost("add")]
		public async Task<IActionResult> Add([FromBody] AddItemRequest req)
		{
			if (req.ProductId <= 0 || req.Quantity <= 0) return BadRequest(new { message = "Datos inválidos." });
			if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
			    return Unauthorized(new { message = "Usuario no identificado." });

			using var con = new SqlConnection(_cs);
			await con.OpenAsync();

			var cartId = await con.ExecuteScalarAsync<int?>(
				"SELECT TOP 1 Id FROM ShoppingCart WHERE UserId=@UserId AND Activo=1 ORDER BY Id DESC",
				new { UserId = userId });

			if (!cartId.HasValue)
				cartId = await con.ExecuteScalarAsync<int>(
					@"INSERT INTO ShoppingCart(UserId, CreadoEl, ActualizadoEl, Activo)
                      OUTPUT INSERTED.Id VALUES(@UserId, GETUTCDATE(), GETUTCDATE(), 1)",
					new { UserId = userId });

			var prod = await con.QueryFirstOrDefaultAsync<(decimal Precio, bool Disponible, int Stock)>(
				"SELECT Precio, Disponible, Stock FROM Producto WHERE Id=@Id AND Activo=1",
				new { Id = req.ProductId });
			if (prod == default || !prod.Disponible) return BadRequest(new { message = "Producto no disponible." });
			if (prod.Stock < req.Quantity) return BadRequest(new { message = "Stock insuficiente." });

			var existingId = await con.ExecuteScalarAsync<int?>(
				"SELECT Id FROM CartItem WHERE CartId=@CartId AND ProductId=@Pid AND Activo=1",
				new { CartId = cartId, Pid = req.ProductId });

			if (existingId.HasValue)
			{
				await con.ExecuteAsync(
					"UPDATE CartItem SET Quantity = Quantity + @Qty WHERE Id=@Id",
					new { Qty = req.Quantity, Id = existingId.Value });
			}
			else
			{
				await con.ExecuteAsync(
					@"INSERT INTO CartItem(CartId, ProductId, Quantity, AgregadoEl, Activo)
                      VALUES(@CartId, @Pid, @Qty, GETUTCDATE(), 1)",
					new { CartId = cartId, Pid = req.ProductId, Qty = req.Quantity });
			}

			return Ok(new { message = "Item agregado." });
		}

        [HttpPut("{itemId}")]
        public async Task<IActionResult> UpdateQty(int itemId, [FromBody] UpdateQtyRequest req)
        {
            if (req.Quantity <= 0) return BadRequest(new { message = "Cantidad inválida." });
            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return Unauthorized(new { message = "Usuario no identificado." });

            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var owned = await con.ExecuteScalarAsync<int>(
                @"SELECT COUNT(1)
				  FROM CartItem ci
				  INNER JOIN ShoppingCart sc ON sc.Id = ci.CartId
				  WHERE ci.Id=@Id AND sc.UserId=@Uid AND ci.Activo=1 AND sc.Activo=1",
                new { Id = itemId, Uid = userId });
            if (owned == 0) return Forbid();

            var p = await con.QueryFirstOrDefaultAsync<(int ProductId, int Stock)>(
                @"SELECT ci.ProductId, p.Stock
				  FROM CartItem ci INNER JOIN Producto p ON p.Id=ci.ProductId
				  WHERE ci.Id=@Id AND ci.Activo=1", new { Id = itemId });
            if (p == default) return NotFound(new { message = "Item no encontrado." });
            if (p.Stock < req.Quantity) return BadRequest(new { message = "Stock insuficiente." });

            await con.ExecuteAsync("UPDATE CartItem SET Quantity=@Qty WHERE Id=@Id AND Activo=1",
                new { Qty = req.Quantity, Id = itemId });

            return Ok(new { message = "Cantidad actualizada." });
        }

        [HttpDelete("{itemId}")]
        public async Task<IActionResult> Remove(int itemId)
        {
            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return Unauthorized(new { message = "Usuario no identificado." });
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var owned = await con.ExecuteScalarAsync<int>(
                @"SELECT COUNT(1)
          FROM CartItem ci
          INNER JOIN ShoppingCart sc ON sc.Id = ci.CartId
          WHERE ci.Id=@Id AND sc.UserId=@Uid AND ci.Activo=1 AND sc.Activo=1",
                new { Id = itemId, Uid = userId });
            if (owned == 0) return Forbid();

            var rows = await con.ExecuteAsync("UPDATE CartItem SET Activo=0 WHERE Id=@Id", new { Id = itemId });
            if (rows == 0) return NotFound(new { message = "Item no encontrado." });
            return Ok(new { message = "Item eliminado." });
        }

        public record AddItemRequest(int ProductId, int Quantity);
		public record UpdateQtyRequest(int Quantity);
	}
}
