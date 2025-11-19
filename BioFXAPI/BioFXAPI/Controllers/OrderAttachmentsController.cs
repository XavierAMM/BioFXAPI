using System.Data;
using BioFXAPI.Models;
using BioFXAPI.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace BioFXAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrderAttachmentsController : ControllerBase
    {
        private readonly string _cs;
        private readonly IFileStorageService _fileStorage;

        public OrderAttachmentsController(IConfiguration cfg, IFileStorageService fileStorage)
        {
            _cs = cfg.GetConnectionString("DefaultConnection");
            _fileStorage = fileStorage;
        }

        // GET api/OrderAttachments/5  -> solo metadata
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var attachment = await con.QueryFirstOrDefaultAsync<OrderAttachment>(
                @"SELECT Id, FileName, ContentType, FileSize, StorageKey, Tipo, Activo, CreadoEl, ActualizadoEl
                  FROM [OrderAttachment]
                  WHERE Id = @Id AND Activo = 1",
                new { Id = id });

            if (attachment is null)
                return NotFound(new { message = "Adjunto no encontrado." });

            return Ok(attachment);
        }

        [HttpGet("{id:int}/download")]
        public async Task<IActionResult> Download(int id, CancellationToken ct)
        {
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            var attachment = await con.QueryFirstOrDefaultAsync<OrderAttachment>(
                @"SELECT Id, FileName, ContentType, FileSize, StorageKey, Tipo, Activo, CreadoEl, ActualizadoEl
                  FROM [OrderAttachment]
                  WHERE Id = @Id AND Activo = 1",
                new { Id = id });

            if (attachment is null)
                return NotFound(new { message = "Adjunto no encontrado." });

            var stream = await _fileStorage.GetAsync(attachment.StorageKey, ct);

            // Volvemos el stream como archivo descargable/mostrable
            return File(stream, attachment.ContentType, attachment.FileName);
        }

        [HttpPost("test-upload")]
        [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB, ajusta si necesitas
        public async Task<IActionResult> TestUpload(IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "Archivo requerido." });
            }

            var key = $"test/{Guid.NewGuid():N}_{file.FileName}";

            await using var stream = file.OpenReadStream();
            var storedKey = await _fileStorage.UploadAsync(stream, key, file.ContentType, ct);

            return Ok(new
            {
                message = "Archivo subido correctamente a S3.",
                key = storedKey
            });
        }
    }
}
