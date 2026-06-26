using CloudDrive.Core;
using CloudDrive.Core.Auth;
using CloudDrive.Core.CloudProviders;
using CloudDrive.Core.Models;
using Serilog;

namespace CloudDrive.Tray;

/// <summary>
/// نقطه ورود برنامه CloudDrive System Tray.
/// </summary>
static class Program
{
    // کلید لایسنس CBFS Connect 2024
    private const string CbfsLicense =
        "43434E4A414230303333393434374D304E544152415A004B41454C44515450454449464E564254004A333931533650350000454341413943383237574A560000";

    [STAThread]
    static async Task Main(string[] args)
    {
        // جلوگیری از اجرای چندین نمونه همزمان
        using var mutex = new System.Threading.Mutex(true, "CloudDrive_SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "CloudDrive در حال اجرا است.\nنماد آن را در System Tray پیدا کنید.",
                "CloudDrive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // --- تنظیم لاگر ---
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CloudDrive", "logs", "clouddrive-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var logger = Log.Logger;

        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);

            logger.Information("CloudDrive Tray starting...");

            // --- تنظیمات پایه ---
            var config = new DriveConfig
            {
                DriveLetter = "Z:",
                VolumeLabel = "Google Drive",
                CbfsLicenseKey = CbfsLicense,
                GoogleCredentialsPath = Path.Combine(AppContext.BaseDirectory, "credentials.json"),
                ApplicationName = "CloudDrive",
                CachePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CloudDrive", "cache"),
                MaxCacheSizeBytes = 1L * 1024 * 1024 * 1024,
                MetadataCacheTtl = TimeSpan.FromMinutes(5)
            };

            // --- بررسی فایل credentials ---
            if (!File.Exists(config.GoogleCredentialsPath))
            {
                ShowCredentialsWarning(config.GoogleCredentialsPath);
                // برنامه ادامه می‌دهد اما Mount نمی‌شود
            }

            // --- ساخت State ---
            var state = new AppState(config, logger);

            // --- ساخت Provider ---
            var authManager = new GoogleAuthManager(
                config.GoogleCredentialsPath,
                config.ApplicationName,
                logger);

            var provider = new GoogleDriveProvider(
                async () => await authManager.AuthenticateAsync(),
                logger);

            // --- ساخت DriveManager ---
            var driveManager = new VirtualDriveManager(provider, config, logger);
            state.DriveManager = driveManager;

            // --- ساخت TrayApp ---
            using var trayApp = new TrayApp(state, logger);

            // --- Auto-mount اگر flag داده شده ---
            bool autoMount = args.Contains("--auto-mount");
            if (autoMount && File.Exists(config.GoogleCredentialsPath))
            {
                logger.Information("Auto-mount requested, mounting in background...");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1500); // صبر برای آماده شدن TrayApp
                    state.SetStatus(DriveStatus.Mounting);
                    try
                    {
                        await driveManager.MountAsync();
                        state.SetStatus(DriveStatus.Mounted);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Auto-mount failed");
                        state.SetStatus(DriveStatus.Error);
                    }
                });
            }

            logger.Information("CloudDrive Tray ready. Running message loop...");

            // --- Message Loop ---
            Application.Run();

            // --- پاکسازی ---
            logger.Information("Application exiting, cleaning up...");
            if (state.IsMounted)
            {
                try { await driveManager.UnmountAsync(); }
                catch (Exception ex) { logger.Error(ex, "Error during final unmount"); }
            }
            driveManager.Dispose();
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Fatal error in CloudDrive Tray");
            MessageBox.Show(
                $"خطای بحرانی:\n{ex.Message}\n\nجزئیات در فایل لاگ موجود است.",
                "CloudDrive - خطا",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void ShowCredentialsWarning(string credPath)
    {
        MessageBox.Show(
            $"⚠ فایل credentials.json پیدا نشد!\n\n" +
            $"مسیر مورد انتظار:\n{credPath}\n\n" +
            "برای ماونت کردن گوگل درایو، این فایل لازم است.\n\n" +
            "مراحل دریافت:\n" +
            "1. به console.cloud.google.com بروید\n" +
            "2. APIs & Services > Credentials\n" +
            "3. Create OAuth 2.0 Client ID (Desktop App)\n" +
            "4. فایل JSON را دانلود و با نام credentials.json در کنار برنامه قرار دهید\n\n" +
            "برنامه ادامه می‌یابد اما تا قرار دادن فایل، ماونت ممکن نیست.",
            "CloudDrive - پیکربندی ناقص",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
