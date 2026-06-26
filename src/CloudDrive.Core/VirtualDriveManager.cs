using System.Runtime.InteropServices;
using callback.CBFSConnect;
using CloudDrive.Core.Cache;
using CloudDrive.Core.CloudProviders;
using CloudDrive.Core.Models;
using Serilog;

namespace CloudDrive.Core;

/// <summary>
/// مدیریت درایو مجازی CBFS Connect.
/// این کلاس پل ارتباطی بین سیستم‌عامل (از طریق CBFS) و سرویس ابری (از طریق ICloudProvider) است.
/// فازهای ۱ تا ۴: خواندن، نوشتن، ساخت، حذف و تغییر نام فایل‌ها.
/// </summary>
public class VirtualDriveManager : IDisposable
{
    private readonly CBFS _cbfs;
    private readonly ICloudProvider _provider;
    private readonly DriveConfig _config;
    private readonly ILogger _logger;
    private readonly MetadataCacheManager _metadataCache;
    private readonly FileCacheManager _fileCache;
    private bool _isMounted;

    // Context برای فایل‌های باز: fileHandle -> (fileId, isWriting, localBuffer)
    private readonly Dictionary<long, OpenFileContext> _openFiles = new();
    private readonly object _openFilesLock = new();
    private long _nextHandle = 1;

    public VirtualDriveManager(ICloudProvider provider, DriveConfig config, ILogger logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _metadataCache = new MetadataCacheManager(config.MetadataCacheTtl, logger);
        _fileCache = new FileCacheManager(config.CachePath, config.MaxCacheSizeBytes, logger);

        _cbfs = new CBFS();
        _cbfs.RuntimeLicense = _config.CbfsLicenseKey;

        RegisterEvents();
        _logger.Information("VirtualDriveManager initialized for {Provider}", _provider.ProviderName);
    }

    // ========== Mount / Unmount ==========

    /// <summary>نصب درایور CBFS در سیستم‌عامل (فقط یک بار، نیاز به Admin)</summary>
    public void InstallDriver(string cabFilePath)
    {
        _logger.Information("Installing CBFS driver from {CabPath}", cabFilePath);
        int reboot = _cbfs.Install(cabFilePath, "{713CC6CE-B3E2-4fd9-838D-E28F558F6866}", "", 1, 0x10);
        if (reboot != 0)
            _logger.Warning("System reboot is required to complete driver installation!");
        else
            _logger.Information("CBFS driver installed successfully (no reboot needed)");
    }

    /// <summary>ماونت کردن درایو مجازی</summary>
    public async Task MountAsync()
    {
        if (_isMounted)
        {
            _logger.Warning("Drive is already mounted");
            return;
        }

        _logger.Information("Connecting to {Provider}...", _provider.ProviderName);
        await _provider.ConnectAsync();

        _logger.Information("Creating virtual storage...");
        _cbfs.CreateStorage();

        _logger.Information("Mounting media as {DriveLetter}...", _config.DriveLetter);
        _cbfs.MountMedia(0);
        _cbfs.AddMountingPoint(_config.DriveLetter, 0x00000001, 0);

        _isMounted = true;
        _logger.Information("✅ Drive {DriveLetter} mounted successfully as '{Label}'",
            _config.DriveLetter, _config.VolumeLabel);
    }

