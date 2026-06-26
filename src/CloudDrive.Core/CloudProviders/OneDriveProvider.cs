using Microsoft.Graph;
using Microsoft.Graph.Models;
using Azure.Identity;
using CloudDrive.Core.Models;
using Serilog;

namespace CloudDrive.Core.CloudProviders;

/// <summary>
/// پیاده‌سازی ICloudProvider برای Microsoft OneDrive.
/// از Microsoft Graph API v5 استفاده می‌کند.
/// احراز هویت: Interactive Browser (OAuth 2.0 PKCE).
/// </summary>
public class OneDriveProvider : ICloudProvider
{
    private GraphServiceClient? _client;
    private string? _driveId;
    private readonly string _clientId;
    private readonly ILogger _logger;

    public string ProviderName => "OneDrive";
    public bool IsConnected => _client != null;

    private static readonly string[] Scopes =
    {
        "Files.ReadWrite.All",
        "offline_access"
    };

    public OneDriveProvider(string clientId, ILogger logger)
    {
        _clientId = clientId;
        _logger = logger;
    }

    public async Task ConnectAsync()
    {
        _logger.Information("Connecting to OneDrive via Microsoft Graph...");

        var credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            ClientId = _clientId,
            TenantId = "consumers",
            RedirectUri = new Uri("http://localhost")
        });

        _client = new GraphServiceClient(credential, Scopes);

        // گرفتن driveId که در تمام عملیات‌ها نیاز است
        var drive = await _client.Me.Drive.GetAsync();
        _driveId = drive?.Id ?? throw new Exception("Could not get OneDrive ID");

        _logger.Information("Connected to OneDrive: driveId={Id}", _driveId);
    }

    public Task DisconnectAsync()
    {
        _client = null;
        _driveId = null;
        _logger.Information("Disconnected from OneDrive");
        return Task.CompletedTask;
    }

    private GraphServiceClient Client => _client ??
        throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");

    private string DriveId => _driveId ??
        throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");

    // ========== List & Metadata ==========

    public async Task<List<CloudFileItem>> ListFilesAsync(string? folderId = null)
    {
        folderId ??= "root";
        _logger.Debug("OneDrive: Listing folder {FolderId}", folderId);

        DriveItemCollectionResponse? response = null;

        if (folderId == "root")
        {
            // ریشه: ابتدا Root item را بگیر، بعد children آن را list کن
            var rootItem = await Client.Drives[DriveId].Root.GetAsync();
            if (rootItem?.Id != null)
            {
                response = await Client.Drives[DriveId].Items[rootItem.Id].Children.GetAsync(req =>
                    req.QueryParameters.Top = 1000);
            }
        }
        else
        {
            response = await Client.Drives[DriveId].Items[folderId].Children.GetAsync(req =>
                req.QueryParameters.Top = 1000);
        }

        var result = new List<CloudFileItem>();
        foreach (var item in response?.Value ?? new List<DriveItem>())
            result.Add(MapToCloudFileItem(item));

        _logger.Debug("OneDrive: Found {Count} items in {FolderId}", result.Count, folderId);
        return result;
    }

    public async Task<CloudFileItem?> GetFileInfoAsync(string fileId)
    {
        try
        {
            var item = await Client.Drives[DriveId].Items[fileId].GetAsync();
            return item == null ? null : MapToCloudFileItem(item);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OneDrive: GetFileInfo failed for {FileId}", fileId);
            return null;
        }
    }

    public async Task<CloudFileItem?> GetFileInfoByPathAsync(string virtualPath)
    {
        try
        {
            var graphPath = virtualPath.Replace('\\', '/').TrimStart('/');
            var item = await Client.Drives[DriveId].Root.ItemWithPath(graphPath).GetAsync();
            return item == null ? null : MapToCloudFileItem(item);
        }
        catch (Exception ex)
        {
            _logger.Debug("OneDrive: Path not found {Path}: {Msg}", virtualPath, ex.Message);
            return null;
        }
    }

    public async Task<(long totalSpace, long usedSpace)> GetStorageQuotaAsync()
    {
        var drive = await Client.Drives[DriveId].GetAsync();
        long total = drive?.Quota?.Total ?? 5L * 1024 * 1024 * 1024;
        long used = drive?.Quota?.Used ?? 0;
        return (total, used);
    }

    // ========== Download ==========

    public async Task<Stream> DownloadFileAsync(string fileId)
    {
        _logger.Debug("OneDrive: Downloading {FileId}", fileId);
        var stream = await Client.Drives[DriveId].Items[fileId].Content.GetAsync();
        return stream ?? Stream.Null;
    }

    public async Task<byte[]> DownloadFileRangeAsync(string fileId, long offset, int count)
    {
        using var stream = await DownloadFileAsync(fileId);
        stream.Position = offset;
        var buffer = new byte[count];
        int bytesRead = await stream.ReadAsync(buffer, 0, count);
        if (bytesRead < count) Array.Resize(ref buffer, bytesRead);
        return buffer;
    }

    // ========== Upload ==========

    public async Task<string> UploadFileAsync(string parentFolderId, string fileName, Stream content,
        string mimeType = "application/octet-stream")
    {
        _logger.Information("OneDrive: Uploading {FileName} to {Parent}", fileName, parentFolderId);

        string effectiveParentId = parentFolderId;
        if (parentFolderId == "root")
        {
            var rootItem = await Client.Drives[DriveId].Root.GetAsync();
            effectiveParentId = rootItem?.Id ?? "root";
        }

        var result = await Client.Drives[DriveId].Items[effectiveParentId]
            .ItemWithPath(fileName).Content.PutAsync(content);

        return result?.Id ?? throw new Exception("Upload failed");
    }

    public async Task UpdateFileContentAsync(string fileId, Stream content)
    {
        await Client.Drives[DriveId].Items[fileId].Content.PutAsync(content);
    }

    public async Task<string> CreateFolderAsync(string parentFolderId, string folderName)
    {
        string effectiveParentId = parentFolderId;
        if (parentFolderId == "root")
        {
            var rootItem = await Client.Drives[DriveId].Root.GetAsync();
            effectiveParentId = rootItem?.Id ?? throw new Exception("Could not get root ID");
        }

        var newFolder = new DriveItem
        {
            Name = folderName,
            Folder = new Folder(),
            AdditionalData = new Dictionary<string, object>
            {
                { "@microsoft.graph.conflictBehavior", "rename" }
            }
        };

        var result = await Client.Drives[DriveId].Items[effectiveParentId].Children.PostAsync(newFolder);
        return result?.Id ?? throw new Exception("Folder creation failed");
    }

    // ========== Delete / Rename / Move ==========

    public async Task DeleteFileAsync(string fileId, bool permanent = false)
    {
        _logger.Information("OneDrive: Deleting {FileId}", fileId);
        await Client.Drives[DriveId].Items[fileId].DeleteAsync();
    }

    public async Task RenameFileAsync(string fileId, string newName)
    {
        _logger.Information("OneDrive: Renaming {FileId} to {Name}", fileId, newName);
        await Client.Drives[DriveId].Items[fileId].PatchAsync(new DriveItem { Name = newName });
    }

    public async Task MoveFileAsync(string fileId, string newParentId, string? oldParentId = null)
    {
        _logger.Information("OneDrive: Moving {FileId} to {NewParent}", fileId, newParentId);

        string effectiveParentId = newParentId;
        if (newParentId == "root")
        {
            var rootItem = await Client.Drives[DriveId].Root.GetAsync();
            effectiveParentId = rootItem?.Id ?? throw new Exception("Could not get root ID");
        }

        await Client.Drives[DriveId].Items[fileId].PatchAsync(new DriveItem
        {
            ParentReference = new ItemReference
            {
                DriveId = DriveId,
                Id = effectiveParentId
            }
        });
    }

    // ========== Helpers ==========

    private static CloudFileItem MapToCloudFileItem(DriveItem item)
    {
        return new CloudFileItem
        {
            Id = item.Id ?? string.Empty,
            Name = item.Name ?? string.Empty,
            ParentId = item.ParentReference?.Id ?? "root",
            Size = item.Size ?? 0,
            IsDirectory = item.Folder != null,
            CreatedTime = item.CreatedDateTime?.DateTime ?? DateTime.Now,
            ModifiedTime = item.LastModifiedDateTime?.DateTime ?? DateTime.Now,
            MimeType = item.File?.MimeType ?? (item.Folder != null ? CloudFileItem.FolderMimeType : "application/octet-stream")
        };
    }
}
