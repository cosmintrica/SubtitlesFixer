using System.IO;
using System.Reflection;

namespace SubtitlesFixer.App;

internal static class ScriptLocator
{
    private static string? _cachedPath;

    /// <summary>
    /// Preferă fixsubs.ps1 lângă exe (ex. publish folder). Altfel extrage din resursă încorporată (ex. single-file).
    /// </summary>
    public static string GetScriptPath()
    {
        if (_cachedPath is not null && File.Exists(_cachedPath))
            return _cachedPath;

        var sideBySide = Path.Combine(AppContext.BaseDirectory, "fixsubs.ps1");
        if (File.Exists(sideBySide))
        {
            _cachedPath = sideBySide;
            return _cachedPath;
        }

        var asm = Assembly.GetExecutingAssembly();
        var resourceName = FindEmbeddedScriptName(asm);
        if (resourceName is null)
            throw new InvalidOperationException(
                "Lipsește fixsubs.ps1 lângă aplicație și nici resursă încorporată. Republish sau copiază fixsubs.ps1 lângă exe.");

        var dir = Path.Combine(Path.GetTempPath(), "SubtitlesFixer");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "fixsubs.ps1");
        using (var stream = asm.GetManifestResourceStream(resourceName)
               ?? throw new InvalidOperationException("Nu pot citi resursa fixsubs.ps1."))
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            stream.CopyTo(fs);
        }

        _cachedPath = path;
        return _cachedPath;
    }

    private static string? FindEmbeddedScriptName(Assembly asm)
    {
        foreach (var n in asm.GetManifestResourceNames())
        {
            if (n.EndsWith("fixsubs.ps1", StringComparison.OrdinalIgnoreCase))
                return n;
        }

        return null;
    }
}
