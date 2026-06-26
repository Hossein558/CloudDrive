using CloudDrive.Core.Models;
using Serilog;

namespace CloudDrive.Core.Cache;

/// <summary>
/// کش متادیتای فایل‌ها با TTL (Time To Live).
/// از دریافت مجدد لیست فایل‌ها از API در مدت زمان کوتاه جلوگیری می‌کند.
/// Thread-safe است.
/// </summary>
public class MetadataCacheManager
{
    private readonly TimeSpan _ttl;
    private readonly ILogger _logger;

    // کش لیست فایل‌های هر پوشه: folderId -> (list, expiry)
    private readonly Dictionary<string, (List<CloudFileItem> Items, DateTime ExpiresAt)> _folderCache = new();

    // کش اطلاعات تک فایل: fileId -> (item, expiry)
    private readonly Dictionary<string, (CloudFileItem Item, DateTime ExpiresAt)> _fileCache = new();

    // نگاشت مسیر مجازی -> شناسه فایل
    private readonly Dictionary<string, string> _pathToIdMap = new();

    private readonly object _lock = new();

    public MetadataCacheManager(TimeSpan ttl, ILogger logger)
    {
        _ttl = ttl;
        _logger = logger;
    }

    // ========== Folder Cache ==========

    /// <summary>لیست محتویات پوشه را از کش بخوانید. null اگر کش نبود یا منقضی شده.</summary>
    public List<CloudFileItem>? GetFolderContents(string folderId)
    {
        lock (_lock)
        {
            if (_folderCache.TryGetValue(folderId, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
            {
                _logger.Debug("[Cache HIT] Folder: {FolderId}", folderId);
                return entry.Items;
            }
            _logger.Debug("[Cache MISS] Folder: {FolderId}", folderId);
            return null;
        }
    }

    /// <summary>لیست محتویات پوشه را در کش ذخیره کنید.</summary>
    public void SetFolderContents(string folderId, List<CloudFileItem> items, string virtualPath = "")
    {
        lock (_lock)
        {
            _folderCache[folderId] = (items, DateTime.UtcNow.Add(_ttl));

            // ثبت نگاشت مسیر -> id برای هر آیتم
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.VirtualPath))
                {
                    _pathToIdMap[item.VirtualPath.ToUpperInvariant()] = item.Id;
                    // کش تک فایل هم به‌روز کن
                    _fileCache[item.Id] = (item, DateTime.UtcNow.Add(_ttl));
                }
            }

            _logger.Debug("[Cache SET] Folder: {FolderId}, Count: {Count}", folderId, items.Count);
        }
    }

    /// <summary>پوشه مشخص را از کش حذف کن (بعد از تغییر محتوا).</summary>
    public void InvalidateFolder(string folderId)
    {
        lock (_lock)
        {
            _folderCache.Remove(folderId);
            _logger.Debug("[Cache INVALIDATE] Folder: {FolderId}", folderId);
        }
    }

    // ========== File Cache ==========

    /// <summary>اطلاعات یک فایل را از کش بخوانید.</summary>
    public CloudFileItem? GetFileInfo(string fileId)
    {
        lock (_lock)
        {
            if (_fileCache.TryGetValue(fileId, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
            {
                _logger.Debug("[Cache HIT] File: {FileId}", fileId);
                return entry.Item;
            }
            return null;
        }
    }

    /// <summary>اطلاعات یک فایل را در کش ذخیره کنید.</summary>
    public void SetFileInfo(string fileId, CloudFileItem item)
    {
        lock (_lock)
        {
            _fileCache[fileId] = (item, DateTime.UtcNow.Add(_ttl));
            if (!string.IsNullOrEmpty(item.VirtualPath))
            {
                _pathToIdMap[item.VirtualPath.ToUpperInvariant()] = fileId;
            }
        }
    }

    /// <summary>یک فایل را از کش حذف کن.</summary>
    public void InvalidateFile(string fileId)
    {
        lock (_lock)
        {
            if (_fileCache.TryGetValue(fileId, out var entry))
            {
                _pathToIdMap.Remove(entry.Item.VirtualPath.ToUpperInvariant());
            }
            _fileCache.Remove(fileId);
            _logger.Debug("[Cache INVALIDATE] File: {FileId}", fileId);
        }
    }

    // ========== Path -> ID Lookup ==========

    /// <summary>شناسه فایل را از مسیر مجازی پیدا کنید.</summary>
    public string? GetIdByPath(string virtualPath)
    {
        lock (_lock)
        {
            _pathToIdMap.TryGetValue(virtualPath.ToUpperInvariant(), out var id);
            return id;
        }
    }

    /// <summary>نگاشت مسیر به شناسه را ثبت کنید.</summary>
    public void RegisterPath(string virtualPath, string fileId)
    {
        lock (_lock)
        {
            _pathToIdMap[virtualPath.ToUpperInvariant()] = fileId;
        }
    }

    /// <summary>نگاشت مسیر را حذف کنید.</summary>
    public void UnregisterPath(string virtualPath)
    {
        lock (_lock)
        {
            _pathToIdMap.Remove(virtualPath.ToUpperInvariant());
        }
    }

    // ========== Cache Maintenance ==========

    /// <summary>تمام کش را پاک کنید.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _folderCache.Clear();
            _fileCache.Clear();
            _pathToIdMap.Clear();
            _logger.Information("[Cache] All metadata cache cleared");
        }
    }

    /// <summary>آیتم‌های منقضی‌شده را از کش حذف کنید.</summary>
    public void EvictExpired()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var expiredFolders = _folderCache.Where(kv => kv.Value.ExpiresAt <= now).Select(kv => kv.Key).ToList();
            var expiredFiles = _fileCache.Where(kv => kv.Value.ExpiresAt <= now).Select(kv => kv.Key).ToList();

            foreach (var key in expiredFolders) _folderCache.Remove(key);
            foreach (var key in expiredFiles) _fileCache.Remove(key);

            if (expiredFolders.Count + expiredFiles.Count > 0)
                _logger.Debug("[Cache EVICT] Removed {F} folders, {Fi} files", expiredFolders.Count, expiredFiles.Count);
        }
    }

    public int FolderCount { get { lock (_lock) return _folderCache.Count; } }
    public int FileCount { get { lock (_lock) return _fileCache.Count; } }
}
