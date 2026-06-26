using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using CloudDrive.Core.Models;
using Serilog;

namespace CloudDrive.Core.CloudProviders;

/// <summary>
/// پیاده‌سازی ICloudProvider برای Google Drive.
/// از Google Drive API v3 استفاده می‌کند.
/// </summary>
public class GoogleDriveProvider : ICloudProvider
{
    private DriveService? _service;
    private readonly Func<Task<DriveService>> _serviceFactory;
    private readonly ILogger _logger;

    public string ProviderName => "Google Drive";
    public bool IsConnected => _service != null;

    /// <summary>
    /// سازنده با Factory برای ساخت DriveService
    /// </summary>
    public GoogleDriveProvider(Func<Task<DriveService>> serviceFactory, ILogger logger)
    {
        _serviceFactory = serviceFactory;
        _logger = logger;
    }

    public async Task ConnectAsync()
    {
        _logger.Information("Connecting to Google Drive...");
        _service = await _serviceFactory();
        _logger.Information("Connected to Google Drive successfully");
    }

    public Task DisconnectAsync()
    {
        _service?.Dispose();
        _service = null;
        _logger.Information("Disconnected from Google Drive");
        return Task.CompletedTask;
    }

    private DriveService Service => _service ?? throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");

    #region List & Metadata

    public async Task<List<CloudFileItem>> ListFilesAsync(string? folderId = null)
    {
        folderId ??= "root";
        _logger.Debug("Listing files in folder: {FolderId}", folderId);

        var request = Service.Files.List();
        request.Q = $"'{folderId}' in parents and trashed = false";
        request.Fields = "nextPageToken, files(id, name, mimeType, size, createdTime, modifiedTime, parents)";
        request.PageSize = 1000;
        request.OrderBy = "folder,name";

        var result = new List<CloudFileItem>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync();

            foreach (var file in response.Files)
            {
                result.Add(MapToCloudFileItem(file));
            }

            pageToken = response.NextPageToken;
        } while (pageToken != null);

