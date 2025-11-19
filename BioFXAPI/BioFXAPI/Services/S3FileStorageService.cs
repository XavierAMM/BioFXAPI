using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using BioFXAPI.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BioFXAPI.Services
{
    public class S3FileStorageService : IFileStorageService
    {
        private readonly IAmazonS3 _s3;
        private readonly S3Options _options;
        private readonly ILogger<S3FileStorageService> _logger;

        public S3FileStorageService(
            IAmazonS3 s3,
            IOptions<S3Options> options,
            ILogger<S3FileStorageService> logger)
        {
            _s3 = s3;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<string> UploadAsync(
            Stream stream,
            string key,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            var request = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key,
                InputStream = stream,
                ContentType = contentType
            };

            var response = await _s3.PutObjectAsync(request, cancellationToken);

            _logger.LogInformation(
                "Uploaded file {Key} to bucket {Bucket}. HTTP status {StatusCode}",
                key, _options.BucketName, response.HttpStatusCode);

            return key;
        }

        public async Task<Stream> GetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            var request = new GetObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key
            };

            var response = await _s3.GetObjectAsync(request, cancellationToken);

            var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;
            return ms;
        }

        public async Task DeleteAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key
            };

            var response = await _s3.DeleteObjectAsync(request, cancellationToken);

            _logger.LogInformation(
                "Deleted file {Key} from bucket {Bucket}. HTTP status {StatusCode}",
                key, _options.BucketName, response.HttpStatusCode);
        }
    }
}
