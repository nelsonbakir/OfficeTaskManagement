using System.IO;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Services
{
    public interface IMediaService
    {
        Task<string> UploadAsync(Stream fileStream, string fileName, string contentType);
        Task DeleteAsync(string filePath);
        string GetUrl(string filePath);
    }
}
