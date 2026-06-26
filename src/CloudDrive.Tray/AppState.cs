using CloudDrive.Core;
using CloudDrive.Core.Models;
using Serilog;

namespace CloudDrive.Tray;

/// <summary>
/// وضعیت کلی برنامه — shared state بین TrayApp و SettingsForm.
/// </summary>
public class AppState
{
    private DriveStatus _currentStatus = DriveStatus.Disconnected;
    private readonly ILogger _logger;

    public DriveConfig Config { get; private set; }
    public VirtualDriveManager? DriveManager { get; set; }

    public DriveStatus CurrentStatus => _currentStatus;
    public bool IsMounted => _currentStatus == DriveStatus.Mounted;
    public bool IsMounting => _currentStatus == DriveStatus.Mounting;

    /// <summary>رویداد برای اطلاع‌رسانی تغییر وضعیت به UI</summary>
    public event Action<DriveStatus>? StatusChanged;

    public AppState(DriveConfig config, ILogger logger)
    {
        Config = config;
        _logger = logger;
    }

    public void SetStatus(DriveStatus status)
    {
        _currentStatus = status;
        _logger.Debug("AppState: status changed to {Status}", status);
        StatusChanged?.Invoke(status);
    }

    public void UpdateConfig(DriveConfig newConfig)
    {
        Config = newConfig;
    }
}

/// <summary>
/// وضعیت‌های ممکن درایو
/// </summary>
public enum DriveStatus
{
    Disconnected,
    Mounting,
    Mounted,
    Unmounting,
    Error
}
