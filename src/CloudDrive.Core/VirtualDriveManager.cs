using System.Runtime.InteropServices;
using callback.CBFSConnect;
using CloudDrive.Core.CloudProviders;
using CloudDrive.Core.Models;
using Serilog;

namespace CloudDrive.Core;

/// <summary>
/// مدیریت درایو مجازی CBFS Connect.
/// این کلاس پل ارتباطی بین سیستم‌عامل (از طریق CBFS) و سرویس ابری (از طریق ICloudProvider) است.
/// </summary>
public class VirtualDriveManager : IDisposable
{
    private readonly CBFS _cbfs;
    private readonly ICloudProvider _provider;
    private readonly DriveConfig _config;
    private readonly ILogger _logger;
    private bool _isMounted;

    // کش ساده برای نگهداری فایل‌های هر پوشه (folderId -> list of items)
    private readonly Dictionary<string, List<CloudFileItem>> _directoryCache = new();
    // نگاشت مسیر مجازی به شناسه فایل در کلود
    private readonly Dictionary<string, CloudFileItem> _pathToFileMap = new();

    public VirtualDriveManager(ICloudProvider provider, DriveConfig config, ILogger logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _cbfs = new CBFS();

        // تنظیم لایسنس
        _cbfs.RuntimeLicense = _config.CbfsLicenseKey;

        // ثبت رویدادهای ضروری
        RegisterEvents();

        _logger.Information("VirtualDriveManager initialized for {Provider}", _provider.ProviderName);
    }

    /// <summary>
    /// ثبت تمام رویدادهای CBFS
    /// </summary>
    private void RegisterEvents()
    {
        // --- رویدادهای Volume (درایو) ---
        _cbfs.OnMount += OnMount;
        _cbfs.OnUnmount += OnUnmount;
        _cbfs.OnGetVolumeSize += OnGetVolumeSize;
        _cbfs.OnGetVolumeLabel += OnGetVolumeLabel;
        _cbfs.OnSetVolumeLabel += OnSetVolumeLabel;

        // --- رویدادهای متادیتا ---
        _cbfs.OnGetFileInfo += OnGetFileInfo;
        _cbfs.OnEnumerateDirectory += OnEnumerateDirectory;
        _cbfs.OnCloseDirectoryEnumeration += OnCloseDirectoryEnumeration;

        // --- رویدادهای فایل ---
        _cbfs.OnOpenFile += OnOpenFile;
        _cbfs.OnCloseFile += OnCloseFile;
        _cbfs.OnReadFile += OnReadFile;
        _cbfs.OnWriteFile += OnWriteFile;
        _cbfs.OnFlushFile += OnFlushFile;

        // --- رویدادهای ساختاری ---
        _cbfs.OnCreateFile += OnCreateFile;
        _cbfs.OnDeleteFile += OnDeleteFile;
        _cbfs.OnRenameOrMoveFile += OnRenameOrMoveFile;
        _cbfs.OnCanFileBeDeleted += OnCanFileBeDeleted;

        // --- رویدادهای Attribute ---
        _cbfs.OnSetFileAttributes += OnSetFileAttributes;

        _logger.Debug("All CBFS events registered successfully");
    }

    #region Mount / Unmount

    /// <summary>
    /// نصب درایور CBFS در سیستم‌عامل (فقط یک بار نیاز است، نیاز به دسترسی Admin)
    /// </summary>
    public void InstallDriver(string cabFilePath)
    {
        _logger.Information("Installing CBFS driver from {CabPath}", cabFilePath);

        // Install(cabFileName, productGUID, pathToInstall, modulesToInstall, flags)
        // modulesToInstall: 1 = driver module
        // flags: 0x10 = remove old versions
        int reboot = _cbfs.Install(cabFilePath, "{713CC6CE-B3E2-4fd9-838D-E28F558F6866}", "", 1, 0x10);
        bool rebootRequired = reboot != 0;

        if (rebootRequired)
        {
            _logger.Warning("System reboot is required to complete driver installation!");
        }
        else
        {
            _logger.Information("CBFS driver installed successfully (no reboot needed)");
        }
    }

