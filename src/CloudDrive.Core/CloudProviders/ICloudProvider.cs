using CloudDrive.Core.Models;

namespace CloudDrive.Core.CloudProviders;

/// <summary>
/// اینترفیس عمومی برای تمام سرویس‌های ابری.
/// با پیاده‌سازی این اینترفیس، هر سرویس ابری (Google Drive, OneDrive, S3, ...) 
/// قابل اتصال به درایو مجازی خواهد بود.
/// </summary>
public interface ICloudProvider
{
    /// <summary>نام سرویس ابری (مثلاً "Google Drive")</summary>
    string ProviderName { get; }

    /// <summary>احراز هویت و اتصال به سرویس ابری</summary>
    Task ConnectAsync();

    /// <summary>قطع اتصال از سرویس ابری</summary>
    Task DisconnectAsync();

    /// <summary>آیا اتصال برقرار است؟</summary>
    bool IsConnected { get; }

    // --- عملیات لیست و متادیتا ---

    /// <summary>لیست فایل‌ها و پوشه‌های درون یک پوشه</summary>
    /// <param name="folderId">شناسه پوشه (null یا "root" برای ریشه)</param>
    Task<List<CloudFileItem>> ListFilesAsync(string? folderId = null);

    /// <summary>دریافت اطلاعات یک فایل یا پوشه</summary>
    Task<CloudFileItem?> GetFileInfoAsync(string fileId);

    /// <summary>دریافت اطلاعات یک فایل بر اساس مسیر مجازی</summary>
    Task<CloudFileItem?> GetFileInfoByPathAsync(string virtualPath);

    /// <summary>دریافت فضای کل و استفاده‌شده</summary>
    Task<(long totalSpace, long usedSpace)> GetStorageQuotaAsync();

    // --- عملیات خواندن ---

    /// <summary>دانلود محتوای فایل</summary>
    /// <param name="fileId">شناسه فایل</param>
    /// <returns>استریم محتوای فایل</returns>
    Task<Stream> DownloadFileAsync(string fileId);

    /// <summary>دانلود بخشی از فایل (برای فایل‌های بزرگ)</summary>
    Task<byte[]> DownloadFileRangeAsync(string fileId, long offset, int count);

    // --- عملیات نوشتن ---

    /// <summary>آپلود فایل جدید</summary>
    Task<string> UploadFileAsync(string parentFolderId, string fileName, Stream content, string mimeType = "application/octet-stream");

    /// <summary>به‌روزرسانی محتوای یک فایل موجود</summary>
    Task UpdateFileContentAsync(string fileId, Stream content);

    /// <summary>ساخت پوشه جدید</summary>
    Task<string> CreateFolderAsync(string parentFolderId, string folderName);

    // --- عملیات ساختاری ---

    /// <summary>حذف فایل یا پوشه</summary>
    /// <param name="fileId">شناسه فایل</param>
    /// <param name="permanent">حذف دائمی یا انتقال به سطل زباله</param>
    Task DeleteFileAsync(string fileId, bool permanent = false);

    /// <summary>تغییر نام فایل یا پوشه</summary>
    Task RenameFileAsync(string fileId, string newName);

    /// <summary>جابجایی فایل به پوشه دیگر</summary>
    Task MoveFileAsync(string fileId, string newParentId, string? oldParentId = null);
}
