using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BioFXAPI.Services
{
    public interface IFileStorageService
    {
        Task<string> UploadAsync(
            Stream stream,
            string key,
            string contentType,
            CancellationToken cancellationToken = default);

        Task<Stream> GetAsync(
            string key,
            CancellationToken cancellationToken = default);

        Task DeleteAsync(
            string key,
            CancellationToken cancellationToken = default);
    }
}
