namespace CloudDrive.Core.Models;

/// <summary>
/// تنظیمات درایو مجازی
/// </summary>
public class DriveConfig
{
    /// <summary>حرف درایو (مثلاً "Z:")</summary>
    public string DriveLetter { get; set; } = "Z:";

    /// <summary>نام درایو که در File Explorer نمایش داده می‌شود</summary>
    public string VolumeLabel { get; set; } = "Google Drive";

    /// <summary>مسیر کش محلی فایل‌ها</summary>
    public string CachePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CloudDrive", "cache");

    /// <summary>حداکثر اندازه کش به بایت (پیش‌فرض: ۱ گیگابایت)</summary>
    public long MaxCacheSizeBytes { get; set; } = 1L * 1024 * 1024 * 1024;

    /// <summary>مدت زمان اعتبار کش متادیتا (پیش‌فرض: ۵ دقیقه)</summary>
    public TimeSpan MetadataCacheTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>کلید لایسنس CBFS Connect</summary>
    public string CbfsLicenseKey { get; set; } = string.Empty;

    /// <summary>مسیر فایل credentials.json گوگل</summary>
    public string GoogleCredentialsPath { get; set; } = "credentials.json";

    /// <summary>نام اپلیکیشن برای API گوگل</summary>
    public string ApplicationName { get; set; } = "CloudDrive";
}
