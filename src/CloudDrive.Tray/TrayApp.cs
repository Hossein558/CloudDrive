using CloudDrive.Core.Models;
using CloudDrive.Tray.Forms;
using Serilog;
using Microsoft.Win32;

namespace CloudDrive.Tray;

/// <summary>
/// مدیریت آیکون System Tray و منوی کانتکست.
/// این کلاس قلب رابط کاربری برنامه است.
/// </summary>
public class TrayApp : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayMenuBuilder _menuBuilder;
    private readonly AppState _state;
    private readonly ILogger _logger;

    // رنگ‌های آیکون
    private Icon? _iconConnected;
    private Icon? _iconDisconnected;
    private Icon? _iconSyncing;

    public TrayApp(AppState state, ILogger logger)
    {
        _state = state;
        _logger = logger;

        _menuBuilder = new TrayMenuBuilder(state, logger);
        _menuBuilder.OnMountRequested += HandleMountRequest;
        _menuBuilder.OnUnmountRequested += HandleUnmountRequest;
        _menuBuilder.OnOpenDriveRequested += HandleOpenDrive;
        _menuBuilder.OnSettingsRequested += HandleSettings;
        _menuBuilder.OnInstallDriverRequested += HandleInstallDriver;
        _menuBuilder.OnExitRequested += HandleExit;

        // ساخت آیکون‌ها به صورت برنامه‌نویسی
        _iconConnected = CreateIcon(Color.FromArgb(52, 168, 83));      // سبز (متصل)
        _iconDisconnected = CreateIcon(Color.FromArgb(120, 120, 120)); // خاکستری (قطع)
        _iconSyncing = CreateIcon(Color.FromArgb(66, 133, 244));       // آبی (در حال سینک)

        _notifyIcon = new NotifyIcon
        {
            Icon = _iconDisconnected,
            Text = "CloudDrive - Disconnected",
            Visible = true,
            ContextMenuStrip = _menuBuilder.Build()
        };

        _notifyIcon.DoubleClick += (_, _) => HandleOpenDrive();

        // به‌روزرسانی منو بر اساس وضعیت
        _state.StatusChanged += OnStateChanged;

        _logger.Information("TrayApp initialized");
    }

    // ========== Event Handlers ==========

    private void HandleMountRequest()
    {
        if (_state.IsMounting || _state.IsMounted) return;

        Task.Run(async () =>
        {
            try
            {
                _state.SetStatus(DriveStatus.Mounting);
                ShowBalloon("CloudDrive", $"در حال ماونت کردن {_state.Config.DriveLetter}...",
                    ToolTipIcon.Info, 2000);

                await _state.DriveManager!.MountAsync();

                _state.SetStatus(DriveStatus.Mounted);
                ShowBalloon("CloudDrive ✅",
                    $"درایو {_state.Config.DriveLetter} با موفقیت ماونت شد!\nGoogle Drive در File Explorer در دسترس است.",
                    ToolTipIcon.Info, 4000);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Mount failed");
                _state.SetStatus(DriveStatus.Error);
                ShowBalloon("CloudDrive ❌", $"خطا در ماونت: {ex.Message}", ToolTipIcon.Error, 5000);
            }
        });
    }

    private void HandleUnmountRequest()
    {
        if (!_state.IsMounted) return;

        Task.Run(async () =>
        {
            try
            {
                _state.SetStatus(DriveStatus.Unmounting);
                ShowBalloon("CloudDrive", $"در حال آنماونت {_state.Config.DriveLetter}...",
                    ToolTipIcon.Info, 2000);

                await _state.DriveManager!.UnmountAsync();

                _state.SetStatus(DriveStatus.Disconnected);
                ShowBalloon("CloudDrive", $"درایو {_state.Config.DriveLetter} آنماونت شد.",
                    ToolTipIcon.Info, 3000);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unmount failed");
                _state.SetStatus(DriveStatus.Error);
                ShowBalloon("CloudDrive ❌", $"خطا در آنماونت: {ex.Message}", ToolTipIcon.Error, 5000);
            }
        });
    }

    private void HandleOpenDrive()
    {
        if (!_state.IsMounted)
        {
            ShowBalloon("CloudDrive", "درایو هنوز ماونت نشده است.", ToolTipIcon.Warning, 2000);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", _state.Config.DriveLetter + "\\");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open drive in explorer");
        }
    }

    private void HandleSettings()
    {
        // فرم Settings را روی UI thread باز کن
        _notifyIcon.ContextMenuStrip?.BeginInvoke(() =>
        {
            using var settingsForm = new SettingsForm(_state.Config, _state, _logger);
            if (settingsForm.ShowDialog() == DialogResult.OK)
            {
                // تنظیمات ذخیره شد - به‌روزرسانی وضعیت
                _logger.Information("Settings updated by user");
                UpdateTrayTooltip();
            }
        });
    }

    private void HandleInstallDriver()
    {
        var cabPath = Path.Combine(AppContext.BaseDirectory, "cbfs.cab");
        if (!File.Exists(cabPath))
        {
            MessageBox.Show(
                $"فایل درایور پیدا نشد:\n{cabPath}\n\nلطفاً فایل cbfs.cab را در کنار برنامه قرار دهید.",
                "CloudDrive - خطا",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var result = MessageBox.Show(
            "نصب درایور CBFS نیاز به دسترسی Administrator دارد.\nآیا ادامه می‌دهید؟",
            "CloudDrive - نصب درایور",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        Task.Run(() =>
        {
            try
            {
                _state.DriveManager?.InstallDriver(cabPath);
                ShowBalloon("CloudDrive ✅", "درایور CBFS با موفقیت نصب شد!", ToolTipIcon.Info, 4000);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Driver install failed");
                ShowBalloon("CloudDrive ❌", $"خطا در نصب درایور: {ex.Message}", ToolTipIcon.Error, 5000);
            }
        });
    }

    private void HandleExit()
    {
        Task.Run(async () =>
        {
            if (_state.IsMounted)
            {
                try { await _state.DriveManager!.UnmountAsync(); }
                catch { }
            }

            _notifyIcon.ContextMenuStrip?.BeginInvoke(() =>
            {
                Application.Exit();
            });
        });
    }

    // ========== State Changed Handler ==========

    private void OnStateChanged(DriveStatus status)
    {
        // به‌روزرسانی UI باید در Main Thread انجام شود
        _notifyIcon.ContextMenuStrip?.BeginInvoke(() =>
        {
            UpdateTrayIcon(status);
            UpdateTrayTooltip();
            _menuBuilder.UpdateMenuItems(status);
        });
    }

    private void UpdateTrayIcon(DriveStatus status)
    {
        _notifyIcon.Icon = status switch
        {
            DriveStatus.Mounted => _iconConnected,
            DriveStatus.Mounting or DriveStatus.Unmounting => _iconSyncing,
            _ => _iconDisconnected
        };
    }

    private void UpdateTrayTooltip()
    {
        var status = _state.CurrentStatus switch
        {
            DriveStatus.Mounted => $"متصل • {_state.Config.DriveLetter} • {_state.Config.VolumeLabel}",
            DriveStatus.Mounting => "در حال اتصال...",
            DriveStatus.Unmounting => "در حال قطع اتصال...",
            DriveStatus.Error => "خطا در اتصال",
            _ => "قطع - کلیک راست برای ماونت"
        };

        // NotifyIcon.Text حداکثر 63 کاراکتر دارد
        var tooltip = $"CloudDrive | {status}";
        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    private void ShowBalloon(string title, string message, ToolTipIcon icon, int duration)
    {
        try
        {
            _notifyIcon.ContextMenuStrip?.BeginInvoke(() =>
            {
                _notifyIcon.ShowBalloonTip(duration, title, message, icon);
            });
        }
        catch { }
    }

    // ========== Auto-Start Registry ==========

    public static void SetAutoStart(bool enable, string executablePath)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "CloudDrive";

        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        if (key == null) return;

        if (enable)
            key.SetValue(valueName, $"\"{executablePath}\" --minimized");
        else
            key.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public static bool IsAutoStartEnabled()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "CloudDrive";

        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        return key?.GetValue(valueName) != null;
    }

    // ========== آیکون‌سازی برنامه‌نویسی ==========

    private static Icon CreateIcon(Color color)
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // پس‌زمینه دایره‌ای
        using var bgBrush = new SolidBrush(color);
        g.FillEllipse(bgBrush, 2, 2, 28, 28);

        // ابر سفید (نماد Cloud)
        using var cloudBrush = new SolidBrush(Color.White);
        // بدنه ابر
        g.FillEllipse(cloudBrush, 5, 14, 12, 10);
        g.FillEllipse(cloudBrush, 12, 10, 14, 12);
        g.FillEllipse(cloudBrush, 18, 14, 10, 10);
        g.FillRectangle(cloudBrush, 5, 18, 23, 6);

        var iconHandle = bmp.GetHicon();
        var icon = Icon.FromHandle(iconHandle);
        bmp.Dispose();
        return icon;
    }

    public void Dispose()
    {
        _state.StatusChanged -= OnStateChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _iconConnected?.Dispose();
        _iconDisconnected?.Dispose();
        _iconSyncing?.Dispose();
        GC.SuppressFinalize(this);
    }
}
