using Velopack;
using Velopack.Sources;

namespace SubtitlesFixer.App;

internal static class UpdateService
{
    private const string RepositoryUrl = "https://github.com/cosmintrica/SubtitlesFixer";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    public static bool IsEligibleForAutomaticUpdates()
    {
        var baseDir = AppContext.BaseDirectory;
        return baseDir.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) < 0
               && baseDir.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) < 0;
    }

    public static bool ShouldCheckNow(AppSettings settings)
    {
        if (!settings.LastUpdateCheckUtc.HasValue)
            return true;

        return DateTimeOffset.UtcNow - settings.LastUpdateCheckUtc.Value >= CheckInterval;
    }

    public static async Task<PreparedUpdate?> CheckAndPrepareAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
        try
        {
            var updateInfo = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updateInfo is null)
            {
                MarkSuccessfulCheck(settings);
                Release(manager);
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await manager.DownloadUpdatesAsync(updateInfo).ConfigureAwait(false);
            MarkSuccessfulCheck(settings);

            return new PreparedUpdate(manager, updateInfo, GetVersionLabel(updateInfo));
        }
        catch
        {
            Release(manager);
            throw;
        }
    }

    private static string GetVersionLabel(UpdateInfo updateInfo)
    {
        var version = updateInfo.TargetFullRelease?.Version?.ToString();
        return string.IsNullOrWhiteSpace(version) ? "o versiune noua" : $"v{version}";
    }

    private static void MarkSuccessfulCheck(AppSettings settings)
    {
        settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
        settings.Save();
    }

    public static void Release(UpdateManager? manager)
    {
        if (manager is IDisposable disposable)
            disposable.Dispose();
    }
}

internal sealed class PreparedUpdate
{
    public PreparedUpdate(UpdateManager manager, UpdateInfo updateInfo, string versionLabel)
    {
        Manager = manager;
        UpdateInfo = updateInfo;
        VersionLabel = versionLabel;
    }

    public UpdateManager Manager { get; }

    public UpdateInfo UpdateInfo { get; }

    public string VersionLabel { get; }
}
