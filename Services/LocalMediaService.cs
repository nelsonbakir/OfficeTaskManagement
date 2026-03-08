using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using OfficeTaskManagement.Models.Settings;

namespace OfficeTaskManagement.Services
{
    public class LocalMediaService : IMediaService
    {
        private readonly IWebHostEnvironment _env;
        private readonly MediaSettings _settings;

        public LocalMediaService(IWebHostEnvironment env, IOptions<MediaSettings> settings)
        {
            _env = env;
            _settings = settings.Value;
        }

        public async Task<string> UploadAsync(Stream fileStream, string fileName, string contentType)
        {
            string uploadsFolder = Path.Combine(_env.WebRootPath, _settings.LocalPath);
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(fileName);
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var destinationStream = new FileStream(filePath, FileMode.Create))
            {
                await fileStream.CopyToAsync(destinationStream);
            }

            return Path.Combine("/", _settings.LocalPath, uniqueFileName).Replace("\\", "/");
        }

        public Task DeleteAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return Task.CompletedTask;

            string fullPath = Path.Combine(_env.WebRootPath, filePath.TrimStart('/'));
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return Task.CompletedTask;
        }

        public string GetUrl(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;
            return filePath.StartsWith("http") ? filePath : filePath;
        }
    }
}
