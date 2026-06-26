using Serilog;

namespace CloudDrive.Core.Cache;

/// <summary>
/// مدیریت کش فایل‌های دانلودشده روی دیسک محلی.
/// از دانلود مجدد فایل‌هایی که قبلاً دانلود شده‌اند جلوگیری می‌کند.
/// پیاده‌سازی LRU (Least Recently Used) برای محدود کردن حجم کش.
/// </summary>
public class FileCacheManager : IDisposable
{
    private readonly string _cacheDirectory;
    private readonly long _maxCacheSizeBytes;
    private readonly ILogger _logger;

    // نگاشت fileId -> (مسیر فایل کش، زمان آخرین دسترسی، اندازه)
    private readonly Dictionary<string, (string Path, DateTime LastAccessed, long Size)> _cacheIndex = new();
    private readonly object _lock = new();
    private long _currentSizeBytes;

    public FileCacheManager(string cacheDirectory, long maxCacheSizeBytes, ILogger logger)
    {
        _cacheDirectory = cacheDirectory;
        _maxCacheSizeBytes = maxCacheSizeBytes;
        _logger = logger;

        Directory.CreateDirectory(_cacheDirectory);
        LoadExistingCacheIndex();
    }

    // ========== Public API ==========

    /// <summary>آیا فایل در کش موجود است؟</summary>
    public bool HasFile(string fileId)
    {
        lock (_lock)
        {
            return _cacheIndex.TryGetValue(fileId, out var entry) && File.Exists(entry.Path);
        }
    }

    /// <summary>بخوانید فایل از کش. null اگر وجود نداشت.</summary>
    public Stream? ReadFile(string fileId)
    {
        lock (_lock)
        {
            if (!_cacheIndex.TryGetValue(fileId, out var entry) || !File.Exists(entry.Path))
                return null;

            // به‌روزرسانی LRU time
            _cacheIndex[fileId] = (entry.Path, DateTime.UtcNow, entry.Size);
            _logger.Debug("[FileCache HIT] {FileId}", fileId);
        }

        // Stream رو خارج از lock باز کن
        try
        {
            return new FileStream(_cacheIndex[fileId].Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "[FileCache] Failed to open cached file for {FileId}", fileId);
            return null;
        }
    }

    /// <summary>خواندن محدوده‌ای از فایل کش‌شده.</summary>
    public byte[]? ReadFileRange(string fileId, long offset, int count)
    {
        using var stream = ReadFile(fileId);
        if (stream == null) return null;

        stream.Position = offset;
        var buffer = new byte[count];
        int bytesRead = stream.Read(buffer, 0, count);

        if (bytesRead < count)
            Array.Resize(ref buffer, bytesRead);

        return buffer;
    }

    /// <summary>محتوای یک فایل را در کش ذخیره کنید.</summary>
    public async Task WriteFileAsync(string fileId, Stream content)
    {
        var cachePath = GetCachePath(fileId);

        try
        {
            // اگر حجم کش پر است، اول جا باز کن
            await using (var fs = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(fs);
            }

            var fileInfo = new FileInfo(cachePath);
            var fileSize = fileInfo.Length;

            lock (_lock)
            {
                // اگر قبلاً وجود داشت، حجم قدیمی را کم کن
                if (_cacheIndex.TryGetValue(fileId, out var oldEntry))
                    _currentSizeBytes -= oldEntry.Size;

                _cacheIndex[fileId] = (cachePath, DateTime.UtcNow, fileSize);
                _currentSizeBytes += fileSize;
            }

            _logger.Debug("[FileCache WRITE] {FileId}, Size: {Size} bytes", fileId, fileSize);

            // LRU Eviction
            await EvictIfNeededAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[FileCache] Failed to write cache for {FileId}", fileId);
            // پاکسازی فایل ناقص
            try { File.Delete(cachePath); } catch { }
        }
    }

    /// <summary>محتوای یک فایل را از byte[] در کش ذخیره کنید.</summary>
    public async Task WriteFileBytesAsync(string fileId, byte[] data)
    {
        using var ms = new MemoryStream(data);
        await WriteFileAsync(fileId, ms);
    }

    /// <summary>فایل را از کش حذف کنید.</summary>
    public void InvalidateFile(string fileId)
    {
        lock (_lock)
        {
            if (_cacheIndex.TryGetValue(fileId, out var entry))
            {
                try { File.Delete(entry.Path); } catch { }
                _currentSizeBytes -= entry.Size;
                _cacheIndex.Remove(fileId);
                _logger.Debug("[FileCache INVALIDATE] {FileId}", fileId);
            }
        }
    }

    /// <summary>کل کش را پاک کنید.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var entry in _cacheIndex.Values)
            {
                try { File.Delete(entry.Path); } catch { }
            }
            _cacheIndex.Clear();
            _currentSizeBytes = 0;
            _logger.Information("[FileCache] All file cache cleared");
        }
    }

    /// <summary>اطلاعات آماری کش.</summary>
    public (int FileCount, long SizeBytes, long MaxSizeBytes) GetStats()
    {
        lock (_lock)
        {
            return (_cacheIndex.Count, _currentSizeBytes, _maxCacheSizeBytes);
        }
    }

    // ========== Private Helpers ==========

    private string GetCachePath(string fileId)
    {
        // از fileId به عنوان نام فایل استفاده می‌کنیم، کاراکترهای غیرمجاز را جایگزین می‌کنیم
        var safeId = string.Concat(fileId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_cacheDirectory, safeId + ".cache");
    }

    private async Task EvictIfNeededAsync()
    {
        List<(string FileId, string Path, long Size)> toEvict = new();

        lock (_lock)
        {
            if (_currentSizeBytes <= _maxCacheSizeBytes) return;

            // LRU: آیتم‌هایی که دیرتر استفاده شده‌اند اول حذف می‌شوند
            var sorted = _cacheIndex
                .OrderBy(kv => kv.Value.LastAccessed)
                .ToList();

            long sizeToFree = _currentSizeBytes - (_maxCacheSizeBytes * 8 / 10); // تا ۸۰٪ کم کن
            long freed = 0;

            foreach (var kv in sorted)
            {
                if (freed >= sizeToFree) break;
                toEvict.Add((kv.Key, kv.Value.Path, kv.Value.Size));
                freed += kv.Value.Size;
            }
        }

        foreach (var (fileId, path, size) in toEvict)
        {
            try
            {
                await Task.Run(() => File.Delete(path));
                lock (_lock)
                {
                    _cacheIndex.Remove(fileId);
                    _currentSizeBytes -= size;
                }
                _logger.Debug("[FileCache EVICT] {FileId}, freed {Size} bytes", fileId, size);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "[FileCache] Could not evict {FileId}", fileId);
            }
        }
    }

    /// <summary>بارگذاری ایندکس کش از فایل‌های موجود در پوشه کش (بعد از restart برنامه).</summary>
    private void LoadExistingCacheIndex()
    {
        try
        {
            var files = Directory.GetFiles(_cacheDirectory, "*.cache");
            foreach (var file in files)
            {
                var fileId = Path.GetFileNameWithoutExtension(file);
                var info = new FileInfo(file);
                _cacheIndex[fileId] = (file, info.LastAccessTime.ToUniversalTime(), info.Length);
                _currentSizeBytes += info.Length;
            }
            _logger.Debug("[FileCache] Loaded {Count} cached files ({Size} bytes)", files.Length, _currentSizeBytes);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "[FileCache] Could not load existing cache index");
        }
    }

    public void Dispose()
    {
        // هیچ منبعی نیاز به آزادسازی خاص ندارد
    }
}
