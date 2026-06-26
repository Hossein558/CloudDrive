using CloudDrive.Core.Cache;
using CloudDrive.Core.Models;
using Serilog;

namespace CloudDrive.Tests;

public class MetadataCacheManagerTests
{
    private readonly MetadataCacheManager _cache;
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();

    public MetadataCacheManagerTests()
    {
        _cache = new MetadataCacheManager(TimeSpan.FromMinutes(5), _logger);
    }

    [Fact]
    public void GetFolderContents_ReturnsNull_WhenNotCached()
    {
        var result = _cache.GetFolderContents("non-existent-folder");
        Assert.Null(result);
    }

    [Fact]
    public void SetAndGet_FolderContents_ReturnsCorrectItems()
    {
        var folderId = "folder123";
        var items = new List<CloudFileItem>
        {
            new CloudFileItem { Id = "f1", Name = "test.txt", VirtualPath = "\\test.txt" },
            new CloudFileItem { Id = "f2", Name = "docs", IsDirectory = true, VirtualPath = "\\docs" }
        };

        _cache.SetFolderContents(folderId, items);
        var result = _cache.GetFolderContents(folderId);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("test.txt", result[0].Name);
    }

    [Fact]
    public void InvalidateFolder_RemovesFromCache()
    {
        var folderId = "folder-to-invalidate";
        var items = new List<CloudFileItem>
        {
            new CloudFileItem { Id = "x1", Name = "file.doc", VirtualPath = "\\file.doc" }
        };
        _cache.SetFolderContents(folderId, items);

        _cache.InvalidateFolder(folderId);
        var result = _cache.GetFolderContents(folderId);

        Assert.Null(result);
    }

    [Fact]
    public void GetFolderContents_ReturnsNull_AfterTtlExpires()
    {
        var shortCache = new MetadataCacheManager(TimeSpan.FromMilliseconds(50), _logger);
        var items = new List<CloudFileItem>
        {
            new CloudFileItem { Id = "x", Name = "test", VirtualPath = "\\test" }
        };
        shortCache.SetFolderContents("folder1", items);

        Thread.Sleep(100); // منتظر انقضا

        var result = shortCache.GetFolderContents("folder1");
        Assert.Null(result);
    }

    [Fact]
    public void RegisterPath_And_GetIdByPath_Works()
    {
        _cache.RegisterPath("\\Documents\\report.pdf", "file-id-abc");

        var id = _cache.GetIdByPath("\\Documents\\report.pdf");
        Assert.Equal("file-id-abc", id);
    }

    [Fact]
    public void GetIdByPath_IsCaseInsensitive()
    {
        _cache.RegisterPath("\\Docs\\File.TXT", "file-id-xyz");

        var id = _cache.GetIdByPath("\\docs\\file.txt");
        Assert.Equal("file-id-xyz", id);
    }

    [Fact]
    public void UnregisterPath_RemovesMapping()
    {
        _cache.RegisterPath("\\temp\\file.tmp", "tmp-id");
        _cache.UnregisterPath("\\temp\\file.tmp");

        var id = _cache.GetIdByPath("\\temp\\file.tmp");
        Assert.Null(id);
    }

    [Fact]
    public void SetFileInfo_AutoRegistersPath()
    {
        var item = new CloudFileItem
        {
            Id = "auto-reg-id",
            Name = "auto.txt",
            VirtualPath = "\\auto.txt"
        };
        _cache.SetFileInfo(item.Id, item);

        var id = _cache.GetIdByPath("\\auto.txt");
        Assert.Equal("auto-reg-id", id);
    }

    [Fact]
    public void Clear_EmptiesAllCaches()
    {
        _cache.RegisterPath("\\a.txt", "id1");
        _cache.SetFolderContents("folder1", new List<CloudFileItem>
        {
            new CloudFileItem { Id = "id1", Name = "a.txt", VirtualPath = "\\a.txt" }
        });

        _cache.Clear();

        Assert.Equal(0, _cache.FolderCount);
        Assert.Equal(0, _cache.FileCount);
        Assert.Null(_cache.GetIdByPath("\\a.txt"));
    }

    [Fact]
    public void EvictExpired_RemovesExpiredEntries()
    {
        var shortCache = new MetadataCacheManager(TimeSpan.FromMilliseconds(50), _logger);
        shortCache.SetFolderContents("old-folder", new List<CloudFileItem>
        {
            new CloudFileItem { Id = "old", Name = "old.txt", VirtualPath = "\\old.txt" }
        });

        // اضافه کردن آیتم با TTL بلند
        var longCache = new MetadataCacheManager(TimeSpan.FromHours(1), _logger);
        longCache.SetFolderContents("new-folder", new List<CloudFileItem>
        {
            new CloudFileItem { Id = "new", Name = "new.txt", VirtualPath = "\\new.txt" }
        });

        Thread.Sleep(100);
        shortCache.EvictExpired();

        Assert.Equal(0, shortCache.FolderCount);
    }
}
