namespace CloudDrive.Core.Models;

/// <summary>
/// مدل اطلاعات فایل یا پوشه در فضای ابری
/// </summary>
public class CloudFileItem
{
    /// <summary>شناسه یکتای فایل در سرویس ابری (مثلاً Google Drive File ID)</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>نام فایل یا پوشه</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>شناسه پوشه والد</summary>
    public string ParentId { get; set; } = string.Empty;

    /// <summary>اندازه فایل به بایت</summary>
    public long Size { get; set; }

    /// <summary>آیا این آیتم یک پوشه است؟</summary>
    public bool IsDirectory { get; set; }

    /// <summary>تاریخ ساخت</summary>
    public DateTime CreatedTime { get; set; }

    /// <summary>تاریخ آخرین تغییر</summary>
    public DateTime ModifiedTime { get; set; }

    /// <summary>نوع MIME فایل</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>مسیر کامل در درایو مجازی (مثلاً \Documents\report.pdf)</summary>
    public string VirtualPath { get; set; } = string.Empty;

    /// <summary>MIME Type خاص گوگل برای پوشه‌ها</summary>
    public const string FolderMimeType = "application/vnd.google-apps.folder";
}
