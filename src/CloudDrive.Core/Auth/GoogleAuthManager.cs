using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Serilog;

namespace CloudDrive.Core.Auth;

/// <summary>
/// مدیریت احراز هویت OAuth 2.0 با گوگل.
/// در اولین اجرا مرورگر باز شده و کاربر لاگین می‌کند.
/// توکن ذخیره می‌شود و دفعات بعدی نیاز به لاگین نیست.
/// </summary>
public class GoogleAuthManager
{
    private readonly string _credentialsPath;
    private readonly string _applicationName;
    private readonly ILogger _logger;
    private DriveService? _driveService;

    private static readonly string[] Scopes = { DriveService.Scope.Drive };
    private const string TokenFolder = "CloudDrive.Tokens";

    public GoogleAuthManager(string credentialsPath, string applicationName, ILogger logger)
    {
        _credentialsPath = credentialsPath;
        _applicationName = applicationName;
        _logger = logger;
    }

    /// <summary>
    /// احراز هویت و ساخت سرویس Google Drive
    /// </summary>
    public async Task<DriveService> AuthenticateAsync()
    {
        _logger.Information("Starting Google OAuth 2.0 authentication...");

        UserCredential credential;

        await using (var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read))
        {
            var tokenPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                TokenFolder);

            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                (await GoogleClientSecrets.FromStreamAsync(stream)).Secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore(tokenPath, true));

            _logger.Information("OAuth token stored at: {TokenPath}", tokenPath);
        }

        _driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _applicationName
        });

        _logger.Information("Google Drive service initialized successfully");
        return _driveService;
    }

    /// <summary>
    /// دریافت سرویس Google Drive (باید قبلاً Authenticate شده باشد)
    /// </summary>
    public DriveService GetService()
    {
        return _driveService ?? throw new InvalidOperationException(
            "Not authenticated. Call AuthenticateAsync() first.");
    }
}
