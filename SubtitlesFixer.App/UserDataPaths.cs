using System;
using System.IO;

namespace SubtitlesFixer.App;

/// <summary>
/// Alege locatia pentru settings.json / last-run.json:
///  - "portable": lânga exe (subfolder "data"), daca exista fisierul "portable.flag"
///     sau ".portable" langa executabil. Util pentru arhiva portabila.
///  - altfel: %APPDATA%\SubtitlesFixer (comportament clasic pentru Setup / Velopack).
///
/// Rezultatul este cached la primul apel.
/// </summary>
internal static class UserDataPaths
{
    private static readonly Lazy<string> _rootDir = new(ResolveRootDir);

    public static string RootDirectory => _rootDir.Value;

    public static string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public static string LastRunFile => Path.Combine(RootDirectory, "last-run.json");

    public static bool IsPortable { get; private set; }

    private static string ResolveRootDir()
    {
        try
        {
            var exeDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(exeDir))
            {
                var flag1 = Path.Combine(exeDir, "portable.flag");
                var flag2 = Path.Combine(exeDir, ".portable");
                if (File.Exists(flag1) || File.Exists(flag2))
                {
                    IsPortable = true;
                    return Path.Combine(exeDir, "data");
                }
            }
        }
        catch
        {
            // Ignorat — pe orice eroare cadem pe calea standard.
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SubtitlesFixer");
    }
}
