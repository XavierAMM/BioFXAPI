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
    public class OrderItemsController : ControllerBase
    {
        private readonly string _cs;
        public OrderItemsController(IConfiguration cfg) => _cs = cfg.GetConnectionString("DefaultConnection");

        [HttpGet("{orderId:int}")]
        public async Task<IActionResult> List(int orderId)        
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))

                return Unauthorized(new { message = "Usuario no identificado." });
            var isMine = await con.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM [Order] WHERE Id=@Id AND UserId=@Uid AND Activo=1",
                new { Id = orderId, Uid = userId });
            if (isMine == 0) return Forbid();

            var items = await con.QueryAsync<dynamic>(
                @"SELECT oi.Id, oi.ProductId, p.Nombre, oi.Quantity, oi.UnitPrice, oi.TotalPrice
                  FROM OrderItem oi
                  INNER JOIN Producto p ON p.Id=oi.ProductId
                  WHERE oi.OrderId=@Id AND oi.Activo=1
                  ORDER BY oi.Id", new { Id = orderId });

            return Ok(items);
        }
    }
}
