using Amazon.S3;
using Amazon.S3.Model;
using CloudDrive.Core.Models;
using Serilog;

namespace CloudDrive.Core.CloudProviders;

/// <summary>
/// پیاده‌سازی ICloudProvider برای Amazon S3 و هر سرویس S3-Compatible.
/// از AWSSDK.S3 استفاده می‌کند.
/// 
/// سرویس‌های پشتیبانی‌شده:
///   - Amazon S3 (ServiceURL = null)
///   - ArvanCloud Object Storage (ServiceURL = "https://s3.ir-thr-at1.arvanstorage.ir")
///   - MinIO (ServiceURL = "http://your-minio:9000")
///   - Cloudflare R2 (ServiceURL = "https://accountid.r2.cloudflarestorage.com")
///   - Wasabi, Backblaze B2, ...
/// </summary>
public class S3Provider : ICloudProvider
{
    private AmazonS3Client? _s3;
    private readonly S3Config _config;
    private readonly ILogger _logger;

    public string ProviderName => _config.ProviderDisplayName;
    public bool IsConnected => _s3 != null;

    public S3Provider(S3Config config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task ConnectAsync()
    {
        _logger.Information("Connecting to S3-compatible storage: {Provider}", _config.ProviderDisplayName);

        var s3Config = new AmazonS3Config
        {
            ForcePathStyle = _config.ForcePathStyle,
        };

        if (!string.IsNullOrEmpty(_config.ServiceUrl))
            s3Config.ServiceURL = _config.ServiceUrl;
        else
            s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_config.Region);

        _s3 = new AmazonS3Client(_config.AccessKey, _config.SecretKey, s3Config);
        _logger.Information("S3 client created for {Provider}", _config.ProviderDisplayName);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _s3?.Dispose();
        _s3 = null;
        _logger.Information("Disconnected from {Provider}", _config.ProviderDisplayName);
        return Task.CompletedTask;
    }

    private AmazonS3Client S3 => _s3 ??
        throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");

    // ========== List & Metadata ==========

    /// <summary>
    /// folderId در S3 = یک پیشوند (prefix). 
    /// "root" = ریشه bucket.
    /// "Documents/" = پوشه Documents.
    /// </summary>
    public async Task<List<CloudFileItem>> ListFilesAsync(string? folderId = null)
    {
        var prefix = folderId == null || folderId == "root" ? "" : EnsureTrailingSlash(folderId);
        _logger.Debug("S3: Listing prefix '{Prefix}' in bucket {Bucket}", prefix, _config.BucketName);

        var request = new ListObjectsV2Request
        {
            BucketName = _config.BucketName,
            Prefix = prefix,
            Delimiter = "/",   // شبیه‌سازی پوشه‌ها
            MaxKeys = 1000
        };

        var result = new List<CloudFileItem>();
        ListObjectsV2Response response;

        do
        {
            response = await S3.ListObjectsV2Async(request);

            // پوشه‌های مجازی (Common Prefixes)
            foreach (var cp in response.CommonPrefixes)
            {
                var folderName = cp.TrimEnd('/').Split('/').Last();
                result.Add(new CloudFileItem
                {
                    Id = cp,
                    Name = folderName,
                    ParentId = prefix == "" ? "root" : prefix,
                    IsDirectory = true,
                    CreatedTime = DateTime.Now,
                    ModifiedTime = DateTime.Now,
                    MimeType = CloudFileItem.FolderMimeType,
                    Size = 0
                });
            }

            // فایل‌ها
            foreach (var obj in response.S3Objects)
            {
                // از پوشه مجازی خودش (trailing slash) صرف‌نظر کن
                if (obj.Key == prefix) continue;

                var fileName = obj.Key.Substring(prefix.Length).TrimEnd('/');
                if (string.IsNullOrEmpty(fileName)) continue;

                result.Add(new CloudFileItem
                {
                    Id = obj.Key,
                    Name = fileName,
                    ParentId = prefix == "" ? "root" : prefix,
                    IsDirectory = false,
                    Size = obj.Size,
                    ModifiedTime = obj.LastModified,
                    CreatedTime = obj.LastModified,
                    MimeType = "application/octet-stream"
                });
            }

            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);

        _logger.Debug("S3: Found {Count} items in prefix '{Prefix}'", result.Count, prefix);
        return result;
    }

    public async Task<CloudFileItem?> GetFileInfoAsync(string fileId)
    {
        try
        {
            var meta = await S3.GetObjectMetadataAsync(_config.BucketName, fileId);
            return new CloudFileItem
            {
                Id = fileId,
                Name = fileId.Split('/').Last(),
                ParentId = GetParentPrefix(fileId),
                Size = meta.ContentLength,
                IsDirectory = false,
                ModifiedTime = meta.LastModified,
                CreatedTime = meta.LastModified,
                MimeType = meta.Headers.ContentType ?? "application/octet-stream"
            };
        }
        catch (Exception ex)
        {
            _logger.Debug("S3: GetFileInfo not found for {Key}: {Msg}", fileId, ex.Message);
            return null;
        }
    }

    public async Task<CloudFileItem?> GetFileInfoByPathAsync(string virtualPath)
    {
        // مسیر مجازی را به S3 key تبدیل کن
        var key = virtualPath.Trim('\\').Replace('\\', '/');
        return await GetFileInfoAsync(key);
    }

    public async Task<(long totalSpace, long usedSpace)> GetStorageQuotaAsync()
    {
        // S3 حجم کلی ندارد، مجموع اشیاء را محاسبه می‌کنیم
        try
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _config.BucketName,
                MaxKeys = 1000
            };
            long totalSize = 0;
            ListObjectsV2Response response;
            do
            {
                response = await S3.ListObjectsV2Async(request);
                totalSize += response.S3Objects.Sum(o => o.Size);
                request.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated == true);

