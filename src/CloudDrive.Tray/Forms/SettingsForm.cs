using CloudDrive.Core.Models;
using CloudDrive.Tray;
using Serilog;

namespace CloudDrive.Tray.Forms;

/// <summary>
/// فرم تنظیمات CloudDrive.
/// کاربر می‌تواند: حرف درایو، اندازه کش، مسیر کش، AutoStart و Scope را تنظیم کند.
/// </summary>
public class SettingsForm : Form
{
    private readonly DriveConfig _config;
    private readonly AppState _state;
    private readonly ILogger _logger;

    // کنترل‌های فرم
    private ComboBox _driveLetterCombo = null!;
    private TextBox _volumeLabelText = null!;
    private NumericUpDown _cacheSizeNumeric = null!;
    private TextBox _cacheFolderText = null!;
    private Button _cacheFolderBrowse = null!;
    private NumericUpDown _cacheTtlNumeric = null!;
    private CheckBox _autoStartCheck = null!;
    private CheckBox _autoMountCheck = null!;
    private Button _clearCacheBtn = null!;
    private Label _cacheStatsLabel = null!;
    private Button _okBtn = null!;
    private Button _cancelBtn = null!;
    private Button _applyBtn = null!;

    public SettingsForm(DriveConfig config, AppState state, ILogger logger)
    {
        _config = new DriveConfig  // کپی برای cancel
        {
            DriveLetter = config.DriveLetter,
            VolumeLabel = config.VolumeLabel,
            CachePath = config.CachePath,
            MaxCacheSizeBytes = config.MaxCacheSizeBytes,
            MetadataCacheTtl = config.MetadataCacheTtl,
            CbfsLicenseKey = config.CbfsLicenseKey,
            GoogleCredentialsPath = config.GoogleCredentialsPath,
            ApplicationName = config.ApplicationName
        };
        _state = state;
        _logger = logger;

        InitializeComponent();
        LoadValues();
    }

