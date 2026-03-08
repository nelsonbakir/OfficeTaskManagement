using System;
using System.IO;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Options;
using OfficeTaskManagement.Models.Settings;

namespace OfficeTaskManagement.Services
{
    public class S3MediaService : IMediaService
    {
        private readonly MediaSettings _settings;
        private readonly IAmazonS3 _s3Client;

        public S3MediaService(IOptions<MediaSettings> settings)
        {
            _settings = settings.Value;
            
            var config = new AmazonS3Config();
            if (!string.IsNullOrEmpty(_settings.S3ServiceUrl))
            {
                config.ServiceURL = _settings.S3ServiceUrl;
                config.ForcePathStyle = true; // Often needed for S3-compatible services
            }
            else if (!string.IsNullOrEmpty(_settings.S3Region))
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(_settings.S3Region);
            }

            _s3Client = new AmazonS3Client(_settings.S3AccessKey, _settings.S3SecretKey, config);
        }

        public async Task<string> UploadAsync(Stream fileStream, string fileName, string contentType)
        {
            string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(fileName);
            
            var uploadRequest = new TransferUtilityUploadRequest
            {
                InputStream = fileStream,
                Key = uniqueFileName,
                BucketName = _settings.S3BucketName,
                ContentType = contentType,
                CannedACL = S3CannedACL.PublicRead
            };

            var fileTransferUtility = new TransferUtility(_s3Client);
            await fileTransferUtility.UploadAsync(uploadRequest);

            return uniqueFileName;
        }

        public async Task DeleteAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            var deleteObjectRequest = new DeleteObjectRequest
            {
                BucketName = _settings.S3BucketName,
                Key = filePath
            };

            await _s3Client.DeleteObjectAsync(deleteObjectRequest);
        }

        public string GetUrl(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;
            
            if (filePath.StartsWith("http")) return filePath;

            // Generate a pre-signed URL or construct the public URL if appropriate
            // For now, let's assume construct public URL or use the S3 Client to get it
            // Constructing public URL (assuming it's public):
            if (!string.IsNullOrEmpty(_settings.S3ServiceUrl))
            {
                return $"{_settings.S3ServiceUrl.TrimEnd('/')}/{_settings.S3BucketName}/{filePath}";
            }
            
            return $"https://{_settings.S3BucketName}.s3.{_settings.S3Region}.amazonaws.com/{filePath}";
        }
    }
}
