namespace OfficeTaskManagement.Models.Settings
{
    public class MediaSettings
    {
        public const string SectionName = "MediaSettings";
        
        public string Provider { get; set; } = "Local"; // Local or S3
        
        // Local Store Settings
        public string LocalPath { get; set; } = "uploads";
        
        // S3 Settings
        public string S3BucketName { get; set; } = string.Empty;
        public string S3AccessKey { get; set; } = string.Empty;
        public string S3SecretKey { get; set; } = string.Empty;
        public string S3Region { get; set; } = string.Empty;
        public string S3ServiceUrl { get; set; } = string.Empty; // For S3 compatible services like MinIO
    }
}
