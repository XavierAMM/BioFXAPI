namespace BioFXAPI.Options
{
    public class S3Options
    {
        public string Region { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        public string? AccessKeyId { get; set; }
        public string? SecretAccessKey { get; set; }
    }
}
