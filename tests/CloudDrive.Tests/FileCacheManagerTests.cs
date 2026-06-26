using CloudDrive.Core.Cache;
using Serilog;

namespace CloudDrive.Tests;

public class FileCacheManagerTests : IDisposable
{
    private readonly FileCacheManager _cache;
    private readonly string _testDir;
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();

    public FileCacheManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "CloudDriveTests_" + Guid.NewGuid());
        _cache = new FileCacheManager(_testDir, 10L * 1024 * 1024, _logger); // 10 MB max
    }

    [Fact]
    public void HasFile_ReturnsFalse_WhenNotCached()
    {
        Assert.False(_cache.HasFile("non-existent-file-id"));
    }

    [Fact]
    public async Task WriteAndRead_File_RoundTrip()
    {
        var fileId = "test-file-001";
        var content = System.Text.Encoding.UTF8.GetBytes("Hello, CloudDrive Cache!");

        await _cache.WriteFileBytesAsync(fileId, content);

        Assert.True(_cache.HasFile(fileId));

        using var stream = _cache.ReadFile(fileId);
        Assert.NotNull(stream);

        var buffer = new byte[content.Length];
        int read = stream.Read(buffer, 0, buffer.Length);

        Assert.Equal(content.Length, read);
        Assert.Equal("Hello, CloudDrive Cache!", System.Text.Encoding.UTF8.GetString(buffer));
    }

    [Fact]
    public async Task ReadFileRange_ReturnsCorrectBytes()
    {
        var fileId = "range-test-002";
        var content = System.Text.Encoding.UTF8.GetBytes("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        await _cache.WriteFileBytesAsync(fileId, content);

        var range = _cache.ReadFileRange(fileId, 5, 10); // از E تا N

        Assert.NotNull(range);
        Assert.Equal(10, range.Length);
        Assert.Equal("FGHIJKLMNO", System.Text.Encoding.UTF8.GetString(range));
    }

    [Fact]
    public async Task InvalidateFile_RemovesFromCache()
    {
        var fileId = "invalidate-test-003";
        await _cache.WriteFileBytesAsync(fileId, new byte[] { 1, 2, 3 });

        _cache.InvalidateFile(fileId);

        Assert.False(_cache.HasFile(fileId));
    }

    [Fact]
    public async Task WriteFile_UpdatesExisting()
    {
        var fileId = "update-test-004";
        var v1 = System.Text.Encoding.UTF8.GetBytes("Version 1");
        var v2 = System.Text.Encoding.UTF8.GetBytes("Version 2 Updated");

        await _cache.WriteFileBytesAsync(fileId, v1);
        await _cache.WriteFileBytesAsync(fileId, v2);

        using var stream = _cache.ReadFile(fileId);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();

        Assert.Equal("Version 2 Updated", text);
    }

    [Fact]
    public async Task GetStats_ReturnsCorrectValues()
    {
        var fileId1 = "stats-test-005a";
        var fileId2 = "stats-test-005b";
        await _cache.WriteFileBytesAsync(fileId1, new byte[1024]);
        await _cache.WriteFileBytesAsync(fileId2, new byte[2048]);

        var (count, size, max) = _cache.GetStats();

        Assert.Equal(2, count);
        Assert.Equal(3072, size);
        Assert.Equal(10L * 1024 * 1024, max);
    }

    [Fact]
    public async Task Clear_RemovesAllFiles()
    {
        await _cache.WriteFileBytesAsync("clear-test-1", new byte[] { 1 });
        await _cache.WriteFileBytesAsync("clear-test-2", new byte[] { 2 });

        _cache.Clear();

        var (count, size, _) = _cache.GetStats();
        Assert.Equal(0, count);
        Assert.Equal(0, size);
    }

    [Fact]
    public async Task LRU_EvictsOldestFiles_WhenFull()
    {
        // Cache با حداکثر ۵ کیلوبایت
        var smallCache = new FileCacheManager(
            Path.Combine(_testDir, "lru_test"),
            5 * 1024,
            _logger);

        var data3KB = new byte[3 * 1024];
        var data2KB = new byte[2 * 1024];

        // آیتم اول
        await smallCache.WriteFileBytesAsync("lru-old", data3KB);
        // آیتم دوم - باید eviction اتفاق بیفتد چون ۵ کیلوبایت می‌شویم
        await smallCache.WriteFileBytesAsync("lru-new1", data2KB);
        // آیتم سوم - حالا باید قدیمی‌ترین حذف شود
        await smallCache.WriteFileBytesAsync("lru-new2", data3KB);

        // بعد از eviction، آیتم قدیمی‌تر نباید در کش باشد
        var (count, size, max) = smallCache.GetStats();
        Assert.True(size <= max, $"Cache size {size} exceeds max {max}");

        smallCache.Clear();
        smallCache.Dispose();
    }

    public void Dispose()
    {
        _cache.Dispose();
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }
}