            long quota = _config.StorageQuotaBytes > 0 ? _config.StorageQuotaBytes : 100L * 1024 * 1024 * 1024;
            return (quota, totalSize);
        }
        catch
        {
            return (100L * 1024 * 1024 * 1024, 0);
        }
    }

    // ========== Download ==========

    public async Task<Stream> DownloadFileAsync(string fileId)
    {
        _logger.Debug("S3: Downloading {Key}", fileId);
        var response = await S3.GetObjectAsync(_config.BucketName, fileId);
        return response.ResponseStream;
    }

    public async Task<byte[]> DownloadFileRangeAsync(string fileId, long offset, int count)
    {
        _logger.Debug("S3: Range download {Key} [{Offset}+{Count}]", fileId, offset, count);

        var request = new GetObjectRequest
        {
            BucketName = _config.BucketName,
            Key = fileId,
            ByteRange = new ByteRange(offset, offset + count - 1)
        };

        using var response = await S3.GetObjectAsync(request);
        using var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms);
        return ms.ToArray();
    }

    // ========== Upload ==========

    public async Task<string> UploadFileAsync(string parentFolderId, string fileName, Stream content,
        string mimeType = "application/octet-stream")
    {
        var prefix = parentFolderId == "root" ? "" : EnsureTrailingSlash(parentFolderId);
        var key = prefix + fileName;

        _logger.Information("S3: Uploading {Key} to bucket {Bucket}", key, _config.BucketName);

        var request = new PutObjectRequest
        {
            BucketName = _config.BucketName,
            Key = key,
            InputStream = content,
            ContentType = mimeType
        };
        await S3.PutObjectAsync(request);

        _logger.Information("S3: Uploaded {Key}", key);
        return key;
    }

    public async Task UpdateFileContentAsync(string fileId, Stream content)
    {
        _logger.Information("S3: Updating {Key}", fileId);
        await S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _config.BucketName,
            Key = fileId,
            InputStream = content
        });
    }

    public async Task<string> CreateFolderAsync(string parentFolderId, string folderName)
    {
        var prefix = parentFolderId == "root" ? "" : EnsureTrailingSlash(parentFolderId);
        var folderKey = prefix + folderName + "/";

        _logger.Information("S3: Creating virtual folder {Key}", folderKey);

        // در S3 پوشه واقعی نیست — یک شیء خالی با trailing slash می‌سازیم
        await S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _config.BucketName,
            Key = folderKey,
            ContentBody = ""
        });

        return folderKey;
    }

    // ========== Delete / Rename / Move ==========

    public async Task DeleteFileAsync(string fileId, bool permanent = false)
    {
        _logger.Information("S3: Deleting {Key}", fileId);

        // اگر پوشه است، محتویاتش را هم حذف کن
        if (fileId.EndsWith("/"))
        {
            var contents = await ListFilesAsync(fileId);
            foreach (var item in contents)
                await DeleteFileAsync(item.Id, permanent);
        }

        await S3.DeleteObjectAsync(_config.BucketName, fileId);
    }

    public async Task RenameFileAsync(string fileId, string newName)
    {
        var parentPrefix = GetParentPrefix(fileId);
        var newKey = (parentPrefix == "root" ? "" : EnsureTrailingSlash(parentPrefix)) + newName;
        await MoveFileAsync(fileId, parentPrefix, null);
        // اگر key عوض شود، باید copy + delete کنیم
        await S3.CopyObjectAsync(_config.BucketName, fileId, _config.BucketName, newKey);
        await S3.DeleteObjectAsync(_config.BucketName, fileId);
    }

    public async Task MoveFileAsync(string fileId, string newParentId, string? oldParentId = null)
    {
        var newPrefix = newParentId == "root" ? "" : EnsureTrailingSlash(newParentId);
        var fileName = fileId.Split('/').Last();
        var newKey = newPrefix + fileName;

        _logger.Information("S3: Moving {Old} -> {New}", fileId, newKey);

        await S3.CopyObjectAsync(_config.BucketName, fileId, _config.BucketName, newKey);
        await S3.DeleteObjectAsync(_config.BucketName, fileId);
    }

    // ========== Helpers ==========

    private static string EnsureTrailingSlash(string s) =>
        s.EndsWith('/') ? s : s + "/";

    private static string GetParentPrefix(string key)
    {
        var parts = key.TrimEnd('/').Split('/');
        if (parts.Length <= 1) return "root";
        return string.Join("/", parts[..^1]) + "/";
    }
}

/// <summary>
/// تنظیمات اتصال S3.
/// </summary>
public class S3Config
{
    /// <summary>نام نمایشی (مثلاً "ArvanCloud" یا "Amazon S3")</summary>
    public string ProviderDisplayName { get; set; } = "S3 Storage";

    /// <summary>Access Key</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Secret Key</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>نام Bucket</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>Region (برای AWS استفاده می‌شود، مثلاً "us-east-1")</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// آدرس سرویس برای S3-Compatible (خالی برای AWS S3 اصلی).
    /// ArvanCloud: "https://s3.ir-thr-at1.arvanstorage.ir"
    /// MinIO: "http://localhost:9000"
    /// Cloudflare R2: "https://ACCOUNT_ID.r2.cloudflarestorage.com"
    /// </summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Force Path Style (لازم برای MinIO و بعضی S3-compatible ها)</summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>حجم کل فضای ذخیره‌سازی (برای نمایش در درایو). 0 = پیش‌فرض 100GB</summary>
    public long StorageQuotaBytes { get; set; } = 0;
}