        _logger.Debug("Found {Count} items in folder {FolderId}", result.Count, folderId);
        return result;
    }

    public async Task<CloudFileItem?> GetFileInfoAsync(string fileId)
    {
        try
        {
            var request = Service.Files.Get(fileId);
            request.Fields = "id, name, mimeType, size, createdTime, modifiedTime, parents";
            var file = await request.ExecuteAsync();
            return MapToCloudFileItem(file);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error getting file info for {FileId}", fileId);
            return null;
        }
    }

    public async Task<CloudFileItem?> GetFileInfoByPathAsync(string virtualPath)
    {
        // مسیر را به بخش‌ها تقسیم کن و قدم به قدم جستجو کن
        var parts = virtualPath.Trim('\\').Split('\\');
        string parentId = "root";

        for (int i = 0; i < parts.Length; i++)
        {
            var name = parts[i];
            var request = Service.Files.List();
            request.Q = $"'{parentId}' in parents and name = '{EscapeQuery(name)}' and trashed = false";
            request.Fields = "files(id, name, mimeType, size, createdTime, modifiedTime, parents)";
            request.PageSize = 1;

            var response = await request.ExecuteAsync();
            if (response.Files.Count == 0) return null;

            var file = response.Files[0];
            if (i == parts.Length - 1)
            {
                return MapToCloudFileItem(file);
            }

            parentId = file.Id;
        }

        return null;
    }

    public async Task<(long totalSpace, long usedSpace)> GetStorageQuotaAsync()
    {
        var about = Service.About.Get();
        about.Fields = "storageQuota";
        var result = await about.ExecuteAsync();

        long total = result.StorageQuota.Limit ?? 15L * 1024 * 1024 * 1024; // 15 GB default
        long used = result.StorageQuota.Usage ?? 0;

        _logger.Debug("Storage quota: {Used}/{Total} bytes", used, total);
        return (total, used);
    }

    #endregion

    #region Download

    public async Task<Stream> DownloadFileAsync(string fileId)
    {
        _logger.Debug("Downloading file: {FileId}", fileId);

        var request = Service.Files.Get(fileId);
        var stream = new MemoryStream();
        await request.DownloadAsync(stream);
        stream.Position = 0;
        return stream;
    }

    public async Task<byte[]> DownloadFileRangeAsync(string fileId, long offset, int count)
    {
        _logger.Debug("Downloading range: {FileId}, Offset: {Offset}, Count: {Count}", fileId, offset, count);

        // برای سادگی، کل فایل را دانلود کرده و بخش مورد نظر را برمی‌گردانیم
        // TODO: بهینه‌سازی با Range Request
        using var stream = await DownloadFileAsync(fileId);
        stream.Position = offset;

        var buffer = new byte[count];
        int bytesRead = await stream.ReadAsync(buffer, 0, count);

        if (bytesRead < count)
        {
            Array.Resize(ref buffer, bytesRead);
        }

        return buffer;
    }

    #endregion

    #region Upload

    public async Task<string> UploadFileAsync(string parentFolderId, string fileName, Stream content, string mimeType = "application/octet-stream")
    {
        _logger.Information("Uploading file: {FileName} to {ParentId}", fileName, parentFolderId);

        var fileMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileName,
            Parents = new List<string> { parentFolderId }
        };

        var request = Service.Files.Create(fileMetadata, content, mimeType);
        request.Fields = "id";
        var result = await request.UploadAsync();

        if (result.Status == Google.Apis.Upload.UploadStatus.Completed)
        {
            _logger.Information("File uploaded successfully: {FileId}", request.ResponseBody.Id);
            return request.ResponseBody.Id;
        }

        throw new Exception($"Upload failed: {result.Exception?.Message}");
    }

    public async Task UpdateFileContentAsync(string fileId, Stream content)
    {
        _logger.Information("Updating file content: {FileId}", fileId);

        var request = Service.Files.Update(new Google.Apis.Drive.v3.Data.File(), fileId, content, "application/octet-stream");
        await request.UploadAsync();
    }

    public async Task<string> CreateFolderAsync(string parentFolderId, string folderName)
    {
        _logger.Information("Creating folder: {FolderName} in {ParentId}", folderName, parentFolderId);

        var fileMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = folderName,
            MimeType = CloudFileItem.FolderMimeType,
            Parents = new List<string> { parentFolderId }
        };

        var request = Service.Files.Create(fileMetadata);
        request.Fields = "id";
        var folder = await request.ExecuteAsync();

        _logger.Information("Folder created: {FolderId}", folder.Id);
        return folder.Id;
    }

    #endregion

    #region Delete / Rename / Move

    public async Task DeleteFileAsync(string fileId, bool permanent = false)
    {
        _logger.Information("Deleting file: {FileId}, Permanent: {Permanent}", fileId, permanent);

        if (permanent)
        {
            await Service.Files.Delete(fileId).ExecuteAsync();
        }
        else
        {
            // انتقال به سطل زباله
            var update = new Google.Apis.Drive.v3.Data.File { Trashed = true };
            await Service.Files.Update(update, fileId).ExecuteAsync();
        }
    }

    public async Task RenameFileAsync(string fileId, string newName)
    {
        _logger.Information("Renaming file: {FileId} to {NewName}", fileId, newName);

        var update = new Google.Apis.Drive.v3.Data.File { Name = newName };
        await Service.Files.Update(update, fileId).ExecuteAsync();
    }

    public async Task MoveFileAsync(string fileId, string newParentId, string? oldParentId = null)
    {
        _logger.Information("Moving file: {FileId} to {NewParentId}", fileId, newParentId);

        var request = Service.Files.Update(new Google.Apis.Drive.v3.Data.File(), fileId);
        request.AddParents = newParentId;
        if (oldParentId != null)
        {
            request.RemoveParents = oldParentId;
        }
        await request.ExecuteAsync();
    }

    #endregion

    #region Helpers

    private static CloudFileItem MapToCloudFileItem(Google.Apis.Drive.v3.Data.File file)
    {
        return new CloudFileItem
        {
            Id = file.Id,
            Name = file.Name,
            ParentId = file.Parents?.FirstOrDefault() ?? "root",
            Size = file.Size ?? 0,
            IsDirectory = file.MimeType == CloudFileItem.FolderMimeType,
            CreatedTime = file.CreatedTimeDateTimeOffset?.DateTime ?? DateTime.Now,
            ModifiedTime = file.ModifiedTimeDateTimeOffset?.DateTime ?? DateTime.Now,
            MimeType = file.MimeType ?? "application/octet-stream"
        };
    }

    private static string EscapeQuery(string value)
    {
        return value.Replace("'", "\\'");
    }

    #endregion
}