    /// <summary>آنماونت کردن درایو مجازی</summary>
    public async Task UnmountAsync()
    {
        if (!_isMounted)
        {
            _logger.Warning("Drive is not mounted");
            return;
        }

        _logger.Information("Unmounting drive {DriveLetter}...", _config.DriveLetter);

        // Flush فایل‌های باز
        await FlushAllOpenFilesAsync();

        try
        {
            _cbfs.RemoveMountingPoint(-1, _config.DriveLetter, 0, 0);
            _cbfs.UnmountMedia(true);
            _cbfs.DeleteStorage(true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during unmount");
        }

        await _provider.DisconnectAsync();
        _isMounted = false;
        _logger.Information("Drive unmounted successfully");
    }

    // ========== Event Registration ==========

    private void RegisterEvents()
    {
        _cbfs.OnMount += OnMount;
        _cbfs.OnUnmount += OnUnmount;
        _cbfs.OnGetVolumeSize += OnGetVolumeSize;
        _cbfs.OnGetVolumeLabel += OnGetVolumeLabel;
        _cbfs.OnSetVolumeLabel += OnSetVolumeLabel;

        _cbfs.OnGetFileInfo += OnGetFileInfo;
        _cbfs.OnEnumerateDirectory += OnEnumerateDirectory;
        _cbfs.OnCloseDirectoryEnumeration += OnCloseDirectoryEnumeration;

        _cbfs.OnOpenFile += OnOpenFile;
        _cbfs.OnCloseFile += OnCloseFile;
        _cbfs.OnReadFile += OnReadFile;
        _cbfs.OnWriteFile += OnWriteFile;
        _cbfs.OnFlushFile += OnFlushFile;

        _cbfs.OnCreateFile += OnCreateFile;
        _cbfs.OnDeleteFile += OnDeleteFile;
        _cbfs.OnRenameOrMoveFile += OnRenameOrMoveFile;
        _cbfs.OnCanFileBeDeleted += OnCanFileBeDeleted;
        _cbfs.OnSetFileAttributes += OnSetFileAttributes;

        _logger.Debug("All CBFS events registered successfully");
    }

    // ========== Volume Events ==========

    private void OnMount(object sender, CBFSMountEventArgs e)
    {
        _logger.Information("Drive mounted event fired");
    }

    private void OnUnmount(object sender, CBFSUnmountEventArgs e)
    {
        _logger.Information("Drive unmount event fired");
    }

    private void OnGetVolumeSize(object sender, CBFSGetVolumeSizeEventArgs e)
    {
        try
        {
            // سعی می‌کنیم فضای واقعی را از Provider بخوانیم
            var quota = _provider.GetStorageQuotaAsync().GetAwaiter().GetResult();
            long sectorSize = 512;
            e.TotalSectors = quota.totalSpace / sectorSize;
            e.AvailableSectors = (quota.totalSpace - quota.usedSpace) / sectorSize;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not get real quota, using defaults");
            e.TotalSectors = 1024L * 1024 * 30;   // ~15 GB
            e.AvailableSectors = 1024L * 1024 * 20; // ~10 GB
        }
    }

    private void OnGetVolumeLabel(object sender, CBFSGetVolumeLabelEventArgs e)
    {
        e.Buffer = _config.VolumeLabel;
    }

    private void OnSetVolumeLabel(object sender, CBFSSetVolumeLabelEventArgs e)
    {
        // تغییر نام درایو را نادیده می‌گیریم
    }

    // ========== Metadata Events ==========

    private void OnGetFileInfo(object sender, CBFSGetFileInfoEventArgs e)
    {
        var fileName = e.FileName;
        _logger.Debug("GetFileInfo: {FileName}", fileName);

        try
        {
            // ریشه درایو
            if (IsRoot(fileName))
            {
                SetRootInfo(e);
                return;
            }

            // جستجو در کش متادیتا
            var cachedId = _metadataCache.GetIdByPath(fileName);
            if (cachedId != null)
            {
                var cachedItem = _metadataCache.GetFileInfo(cachedId);
                if (cachedItem != null)
                {
                    FillFileInfoFromItem(e, cachedItem);
                    return;
                }
            }

            // جستجو از API
            var fileInfo = _provider.GetFileInfoByPathAsync(fileName).GetAwaiter().GetResult();
            if (fileInfo != null)
            {
                fileInfo.VirtualPath = fileName;
                _metadataCache.SetFileInfo(fileInfo.Id, fileInfo);
                _metadataCache.RegisterPath(fileName, fileInfo.Id);
                FillFileInfoFromItem(e, fileInfo);
            }
            else
            {
                e.FileExists = false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in OnGetFileInfo for {FileName}", fileName);
            e.FileExists = false;
        }
    }

    private void OnEnumerateDirectory(object sender, CBFSEnumerateDirectoryEventArgs e)
    {
        var dirName = e.DirectoryName;
        _logger.Debug("EnumerateDirectory: {DirName}", dirName);

        try
        {
            // تعیین شناسه پوشه
            string folderId = "root";
            if (!IsRoot(dirName))
            {
                var id = _metadataCache.GetIdByPath(dirName);
                if (id != null) folderId = id;
                else
                {
                    // جستجو از API
                    var dirInfo = _provider.GetFileInfoByPathAsync(dirName).GetAwaiter().GetResult();
                    if (dirInfo != null) folderId = dirInfo.Id;
                }
            }

            // بارگذاری محتویات از کش یا API
            var files = _metadataCache.GetFolderContents(folderId);
            if (files == null)
            {
                files = _provider.ListFilesAsync(folderId).GetAwaiter().GetResult();

                // ثبت مسیر مجازی هر آیتم
                var basePath = IsRoot(dirName) ? "" : dirName.TrimEnd('\\');
                foreach (var file in files)
                {
                    file.VirtualPath = basePath + "\\" + file.Name;
                }

                _metadataCache.SetFolderContents(folderId, files);
            }

            // EnumerateDirectory با استفاده از HandleContext به عنوان index فراخوانی می‌شود
            int index = e.FileFound ? (int)(e.HandleContext) + 1 : 0;

            if (index < files.Count)
            {
                var item = files[index];
                e.FileFound = true;
                e.FileName = item.Name;
                e.Attributes = item.IsDirectory
                    ? (int)FileAttributes.Directory
                    : (int)FileAttributes.Normal;
                e.CreationTime = item.CreatedTime;
                e.LastAccessTime = item.ModifiedTime;
                e.LastWriteTime = item.ModifiedTime;
                e.Size = item.Size;
                e.HandleContext = index;
            }
            else
            {
                e.FileFound = false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in OnEnumerateDirectory for {DirName}", dirName);
            e.FileFound = false;
        }
    }

    private void OnCloseDirectoryEnumeration(object sender, CBFSCloseDirectoryEnumerationEventArgs e)
    {
        _logger.Debug("CloseDirectoryEnumeration: {DirName}", e.DirectoryName);
    }

    // ========== File Events ==========

    private void OnOpenFile(object sender, CBFSOpenFileEventArgs e)
    {
        var fileName = e.FileName;
        _logger.Debug("OpenFile: {FileName}, DesiredAccess: {Access}", fileName, e.DesiredAccess);

        try
        {
            // پیدا کردن ID فایل
            var fileId = _metadataCache.GetIdByPath(fileName);
            if (fileId == null)
            {
                var info = _provider.GetFileInfoByPathAsync(fileName).GetAwaiter().GetResult();
                if (info != null)
                {
                    fileId = info.Id;
                    info.VirtualPath = fileName;
                    _metadataCache.SetFileInfo(fileId, info);
                    _metadataCache.RegisterPath(fileName, fileId);
                }
            }

            bool isWriteAccess = (e.DesiredAccess & 0x40000000) != 0 || // GENERIC_WRITE
                                  (e.DesiredAccess & 0x00000002) != 0;   // FILE_WRITE_DATA

            long handle;
            lock (_openFilesLock)
            {
                handle = _nextHandle++;
                _openFiles[handle] = new OpenFileContext
                {
                    FileId = fileId ?? string.Empty,
                    VirtualPath = fileName,
                    IsWriteMode = isWriteAccess,
                    WriteBuffer = isWriteAccess ? new MemoryStream() : null
                };
            }

            e.FileContext = (nint)handle;
            _logger.Debug("Opened file {FileName} with handle {Handle}, Write={IsWrite}", fileName, handle, isWriteAccess);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in OnOpenFile for {FileName}", fileName);
        }
    }

    private void OnCloseFile(object sender, CBFSCloseFileEventArgs e)
    {
        var fileName = e.FileName;
        _logger.Debug("CloseFile: {FileName}", fileName);

        OpenFileContext? ctx = null;
        lock (_openFilesLock)
        {
            if (_openFiles.TryGetValue(e.FileContext, out ctx))
                _openFiles.Remove(e.FileContext);
        }

        if (ctx?.WriteBuffer != null && ctx.WriteBuffer.Length > 0)
        {
            // آپلود اطلاعات نوشته‌شده
            try
            {
                ctx.WriteBuffer.Position = 0;
                if (!string.IsNullOrEmpty(ctx.FileId))
                {
                    _provider.UpdateFileContentAsync(ctx.FileId, ctx.WriteBuffer).GetAwaiter().GetResult();
                    _fileCache.InvalidateFile(ctx.FileId);
                    _metadataCache.InvalidateFile(ctx.FileId);
                    _logger.Information("Flushed write for {FileName} ({Bytes} bytes)", fileName, ctx.WriteBuffer.Length);
                }
                ctx.WriteBuffer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error flushing write for {FileName}", fileName);
            }
        }
    }

    private void OnReadFile(object sender, CBFSReadFileEventArgs e)
    {
        var fileName = e.FileName;
        _logger.Debug("ReadFile: {FileName}, Offset: {Offset}, Count: {Count}", fileName, e.Position, e.BytesToRead);

        try
        {
            OpenFileContext? ctx = null;
            lock (_openFilesLock)
            {
                _openFiles.TryGetValue(e.FileContext, out ctx);
            }

            var fileId = ctx?.FileId ?? _metadataCache.GetIdByPath(fileName);
            if (string.IsNullOrEmpty(fileId))
            {
                e.BytesRead = 0;
                return;
            }

            // اگر در write buffer هست، از آنجا بخوان
            if (ctx?.WriteBuffer != null)
            {
                ctx.WriteBuffer.Position = e.Position;
                var tmpBuf = new byte[e.BytesToRead];
                int read = ctx.WriteBuffer.Read(tmpBuf, 0, (int)e.BytesToRead);
                if (read > 0)
                {
                    Marshal.Copy(tmpBuf, 0, e.Buffer, read);
                    e.BytesRead = read;
                    return;
                }
            }

            // بررسی FileCache روی دیسک
            if (_fileCache.HasFile(fileId))
            {
                var data = _fileCache.ReadFileRange(fileId, e.Position, (int)e.BytesToRead);
                if (data != null && data.Length > 0)
                {
                    Marshal.Copy(data, 0, e.Buffer, data.Length);
                    e.BytesRead = data.Length;
                    return;
                }
            }

            // دانلود از Cloud و کش کردن
            _logger.Debug("Cache miss, downloading from cloud: {FileId}", fileId);
            var fullContent = _provider.DownloadFileAsync(fileId).GetAwaiter().GetResult();
            _fileCache.WriteFileAsync(fileId, fullContent).GetAwaiter().GetResult();

            // حالا از کش بخوان
            var rangeData = _fileCache.ReadFileRange(fileId, e.Position, (int)e.BytesToRead);
            if (rangeData != null && rangeData.Length > 0)
            {
                Marshal.Copy(rangeData, 0, e.Buffer, rangeData.Length);
                e.BytesRead = rangeData.Length;
            }
            else
            {
                e.BytesRead = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in OnReadFile for {FileName}", fileName);
            e.BytesRead = 0;
        }
    }

    private void OnWriteFile(object sender, CBFSWriteFileEventArgs e)
    {
        var fileName = e.FileName;
        _logger.Debug("WriteFile: {FileName}, Offset: {Offset}, Count: {Count}", fileName, e.Position, e.BytesToWrite);

        try
        {
            OpenFileContext? ctx = null;
            lock (_openFilesLock)
            {
                _openFiles.TryGetValue(e.FileContext, out ctx);
            }

            if (ctx?.WriteBuffer == null)
            {
                _logger.Warning("WriteFile called but no write context for {FileName}", fileName);
                e.BytesWritten = 0;
                return;
            }

            // کپی داده‌ها از CBFS buffer به MemoryStream
            var data = new byte[e.BytesToWrite];
            Marshal.Copy(e.Buffer, data, 0, (int)e.BytesToWrite);

            ctx.WriteBuffer.Position = e.Position;
            ctx.WriteBuffer.Write(data, 0, data.Length);

            e.BytesWritten = (long)data.Length;

            // اندازه فایل را به‌روز کن
            if (ctx.WriteBuffer.Length > ctx.FileSize)
                ctx.FileSize = ctx.WriteBuffer.Length;

            _logger.Debug("Buffered {Count} bytes for {FileName}", data.Length, fileName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in OnWriteFile for {FileName}", fileName);
            e.BytesWritten = 0;
        }
    }

    private void OnFlushFile(object sender, CBFSFlushFileEventArgs e)
    {
        _logger.Debug("FlushFile: {FileName}", e.FileName);
        // آپلود فقط در CloseFile انجام می‌شود تا از آپلودهای مکرر جلوگیری شود
    }

    // ========== Structural Events (فاز ۴) ==========

    private void OnCreateFile(object sender, CBFSCreateFileEventArgs e)
    {
        var fileName = e.FileName;
        _logger.Information("CreateFile: {FileName}, IsDir: {IsDir}", fileName, ((int)e.Attributes & (int)FileAttributes.Directory) != 0);

        try
        {
            bool isDirectory = ((int)e.Attributes & (int)FileAttributes.Directory) != 0;

            // پیدا کردن شناسه پوشه والد
            var parentPath = Path.GetDirectoryName(fileName) ?? "\\";
            var itemName = Path.GetFileName(fileName);

            string parentId = "root";
            if (!IsRoot(parentPath))
            {
                var pid = _metadataCache.GetIdByPath(parentPath);
                if (pid != null) parentId = pid;
                else
                {
                    var pInfo = _provider.GetFileInfoByPathAsync(parentPath).GetAwaiter().GetResult();
                    if (pInfo != null) parentId = pInfo.Id;
                }
            }

            string newId;
            if (isDirectory)
            {
                newId = _provider.CreateFolderAsync(parentId, itemName).GetAwaiter().GetResult();
            }
            else
            {
                // فایل خالی می‌سازیم، محتوا در WriteFile/CloseFile آپلود می‌شود
                using var emptyStream = new MemoryStream();
                newId = _provider.UploadFileAsync(parentId, itemName, emptyStream).GetAwaiter().GetResult();
            }

            // ثبت در کش
            var newItem = new CloudFileItem
            {
                Id = newId,
                Name = itemName,
                ParentId = parentId,
                IsDirectory = isDirectory,
                VirtualPath = fileName,
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now,
                Size = 0
            };
            _metadataCache.SetFileInfo(newId, newItem);
            _metadataCache.RegisterPath(fileName, newId);
            _metadataCache.InvalidateFolder(parentId); // کش پوشه والد را باطل کن

            // Context فایل جدید
            long handle;
            lock (_openFilesLock)
            {
                handle = _nextHandle++;
                _openFiles[handle] = new OpenFileContext
                {
                    FileId = newId,
                    VirtualPath = fileName,
                    IsWriteMode = true,
                    WriteBuffer = isDirectory ? null : new MemoryStream()
                };
            }
            e.FileContext = (nint)handle;

            _logger.Information("Created {Type} '{Name}' with ID {Id}", isDirectory ? "folder" : "file", itemName, newId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in OnCreateFile for {FileName}", fileName);
        }
    }

    private void OnDeleteFile(object sender, CBFSDeleteFileEventArgs e)
    {
        var fileName = e.FileName;
        _logger.Information("DeleteFile: {FileName}", fileName);

        try
        {
            var fileId = _metadataCache.GetIdByPath(fileName);
            if (fileId == null)
            {
                var info = _provider.GetFileInfoByPathAsync(fileName).GetAwaiter().GetResult();
                if (info == null)
                {
                    _logger.Warning("DeleteFile: file not found {FileName}", fileName);
                    return;
                }
                fileId = info.Id;
            }

            // انتقال به Trash (نه حذف دائمی)
            _provider.DeleteFileAsync(fileId, permanent: false).GetAwaiter().GetResult();

            // به‌روزرسانی کش
            var parentPath = Path.GetDirectoryName(fileName) ?? "\\";
            string parentId = _metadataCache.GetIdByPath(parentPath) ?? "root";
            _metadataCache.InvalidateFolder(parentId);
            _metadataCache.InvalidateFile(fileId);
            _metadataCache.UnregisterPath(fileName);
            _fileCache.InvalidateFile(fileId);

            _logger.Information("Deleted (trashed) {FileName}", fileName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in OnDeleteFile for {FileName}", fileName);
        }
    }

    private void OnRenameOrMoveFile(object sender, CBFSRenameOrMoveFileEventArgs e)
    {
        var oldName = e.FileName;
        var newName = e.NewFileName;
        _logger.Information("RenameOrMove: {OldName} -> {NewName}", oldName, newName);

        try
        {
            var fileId = _metadataCache.GetIdByPath(oldName);
            if (fileId == null)
            {
                var info = _provider.GetFileInfoByPathAsync(oldName).GetAwaiter().GetResult();
                if (info == null)
                {
                    _logger.Warning("RenameOrMove: source not found {OldName}", oldName);
                    return;
                }
                fileId = info.Id;
            }

            var oldParentPath = Path.GetDirectoryName(oldName) ?? "\\";
            var newParentPath = Path.GetDirectoryName(newName) ?? "\\";
            var newFileName = Path.GetFileName(newName);

            bool isMove = !string.Equals(oldParentPath, newParentPath, StringComparison.OrdinalIgnoreCase);
            bool isRename = !string.Equals(Path.GetFileName(oldName), newFileName, StringComparison.OrdinalIgnoreCase);

            if (isRename)
                _provider.RenameFileAsync(fileId, newFileName).GetAwaiter().GetResult();

            if (isMove)
            {
                var oldParentId = _metadataCache.GetIdByPath(oldParentPath) ?? "root";
                var newParentId = _metadataCache.GetIdByPath(newParentPath) ?? "root";

                if (newParentId == "root" && !IsRoot(newParentPath))
                {
                    var pInfo = _provider.GetFileInfoByPathAsync(newParentPath).GetAwaiter().GetResult();
                    if (pInfo != null) newParentId = pInfo.Id;
                }

                _provider.MoveFileAsync(fileId, newParentId, oldParentId).GetAwaiter().GetResult();

                // کش پوشه‌های والد را باطل کن
                _metadataCache.InvalidateFolder(oldParentId);
                _metadataCache.InvalidateFolder(newParentId);
            }
            else
            {
                // فقط rename، کش پوشه والد را باطل کن
                var parentId = _metadataCache.GetIdByPath(oldParentPath) ?? "root";
                _metadataCache.InvalidateFolder(parentId);
            }

            // به‌روزرسانی نگاشت مسیر
            _metadataCache.UnregisterPath(oldName);
            _metadataCache.RegisterPath(newName, fileId);
            _metadataCache.InvalidateFile(fileId);

            _logger.Information("RenameOrMove complete: {Old} -> {New}", oldName, newName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in OnRenameOrMoveFile: {Old} -> {New}", oldName, newName);
        }
    }

    private void OnCanFileBeDeleted(object sender, CBFSCanFileBeDeletedEventArgs e)
    {
        // همه فایل‌ها قابل حذف هستند (فقط به Trash منتقل می‌شوند)
        e.CanBeDeleted = true;
    }

    private void OnSetFileAttributes(object sender, CBFSSetFileAttributesEventArgs e)
    {
        _logger.Debug("SetFileAttributes: {FileName}", e.FileName);
        // گوگل درایو مفهوم NTFS attributes ندارد - نادیده می‌گیریم
    }

    // ========== Private Helpers ==========

    private static bool IsRoot(string path)
        => path == "\\" || path == "\\." || path == "/" || string.IsNullOrEmpty(path);

    private static void SetRootInfo(CBFSGetFileInfoEventArgs e)
    {
        e.FileExists = true;
        e.Attributes = (int)FileAttributes.Directory;
        e.CreationTime = DateTime.Now;
        e.LastAccessTime = DateTime.Now;
        e.LastWriteTime = DateTime.Now;
        e.Size = 0;
    }

    private static void FillFileInfoFromItem(CBFSGetFileInfoEventArgs e, CloudFileItem item)
    {
        e.FileExists = true;
        e.Attributes = item.IsDirectory
            ? (int)FileAttributes.Directory
            : (int)FileAttributes.Normal;
        e.CreationTime = item.CreatedTime;
        e.LastAccessTime = item.ModifiedTime;
        e.LastWriteTime = item.ModifiedTime;
        e.Size = item.Size;
    }

    private async Task FlushAllOpenFilesAsync()
    {
        List<OpenFileContext> ctxList;
        lock (_openFilesLock)
        {
            ctxList = _openFiles.Values.ToList();
        }

        foreach (var ctx in ctxList)
        {
            if (ctx.WriteBuffer != null && ctx.WriteBuffer.Length > 0)
            {
                try
                {
                    ctx.WriteBuffer.Position = 0;
                    if (!string.IsNullOrEmpty(ctx.FileId))
                    {
                        await _provider.UpdateFileContentAsync(ctx.FileId, ctx.WriteBuffer);
                        _fileCache.InvalidateFile(ctx.FileId);
                        _logger.Information("Flushed pending write for {Path}", ctx.VirtualPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error flushing {Path} on unmount", ctx.VirtualPath);
                }
            }
        }
    }

    // ========== IDisposable ==========

    public void Dispose()
    {
        if (_isMounted)
            UnmountAsync().GetAwaiter().GetResult();

        _fileCache?.Dispose();
        _cbfs?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Context برای فایل‌های باز
/// </summary>
internal class OpenFileContext
{
    public string FileId { get; set; } = string.Empty;
    public string VirtualPath { get; set; } = string.Empty;
    public bool IsWriteMode { get; set; }
    public MemoryStream? WriteBuffer { get; set; }
    public long FileSize { get; set; }
}