    /// <summary>
    /// ماونت کردن درایو مجازی
    /// </summary>
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
        // flags: 0x00000001 = STGMP_SIMPLE (drive letter mount point)
        _cbfs.AddMountingPoint(_config.DriveLetter, 0x00000001, 0);

        _isMounted = true;
        _logger.Information("✅ Drive {DriveLetter} mounted successfully as '{Label}'",
            _config.DriveLetter, _config.VolumeLabel);
    }

    /// <summary>
    /// آنماونت کردن درایو مجازی
    /// </summary>
    public async Task UnmountAsync()
    {
        if (!_isMounted)
        {
            _logger.Warning("Drive is not mounted");
            return;
        }

        _logger.Information("Unmounting drive {DriveLetter}...", _config.DriveLetter);

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

    #endregion

    #region Volume Events (رویدادهای درایو)

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
            // در فاز اول از مقادیر ثابت استفاده می‌کنیم
            // در فاز ۲ از _provider.GetStorageQuotaAsync() استفاده خواهیم کرد
            e.TotalSectors = 1024 * 1024 * 20;  // ~10 GB
            e.AvailableSectors = 1024 * 1024 * 10;  // ~5 GB free
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in OnGetVolumeSize");
        }
    }

    private void OnGetVolumeLabel(object sender, CBFSGetVolumeLabelEventArgs e)
    {
        e.Buffer = _config.VolumeLabel;
    }

    private void OnSetVolumeLabel(object sender, CBFSSetVolumeLabelEventArgs e)
    {
        // فعلاً تغییر نام درایو را نادیده می‌گیریم
    }

    #endregion

    #region Metadata Events (رویدادهای متادیتا)

    private void OnGetFileInfo(object sender, CBFSGetFileInfoEventArgs e)
    {
        var fileName = e.FileName;
        _logger.Debug("GetFileInfo: {FileName}", fileName);

        try
        {
            if (fileName == "\\" || fileName == "\\.")
            {
                // ریشه درایو
                e.FileExists = true;
                e.Attributes = (int)FileAttributes.Directory;
                e.CreationTime = DateTime.Now;
                e.LastAccessTime = DateTime.Now;
                e.LastWriteTime = DateTime.Now;
                e.Size = 0;
                return;
            }

            // جستجو در کش
            if (_pathToFileMap.TryGetValue(fileName, out var item))
            {
                e.FileExists = true;
                e.Attributes = item.IsDirectory
                    ? (int)FileAttributes.Directory
                    : (int)FileAttributes.Normal;
                e.CreationTime = item.CreatedTime;
                e.LastAccessTime = item.ModifiedTime;
                e.LastWriteTime = item.ModifiedTime;
                e.Size = item.Size;
                return;
            }

            // اگر در کش نبود، با API بررسی کن
            var fileInfo = _provider.GetFileInfoByPathAsync(fileName).GetAwaiter().GetResult();
            if (fileInfo != null)
            {
                _pathToFileMap[fileName] = fileInfo;
                e.FileExists = true;
                e.Attributes = fileInfo.IsDirectory
                    ? (int)FileAttributes.Directory
                    : (int)FileAttributes.Normal;
                e.CreationTime = fileInfo.CreatedTime;
                e.LastAccessTime = fileInfo.ModifiedTime;
                e.LastWriteTime = fileInfo.ModifiedTime;
                e.Size = fileInfo.Size;
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
        var mask = e.Mask;
        _logger.Debug("EnumerateDirectory: {DirName}, Mask: {Mask}", dirName, mask);

        try
        {
            // تعیین شناسه پوشه
            string folderId = "root";
            if (dirName != "\\" && dirName != "\\.")
            {
                if (_pathToFileMap.TryGetValue(dirName, out var dirItem) && dirItem.IsDirectory)
                {
                    folderId = dirItem.Id;
                }
            }

            // بارگذاری محتویات پوشه
            if (!_directoryCache.ContainsKey(folderId))
            {
                var files = _provider.ListFilesAsync(folderId).GetAwaiter().GetResult();
                _directoryCache[folderId] = files;

                // به‌روزرسانی نگاشت مسیرها
                foreach (var file in files)
                {
                    var path = dirName.TrimEnd('\\') + "\\" + file.Name;
                    file.VirtualPath = path;
                    _pathToFileMap[path] = file;
                }
            }

            var cachedFiles = _directoryCache[folderId];

            // استفاده از FileIndex به عنوان اندیس در لیست
            int index = (int)(e.FileFound ? e.HandleContext + 1 : 0);

            if (index < cachedFiles.Count)
            {
                var item = cachedFiles[index];
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
        // پاکسازی Context
        _logger.Debug("CloseDirectoryEnumeration");
    }

    #endregion

    #region File Events (رویدادهای فایل)

    private void OnOpenFile(object sender, CBFSOpenFileEventArgs e)
    {
        _logger.Debug("OpenFile: {FileName}", e.FileName);
        // در فاز ۱ فقط لاگ می‌کنیم
    }

    private void OnCloseFile(object sender, CBFSCloseFileEventArgs e)
    {
        _logger.Debug("CloseFile: {FileName}", e.FileName);
        // پاکسازی منابع
    }

    private void OnReadFile(object sender, CBFSReadFileEventArgs e)
    {
        var fileName = e.FileName;
        _logger.Debug("ReadFile: {FileName}, Offset: {Offset}, Count: {Count}", fileName, e.Position, e.BytesToRead);

        try
        {
            if (_pathToFileMap.TryGetValue(fileName, out var item))
            {
                var data = _provider.DownloadFileRangeAsync(item.Id, e.Position, (int)e.BytesToRead)
                    .GetAwaiter().GetResult();

                if (data != null && data.Length > 0)
                {
                    Marshal.Copy(data, 0, e.Buffer, data.Length);
                    e.BytesRead = (long)data.Length;
                }
                else
                {
                    e.BytesRead = 0;
                }
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
            // TODO: فاز ۴ - پیاده‌سازی نوشتن فایل
            _logger.Warning("WriteFile not implemented yet for {FileName}", fileName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in OnWriteFile for {FileName}", fileName);
        }
    }

    private void OnFlushFile(object sender, CBFSFlushFileEventArgs e)
    {
        _logger.Debug("FlushFile: {FileName}", e.FileName);
    }

    #endregion

    #region Structural Events (رویدادهای ساختاری)

    private void OnCreateFile(object sender, CBFSCreateFileEventArgs e)
    {
        _logger.Debug("CreateFile: {FileName}", e.FileName);
        // TODO: فاز ۴
    }

    private void OnDeleteFile(object sender, CBFSDeleteFileEventArgs e)
    {
        _logger.Debug("DeleteFile: {FileName}", e.FileName);
        // TODO: فاز ۴
    }

    private void OnRenameOrMoveFile(object sender, CBFSRenameOrMoveFileEventArgs e)
    {
        _logger.Debug("RenameOrMoveFile: {OldName} -> {NewName}", e.FileName, e.NewFileName);
        // TODO: فاز ۴
    }

    private void OnCanFileBeDeleted(object sender, CBFSCanFileBeDeletedEventArgs e)
    {
        e.CanBeDeleted = true; // فعلاً همه فایل‌ها قابل حذف هستند
    }

    private void OnSetFileAttributes(object sender, CBFSSetFileAttributesEventArgs e)
    {
        _logger.Debug("SetFileAttributes: {FileName}", e.FileName);
        // TODO: فاز ۴
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_isMounted)
        {
            UnmountAsync().GetAwaiter().GetResult();
        }
        _cbfs?.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}
