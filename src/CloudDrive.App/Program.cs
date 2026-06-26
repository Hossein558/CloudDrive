using CloudDrive.Core;
using CloudDrive.Core.Auth;
using CloudDrive.Core.CloudProviders;
using CloudDrive.Core.Models;
using Serilog;

namespace CloudDrive.App;

class Program
{
    // کلید لایسنس CBFS Connect 2024
    private const string CbfsLicense =
        "43434E4A414230303333393434374D304E544152415A004B41454C44515450454449464E564254004A333931533650350000454341413943383237574A560000";

    static async Task Main(string[] args)
    {
        // --- 1. تنظیم لاگر ---
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(AppContext.BaseDirectory, "logs", "clouddrive-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var logger = Log.Logger;

        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            logger.Information("═══════════════════════════════════════════");
            logger.Information("  ☁️  CloudDrive - Google Drive as Z:");
            logger.Information("  Version 1.0.0 (CBFS Connect 2024)");
            logger.Information("═══════════════════════════════════════════");

            // --- 2. تنظیمات ---
            var config = new DriveConfig
            {
                DriveLetter = "Z:",
                VolumeLabel = "Google Drive",
                CbfsLicenseKey = CbfsLicense,
                GoogleCredentialsPath = Path.Combine(AppContext.BaseDirectory, "credentials.json"),
                ApplicationName = "CloudDrive"
            };

            // --- 3. بررسی فایل credentials ---
            if (!File.Exists(config.GoogleCredentialsPath))
            {
                logger.Error("❌ فایل credentials.json پیدا نشد!");
                logger.Error("لطفاً فایل credentials.json را از Google Cloud Console دانلود کنید");
                logger.Error("و در مسیر {Path} قرار دهید", config.GoogleCredentialsPath);
                logger.Information("");
                logger.Information("راهنما:");
                logger.Information("1. به https://console.cloud.google.com بروید");
                logger.Information("2. APIs & Services > Credentials > Create Credentials > OAuth Client ID");
                logger.Information("3. Application Type: Desktop Application");
                logger.Information("4. فایل JSON را دانلود و با نام credentials.json ذخیره کنید");
                Console.ReadKey();
                return;
            }

            // --- 4. احراز هویت گوگل ---
            var authManager = new GoogleAuthManager(
                config.GoogleCredentialsPath,
                config.ApplicationName,
                logger);

            // --- 5. ساخت Provider ---
            var provider = new GoogleDriveProvider(
                async () => await authManager.AuthenticateAsync(),
                logger);

            // --- 6. ساخت و ماونت درایو ---
            using var driveManager = new VirtualDriveManager(provider, config, logger);

            logger.Information("Mounting drive {DriveLetter}...", config.DriveLetter);
            await driveManager.MountAsync();

            logger.Information("");
            logger.Information("════════════════════════════════════════════");
            logger.Information("  ✅ درایو {Letter} با موفقیت ماونت شد!", config.DriveLetter);
            logger.Information("  📁 نام درایو: {Label}", config.VolumeLabel);
            logger.Information("  🔑 احراز هویت: Google OAuth 2.0");
            logger.Information("════════════════════════════════════════════");
            logger.Information("");
            logger.Information("برای خروج و آنماونت درایو، کلید Enter را فشار دهید...");

            Console.ReadLine();

            // --- 7. آنماونت ---
            logger.Information("Unmounting...");
            await driveManager.UnmountAsync();
            logger.Information("✅ درایو آنماونت شد. خداحافظ!");
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Fatal error occurred");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
