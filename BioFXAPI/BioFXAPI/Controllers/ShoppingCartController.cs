using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Linq;

namespace BioFXAPI.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	[Authorize]
	public class ShoppingCartController : ControllerBase
	{
		private readonly string _cs;
		public ShoppingCartController(IConfiguration cfg) => _cs = cfg.GetConnectionString("DefaultConnection");

		[HttpGet("mine")]
		public async Task<IActionResult> GetOrCreateMyCart()
		{
			var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
			using var con = new SqlConnection(_cs);
			await con.OpenAsync();

			var cartId = await con.ExecuteScalarAsync<int?>(
				"SELECT TOP 1 Id FROM ShoppingCart WHERE UserId=@UserId AND Activo=1 ORDER BY Id DESC",
				new { UserId = userId });

			if (!cartId.HasValue)
			{
				cartId = await con.ExecuteScalarAsync<int>(
					@"INSERT INTO ShoppingCart(UserId, CreadoEl, ActualizadoEl, Activo)
                      OUTPUT INSERTED.Id VALUES(@UserId, GETUTCDATETIME(), GETUTCDATETIME(), 1)",
					new { UserId = userId });
			}

			var items = await con.QueryAsync<dynamic>(
				@"SELECT ci.Id,
                         ci.ProductId,
                         p.Nombre,
                         p.Precio AS UnitPrice,
                         ci.Quantity,
                         (ci.Quantity * p.Precio) AS Subtotal
                  FROM CartItem ci
                  INNER JOIN Producto p ON p.Id = ci.ProductId
                  WHERE ci.CartId = @CartId AND ci.Activo = 1",
				new { CartId = cartId });

			var total = items.Any() ? items.Sum(i => (decimal)i.Subtotal) : 0m;

			return Ok(new { CartId = cartId, Items = items, Total = total, Currency = "USD" });
		}

		[HttpPost("clear")]
		public async Task<IActionResult> ClearMyCart()
		{
			var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
			using var con = new SqlConnection(_cs);
			await con.OpenAsync();

			var cartId = await con.ExecuteScalarAsync<int?>(
				"SELECT TOP 1 Id FROM ShoppingCart WHERE UserId=@UserId AND Activo=1 ORDER BY Id DESC",
				new { UserId = userId });

			if (!cartId.HasValue) return Ok(new { message = "Carrito ya vacío." });

			await con.ExecuteAsync(
				@"UPDATE CartItem SET Activo=0 WHERE CartId=@CartId;
                  UPDATE ShoppingCart SET ActualizadoEl=GETUTCDATETIME() WHERE Id=@CartId;",
				new { CartId = cartId });

			return Ok(new { message = "Carrito vaciado." });
		}
	}
}
