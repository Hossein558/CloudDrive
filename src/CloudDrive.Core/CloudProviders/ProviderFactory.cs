using CloudDrive.Core.Auth;
using CloudDrive.Core.Models;
using Serilog;

namespace CloudDrive.Core.CloudProviders;

/// <summary>
/// نوع سرویس ابری
/// </summary>
public enum CloudProviderType
{
    GoogleDrive,
    OneDrive,
    AmazonS3,
    ArvanCloud,
    MinIO,
    CloudflareR2,
    Custom_S3
}

/// <summary>
/// تنظیمات یک درایو ابری
/// </summary>
public class CloudDriveProfile
{
    public string Name { get; set; } = "My Cloud Drive";
    public string DriveLetter { get; set; } = "Z:";
    public CloudProviderType ProviderType { get; set; } = CloudProviderType.GoogleDrive;
    public bool Enabled { get; set; } = true;

    // Google Drive
    public string? GoogleCredentialsPath { get; set; }

    // OneDrive
    public string? OneDriveClientId { get; set; }

    // S3 / ArvanCloud / MinIO
    public S3Config? S3Config { get; set; }
}

/// <summary>
/// Factory برای ساخت ICloudProvider بر اساس نوع سرویس.
/// </summary>
public static class ProviderFactory
{
    public static ICloudProvider Create(CloudDriveProfile profile, ILogger logger)
    {
        return profile.ProviderType switch
        {
            CloudProviderType.GoogleDrive => CreateGoogleDrive(profile, logger),
            CloudProviderType.OneDrive => CreateOneDrive(profile, logger),
            CloudProviderType.AmazonS3 => CreateS3(profile, "Amazon S3", logger),
            CloudProviderType.ArvanCloud => CreateArvanCloud(profile, logger),
            CloudProviderType.MinIO => CreateMinIO(profile, logger),
            CloudProviderType.CloudflareR2 => CreateCloudflareR2(profile, logger),
            CloudProviderType.Custom_S3 => CreateS3(profile, "S3 Storage", logger),
            _ => throw new NotSupportedException($"Provider type {profile.ProviderType} is not supported")
        };
    }

    private static ICloudProvider CreateGoogleDrive(CloudDriveProfile profile, ILogger logger)
    {
        var credPath = profile.GoogleCredentialsPath
            ?? Path.Combine(AppContext.BaseDirectory, "credentials.json");

        var auth = new GoogleAuthManager(credPath, "CloudDrive", logger);

        return new GoogleDriveProvider(
            async () => await auth.AuthenticateAsync(),
            logger);
    }

    private static ICloudProvider CreateOneDrive(CloudDriveProfile profile, ILogger logger)
    {
        var clientId = profile.OneDriveClientId
            ?? throw new InvalidOperationException("OneDriveClientId is required");

        return new OneDriveProvider(clientId, logger);
    }

    private static ICloudProvider CreateS3(CloudDriveProfile profile, string displayName, ILogger logger)
    {
        var s3Config = profile.S3Config
            ?? throw new InvalidOperationException("S3Config is required for S3 provider");

        if (string.IsNullOrEmpty(s3Config.ProviderDisplayName))
            s3Config.ProviderDisplayName = displayName;

        return new S3Provider(s3Config, logger);
    }

    private static ICloudProvider CreateArvanCloud(CloudDriveProfile profile, ILogger logger)
    {
        var s3Config = profile.S3Config
            ?? throw new InvalidOperationException("S3Config is required for ArvanCloud");

        s3Config.ServiceUrl = string.IsNullOrEmpty(s3Config.ServiceUrl)
            ? "https://s3.ir-thr-at1.arvanstorage.ir"
            : s3Config.ServiceUrl;
        s3Config.ProviderDisplayName = "ArvanCloud";
        s3Config.ForcePathStyle = true;

        return new S3Provider(s3Config, logger);
    }

    private static ICloudProvider CreateMinIO(CloudDriveProfile profile, ILogger logger)
    {
        var s3Config = profile.S3Config
            ?? throw new InvalidOperationException("S3Config is required for MinIO");

        if (string.IsNullOrEmpty(s3Config.ServiceUrl))
            throw new InvalidOperationException("ServiceUrl (e.g. http://localhost:9000) is required for MinIO");

        s3Config.ProviderDisplayName = "MinIO";
        s3Config.ForcePathStyle = true;

        return new S3Provider(s3Config, logger);
    }

    private static ICloudProvider CreateCloudflareR2(CloudDriveProfile profile, ILogger logger)
    {
        var s3Config = profile.S3Config
            ?? throw new InvalidOperationException("S3Config is required for Cloudflare R2");

        if (string.IsNullOrEmpty(s3Config.ServiceUrl))
            throw new InvalidOperationException("ServiceUrl (your R2 endpoint) is required for Cloudflare R2");

        s3Config.ProviderDisplayName = "Cloudflare R2";
        s3Config.ForcePathStyle = false;

        return new S3Provider(s3Config, logger);
    }
}
