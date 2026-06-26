using CloudDrive.Core.CloudProviders;
using CloudDrive.Core.Models;
using Serilog;

namespace CloudDrive.Core;

/// <summary>
/// مدیریت چند درایو ابری همزمان.
/// هر درایو یک حرف مجزا دارد (مثلاً G:=Google, O:=OneDrive, A:=ArvanCloud).
/// </summary>
public class MultiDriveManager : IDisposable
{
    private readonly List<(CloudDriveProfile Profile, VirtualDriveManager Manager)> _drives = new();
    private readonly ILogger _logger;
    private bool _disposed;

    public MultiDriveManager(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>اضافه کردن یک درایو ابری به مجموعه</summary>
    public void AddDrive(CloudDriveProfile profile, DriveConfig driveConfig)
    {
        if (!profile.Enabled)
        {
            _logger.Information("Skipping disabled drive: {Name}", profile.Name);
            return;
        }

        _logger.Information("Adding drive: {Name} ({Type}) -> {Letter}",
            profile.Name, profile.ProviderType, profile.DriveLetter);

        var provider = ProviderFactory.Create(profile, _logger);

        var config = new DriveConfig
        {
            DriveLetter = profile.DriveLetter,
            VolumeLabel = profile.Name,
            CbfsLicenseKey = driveConfig.CbfsLicenseKey,
            CachePath = Path.Combine(driveConfig.CachePath, SanitizeName(profile.Name)),
            MaxCacheSizeBytes = driveConfig.MaxCacheSizeBytes,
            MetadataCacheTtl = driveConfig.MetadataCacheTtl,
            GoogleCredentialsPath = profile.GoogleCredentialsPath ?? driveConfig.GoogleCredentialsPath,
            ApplicationName = driveConfig.ApplicationName
        };

        var manager = new VirtualDriveManager(provider, config, _logger);
        _drives.Add((profile, manager));
    }

    /// <summary>ماونت کردن تمام درایوها</summary>
    public async Task MountAllAsync()
    {
        _logger.Information("Mounting {Count} drive(s)...", _drives.Count);

        var tasks = _drives.Select(async d =>
        {
            try
            {
                await d.Manager.MountAsync();
                _logger.Information("✅ Mounted: {Letter} ({Name})", d.Profile.DriveLetter, d.Profile.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Failed to mount: {Letter} ({Name})", d.Profile.DriveLetter, d.Profile.Name);
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>آنماونت کردن تمام درایوها</summary>
    public async Task UnmountAllAsync()
    {
        _logger.Information("Unmounting {Count} drive(s)...", _drives.Count);

        foreach (var (profile, manager) in _drives)
        {
            try
            {
                await manager.UnmountAsync();
                _logger.Information("Unmounted: {Letter} ({Name})", profile.DriveLetter, profile.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to unmount: {Letter} ({Name})", profile.DriveLetter, profile.Name);
            }
        }
    }

    /// <summary>ماونت کردن یک درایو خاص</summary>
    public async Task MountDriveAsync(string driveLetter)
    {
        var drive = _drives.FirstOrDefault(d =>
            d.Profile.DriveLetter.Equals(driveLetter, StringComparison.OrdinalIgnoreCase));

        if (drive == default)
            throw new ArgumentException($"Drive {driveLetter} not found");

        await drive.Manager.MountAsync();
    }

    /// <summary>آنماونت کردن یک درایو خاص</summary>
    public async Task UnmountDriveAsync(string driveLetter)
    {
        var drive = _drives.FirstOrDefault(d =>
            d.Profile.DriveLetter.Equals(driveLetter, StringComparison.OrdinalIgnoreCase));

        if (drive == default)
            throw new ArgumentException($"Drive {driveLetter} not found");

        await drive.Manager.UnmountAsync();
    }

    /// <summary>نصب درایور CBFS (فقط یک بار لازم است)</summary>
    public void InstallDriver(string cabPath)
    {
        var first = _drives.FirstOrDefault();
        if (first == default)
            throw new InvalidOperationException("No drives configured");

        first.Manager.InstallDriver(cabPath);
    }

    /// <summary>لیست درایوهای ثبت‌شده</summary>
    public IReadOnlyList<CloudDriveProfile> GetProfiles()
        => _drives.Select(d => d.Profile).ToList().AsReadOnly();

    private static string SanitizeName(string name)
        => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var (_, manager) in _drives)
        {
            try { manager.Dispose(); } catch { }
        }
        _drives.Clear();

        GC.SuppressFinalize(this);
    }
}
