using BioFXAPI.Notifications;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BioFXAPI.Services;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TransactionsController : ControllerBase
    {
        private readonly string _cs;
        private readonly IConfiguration _cfg;        
        private readonly PlacetoPayRefreshService _refresh;
        private readonly OrderCancellationService _cancelSvc;

        public TransactionsController(IConfiguration cfg,  PlacetoPayRefreshService refresh, OrderCancellationService cancelSvc)
        {
            _cfg = cfg;
            _cs = cfg.GetConnectionString("DefaultConnection");            
            _refresh = refresh;
            _cancelSvc = cancelSvc;
        }

        [Authorize]
        [HttpPost("refresh-by-request")]
        public async Task<IActionResult> RefreshByRequestId([FromBody] RefreshRequest r, CancellationToken ct)
        {
            var (error, result) = await _refresh.RefreshByRequestIdAsync(
            requestId: r.RequestId,
            validateOwner: true,
            callerUser: User,
            ct: ct);

            if (error != null) return error;
            return Ok(new { orderId = result!.OrderId, status = result.Status });
        }

        [AllowAnonymous]
        [HttpPost("internal/refresh-by-request")]
        public async Task<IActionResult> RefreshByRequestIdInternal([FromBody] RefreshRequest r, CancellationToken ct)
        {
            // Validar API key interna
            var configured = _cfg["InternalApiKey"];
            if (string.IsNullOrWhiteSpace(configured))
                return StatusCode(500, new { message = "InternalApiKey no configurada." });

            if (!Request.Headers.TryGetValue("X-Internal-Api-Key", out var provided) ||
                string.IsNullOrWhiteSpace(provided) ||
                !string.Equals(provided.ToString(), configured, StringComparison.Ordinal))
            {
                return Unauthorized(new { message = "Invalid internal key." });
            }

            var (error, result) = await _refresh.RefreshByRequestIdAsync(
            requestId: r.RequestId,
            validateOwner: false,
            callerUser: null,
            ct: ct);

            if (error != null) return error;
            return Ok(new { orderId = result!.OrderId, status = result.Status });
        }

        
        [AllowAnonymous]
        [HttpPost("internal/cancel-by-request")]
        public async Task<IActionResult> CancelByRequestInternal([FromBody] RefreshRequest r, CancellationToken ct)
        {
            var configured = _cfg["InternalApiKey"];
            if (string.IsNullOrWhiteSpace(configured))
                return StatusCode(500, new { message = "InternalApiKey no configurada." });

            if (!Request.Headers.TryGetValue("X-Internal-Api-Key", out var provided) ||
                string.IsNullOrWhiteSpace(provided) ||
                !string.Equals(provided.ToString(), configured, StringComparison.Ordinal))
            {
                return Unauthorized(new { message = "Invalid internal key." });
            }

            using var con = new SqlConnection(_cs);
            await con.OpenAsync(ct);

            var orderId = await con.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 OrderId FROM [Transaction] WHERE RequestId=@Rid AND Activo=1 ORDER BY Id DESC",
                new { Rid = r.RequestId });

            if (orderId is null) return NotFound(new { message = "Transacción no encontrada." });

            var (error, status) = await _cancelSvc.CancelOrderAsync(orderId.Value, tryCancelGateway: true, ct);
            if (error != null) return error;

            return Ok(new { orderId = orderId.Value, status });
        }

        public record RefreshRequest(int RequestId);

    }
}
