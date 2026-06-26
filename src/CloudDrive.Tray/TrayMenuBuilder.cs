using Serilog;

namespace CloudDrive.Tray;

/// <summary>
/// سازنده منوی کانتکست System Tray.
/// </summary>
public class TrayMenuBuilder
{
    private readonly AppState _state;
    private readonly ILogger _logger;

    // آیتم‌های منو که نیاز به به‌روزرسانی دارند
    private ToolStripMenuItem? _mountItem;
    private ToolStripMenuItem? _unmountItem;
    private ToolStripMenuItem? _openDriveItem;
    private ToolStripMenuItem? _statusItem;

    // رویدادها
    public event Action? OnMountRequested;
    public event Action? OnUnmountRequested;
    public event Action? OnOpenDriveRequested;
    public event Action? OnSettingsRequested;
    public event Action? OnInstallDriverRequested;
    public event Action? OnExitRequested;

    public TrayMenuBuilder(AppState state, ILogger logger)
    {
        _state = state;
        _logger = logger;
    }

    public ContextMenuStrip Build()
    {
        var menu = new ContextMenuStrip();
        menu.Font = new Font("Segoe UI", 9f);
        menu.Renderer = new ModernMenuRenderer();

        // --- هدر (عنوان برنامه) ---
        var header = new ToolStripLabel("☁ CloudDrive")
        {
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(66, 133, 244),
            Enabled = false
        };
        menu.Items.Add(header);

        // وضعیت فعلی
        _statusItem = new ToolStripMenuItem("● قطع")
        {
            ForeColor = Color.Gray,
            Enabled = false,
            Font = new Font("Segoe UI", 8.5f)
        };
        menu.Items.Add(_statusItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- باز کردن درایو ---
        _openDriveItem = new ToolStripMenuItem(
            $"📂  باز کردن {_state.Config.DriveLetter} در File Explorer",
            null,
            (_, _) => OnOpenDriveRequested?.Invoke())
        {
            Enabled = false,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        menu.Items.Add(_openDriveItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- ماونت ---
        _mountItem = new ToolStripMenuItem(
            "🔌  اتصال (Mount)",
            null,
            (_, _) => OnMountRequested?.Invoke());
        menu.Items.Add(_mountItem);

        // --- آنماونت ---
        _unmountItem = new ToolStripMenuItem(
            "⏏  قطع اتصال (Unmount)",
            null,
            (_, _) => OnUnmountRequested?.Invoke())
        {
            Enabled = false
        };
        menu.Items.Add(_unmountItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- تنظیمات ---
        var settingsItem = new ToolStripMenuItem(
            "⚙  تنظیمات...",
            null,
            (_, _) => OnSettingsRequested?.Invoke());
        menu.Items.Add(settingsItem);

        // --- نصب درایور ---
        var installDriverItem = new ToolStripMenuItem(
            "🔧  نصب/بروزرسانی درایور CBFS",
            null,
            (_, _) => OnInstallDriverRequested?.Invoke());
        menu.Items.Add(installDriverItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- درباره ---
        var aboutItem = new ToolStripMenuItem(
            "ℹ  درباره CloudDrive v1.0",
            null,
            (_, _) => ShowAbout());
        menu.Items.Add(aboutItem);

        menu.Items.Add(new ToolStripSeparator());

        // --- خروج ---
        var exitItem = new ToolStripMenuItem(
            "✖  خروج",
            null,
            (_, _) => OnExitRequested?.Invoke())
        {
            ForeColor = Color.FromArgb(200, 50, 50)
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    public void UpdateMenuItems(DriveStatus status)
    {
        bool mounted = status == DriveStatus.Mounted;
        bool transitioning = status is DriveStatus.Mounting or DriveStatus.Unmounting;

        if (_mountItem != null)
            _mountItem.Enabled = !mounted && !transitioning;

        if (_unmountItem != null)
            _unmountItem.Enabled = mounted && !transitioning;

        if (_openDriveItem != null)
        {
            _openDriveItem.Enabled = mounted;
            _openDriveItem.Text = $"📂  باز کردن {_state.Config.DriveLetter} در File Explorer";
        }

        if (_statusItem != null)
        {
            (_statusItem.Text, _statusItem.ForeColor) = status switch
            {
                DriveStatus.Mounted => ($"● متصل  •  {_state.Config.DriveLetter} • {_state.Config.VolumeLabel}",
                    Color.FromArgb(52, 168, 83)),
                DriveStatus.Mounting => ("⟳ در حال اتصال...", Color.FromArgb(66, 133, 244)),
                DriveStatus.Unmounting => ("⟳ در حال قطع اتصال...", Color.FromArgb(251, 188, 4)),
                DriveStatus.Error => ("✗ خطا در اتصال", Color.FromArgb(234, 67, 53)),
                _ => ("● قطع", Color.Gray)
            };
        }
    }

    private static void ShowAbout()
    {
        MessageBox.Show(
            "CloudDrive v1.0\n\n" +
            "مانت Google Drive به عنوان درایو محلی در ویندوز\n" +
            "با استفاده از CBFS Connect 2024\n\n" +
            "ساخته شده با .NET 8 و CBFS Connect SDK\n\n" +
            "© 2024 - Hossein558",
            "درباره CloudDrive",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}

/// <summary>
/// رندرر سفارشی برای منوی مدرن با تم تیره.
/// </summary>
public class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    public ModernMenuRenderer() : base(new ModernColorTable()) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        var g = e.Graphics;

        if (item.Selected && item.Enabled)
        {
            using var brush = new SolidBrush(Color.FromArgb(230, 240, 255));
            using var pen = new Pen(Color.FromArgb(66, 133, 244), 1);
            var rect = new Rectangle(2, 0, item.Width - 4, item.Height - 1);
            g.FillRectangle(brush, rect);
            g.DrawRectangle(pen, rect);
        }
        else
        {
            base.OnRenderMenuItemBackground(e);
        }
    }
}

public class ModernColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(230, 240, 255);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(210, 230, 255);
    public override Color MenuItemBorder => Color.FromArgb(66, 133, 244);
    public override Color MenuBorder => Color.FromArgb(200, 200, 200);
    public override Color ToolStripDropDownBackground => Color.FromArgb(252, 252, 252);
    public override Color ImageMarginGradientBegin => Color.FromArgb(245, 245, 245);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(245, 245, 245);
    public override Color ImageMarginGradientEnd => Color.FromArgb(245, 245, 245);
    public override Color SeparatorDark => Color.FromArgb(220, 220, 220);
    public override Color SeparatorLight => Color.White;
}