    private void InitializeComponent()
    {
        // --- تنظیمات پنجره ---
        Text = "CloudDrive - تنظیمات";
        Size = new Size(500, 560);
        MinimumSize = new Size(480, 540);
        MaximumSize = new Size(600, 620);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(248, 249, 250);
        Font = new Font("Segoe UI", 9f);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        // ========== Panel اصلی ==========
        var mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16)
        };
        Controls.Add(mainPanel);

        int y = 16;
        int labelWidth = 140;
        int controlLeft = 16;

        // ========== بخش ۱: درایو ==========
        AddSectionHeader(mainPanel, "📁  تنظیمات درایو", ref y);

        // حرف درایو
        AddLabel(mainPanel, "حرف درایو:", y, labelWidth);
        _driveLetterCombo = new ComboBox
        {
            Left = controlLeft + labelWidth + 8,
            Top = y,
            Width = 80,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9f)
        };
        foreach (var c in "GHIJKLMNOPQRSTUVWXYZ")
            _driveLetterCombo.Items.Add($"{c}:");
        mainPanel.Controls.Add(_driveLetterCombo);
        y += 32;

        // نام درایو
        AddLabel(mainPanel, "نام درایو:", y, labelWidth);
        _volumeLabelText = new TextBox
        {
            Left = controlLeft + labelWidth + 8,
            Top = y,
            Width = 240,
            MaxLength = 32
        };
        mainPanel.Controls.Add(_volumeLabelText);
        y += 36;

        // ========== بخش ۲: کش ==========
        AddSectionHeader(mainPanel, "💾  تنظیمات کش", ref y);

        // اندازه کش
        AddLabel(mainPanel, "حداکثر اندازه کش:", y, labelWidth);
        _cacheSizeNumeric = new NumericUpDown
        {
            Left = controlLeft + labelWidth + 8,
            Top = y,
            Width = 100,
            Minimum = 100,
            Maximum = 50000,
            Increment = 100,
            Value = 1024
        };
        mainPanel.Controls.Add(_cacheSizeNumeric);
        var mbLabel = new Label
        {
            Text = "مگابایت",
            Left = controlLeft + labelWidth + 118,
            Top = y + 3,
            AutoSize = true
        };
        mainPanel.Controls.Add(mbLabel);
        y += 32;

        // مسیر کش
        AddLabel(mainPanel, "پوشه کش:", y, labelWidth);
        _cacheFolderText = new TextBox
        {
            Left = controlLeft + labelWidth + 8,
            Top = y,
            Width = 240
        };
        mainPanel.Controls.Add(_cacheFolderText);
        _cacheFolderBrowse = new Button
        {
            Text = "...",
            Left = controlLeft + labelWidth + 256,
            Top = y - 1,
            Width = 36,
            Height = 24
        };
        _cacheFolderBrowse.Click += BrowseCacheFolder;
        mainPanel.Controls.Add(_cacheFolderBrowse);
        y += 32;

        // TTL متادیتا
        AddLabel(mainPanel, "مدت اعتبار لیست:", y, labelWidth);
        _cacheTtlNumeric = new NumericUpDown
        {
            Left = controlLeft + labelWidth + 8,
            Top = y,
            Width = 80,
            Minimum = 1,
            Maximum = 60,
            Value = 5
        };
        mainPanel.Controls.Add(_cacheTtlNumeric);
        var ttlLabel = new Label
        {
            Text = "دقیقه",
            Left = controlLeft + labelWidth + 96,
            Top = y + 3,
            AutoSize = true
        };
        mainPanel.Controls.Add(ttlLabel);
        y += 32;

        // آمار کش + دکمه پاک‌سازی
        _cacheStatsLabel = new Label
        {
            Left = controlLeft,
            Top = y,
            Width = 300,
            AutoSize = false,
            ForeColor = Color.Gray,
            Text = "محاسبه آمار کش..."
        };
        mainPanel.Controls.Add(_cacheStatsLabel);

        _clearCacheBtn = new Button
        {
            Text = "🗑  پاک کردن کش",
            Left = controlLeft + 310,
            Top = y - 3,
            Width = 130,
            Height = 26,
            ForeColor = Color.FromArgb(200, 50, 50)
        };
        _clearCacheBtn.Click += ClearCache;
        mainPanel.Controls.Add(_clearCacheBtn);
        y += 36;

        // ========== بخش ۳: رفتار ==========
        AddSectionHeader(mainPanel, "⚙  رفتار برنامه", ref y);

        // AutoStart
        _autoStartCheck = new CheckBox
        {
            Text = "اجرای خودکار با ویندوز",
            Left = controlLeft,
            Top = y,
            AutoSize = true,
            Checked = TrayApp.IsAutoStartEnabled()
        };
        mainPanel.Controls.Add(_autoStartCheck);
        y += 26;

        // AutoMount
        _autoMountCheck = new CheckBox
        {
            Text = "ماونت خودکار هنگام اجرای برنامه",
            Left = controlLeft,
            Top = y,
            AutoSize = true
        };
        mainPanel.Controls.Add(_autoMountCheck);
        y += 32;

        // ========== دکمه‌های پایین ==========
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.FromArgb(240, 240, 240),
            Padding = new Padding(8)
        };
        Controls.Add(buttonPanel);

        _okBtn = CreateButton("✔  تأیید", DialogResult.OK, Color.FromArgb(52, 168, 83));
        _cancelBtn = CreateButton("✖  انصراف", DialogResult.Cancel, Color.FromArgb(150, 150, 150));
        _applyBtn = CreateButton("↩  اعمال", DialogResult.None, Color.FromArgb(66, 133, 244));

        _applyBtn.Click += (_, _) => ApplySettings();
        _okBtn.Click += (_, _) => { ApplySettings(); DialogResult = DialogResult.OK; Close(); };
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        // چیدمان دکمه‌ها (RTL)
        _okBtn.Left = 8;
        _okBtn.Top = 9;
        _cancelBtn.Left = 116;
        _cancelBtn.Top = 9;
        _applyBtn.Left = 224;
        _applyBtn.Top = 9;

        buttonPanel.Controls.AddRange(new Control[] { _okBtn, _cancelBtn, _applyBtn });

        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
    }

    // ========== Helpers ==========

    private static void AddSectionHeader(Panel parent, string text, ref int y)
    {
        var header = new Label
        {
            Text = text,
            Left = 16,
            Top = y,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(66, 133, 244)
        };
        parent.Controls.Add(header);

        var line = new Panel
        {
            Left = 16,
            Top = y + 22,
            Width = 448,
            Height = 1,
            BackColor = Color.FromArgb(200, 220, 255)
        };
        parent.Controls.Add(line);
        y += 32;
    }

    private static void AddLabel(Panel parent, string text, int top, int width)
    {
        parent.Controls.Add(new Label
        {
            Text = text,
            Left = 16,
            Top = top + 3,
            Width = width,
            TextAlign = ContentAlignment.MiddleRight
        });
    }

    private static Button CreateButton(string text, DialogResult dialogResult, Color color)
    {
        return new Button
        {
            Text = text,
            Width = 100,
            Height = 32,
            DialogResult = dialogResult,
            BackColor = color,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            FlatAppearance = { BorderSize = 0 }
        };
    }

    // ========== Logic ==========

    private void LoadValues()
    {
        // حرف درایو
        var driveLetters = _driveLetterCombo.Items.Cast<string>().ToList();
        var idx = driveLetters.IndexOf(_config.DriveLetter);
        _driveLetterCombo.SelectedIndex = idx >= 0 ? idx : 0;

        _volumeLabelText.Text = _config.VolumeLabel;
        _cacheSizeNumeric.Value = _config.MaxCacheSizeBytes / (1024 * 1024);
        _cacheFolderText.Text = _config.CachePath;
        _cacheTtlNumeric.Value = (int)_config.MetadataCacheTtl.TotalMinutes;

        UpdateCacheStats();
    }

    private void UpdateCacheStats()
    {
        try
        {
            if (Directory.Exists(_config.CachePath))
            {
                var files = Directory.GetFiles(_config.CachePath, "*.cache");
                long totalSize = files.Sum(f => new FileInfo(f).Length);
                _cacheStatsLabel.Text = $"کش فعلی: {files.Length} فایل • {FormatBytes(totalSize)}";
            }
            else
            {
                _cacheStatsLabel.Text = "کش فعلی: خالی";
            }
        }
        catch
        {
            _cacheStatsLabel.Text = "خواندن آمار کش ممکن نیست";
        }
    }

    private void BrowseCacheFolder(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "پوشه کش CloudDrive را انتخاب کنید",
            SelectedPath = _cacheFolderText.Text,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            _cacheFolderText.Text = dialog.SelectedPath;
    }

    private void ClearCache(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            $"آیا از پاک کردن تمام فایل‌های کش اطمینان دارید؟\nمسیر: {_config.CachePath}",
            "پاک کردن کش",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        try
        {
            if (Directory.Exists(_config.CachePath))
            {
                foreach (var f in Directory.GetFiles(_config.CachePath, "*.cache"))
                    File.Delete(f);
            }
            _logger.Information("Cache cleared by user");
            UpdateCacheStats();
            MessageBox.Show("کش با موفقیت پاک شد.", "CloudDrive", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to clear cache");
            MessageBox.Show($"خطا در پاک کردن کش:\n{ex.Message}", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplySettings()
    {
        // ذخیره تنظیمات در Config
        _config.DriveLetter = _driveLetterCombo.SelectedItem?.ToString() ?? "Z:";
        _config.VolumeLabel = _volumeLabelText.Text.Trim();
        if (string.IsNullOrEmpty(_config.VolumeLabel)) _config.VolumeLabel = "Google Drive";
        _config.MaxCacheSizeBytes = (long)_cacheSizeNumeric.Value * 1024 * 1024;
        _config.CachePath = _cacheFolderText.Text.Trim();
        _config.MetadataCacheTtl = TimeSpan.FromMinutes((double)_cacheTtlNumeric.Value);

        // AutoStart
        TrayApp.SetAutoStart(_autoStartCheck.Checked, Application.ExecutablePath);

        // به‌روزرسانی State
        _state.UpdateConfig(_config);

        _logger.Information("Settings applied: Drive={Drive}, Label={Label}, CacheSize={Size}MB",
            _config.DriveLetter, _config.VolumeLabel, _cacheSizeNumeric.Value);

        UpdateCacheStats();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / 1024.0 / 1024 / 1024:F1} GB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / 1024.0 / 1024:F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
