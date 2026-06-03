using System.IO;
using System.Reflection;

namespace SubtitlesFixer.App;

internal static class ScriptLocator
{
    private static string? _cachedPath;

    /// <summary>
    /// Ruleaza preferabil scriptul livrat ca resursa embedded, nu un fisier
    /// side-by-side care poate fi modificat dupa instalare. Fisierul de langa exe
    /// ramane fallback pentru build-uri incomplete sau folosire manuala.
    /// </summary>
    public static string GetScriptPath()
    {
        if (_cachedPath is not null && File.Exists(_cachedPath))
            return _cachedPath;

        var asm = Assembly.GetExecutingAssembly();
        var scriptResourceName = FindEmbeddedResourceName(asm, "fixsubs.ps1");
        if (scriptResourceName is not null)
        {
            var dir = Path.Combine(Path.GetTempPath(), "SubtitlesFixer");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "fixsubs.ps1");
            ExtractEmbeddedFile(asm, scriptResourceName, path, "fixsubs.ps1");

            var dictionaryResourceName = FindEmbeddedResourceName(asm, "words_ro.gz");
            if (dictionaryResourceName is not null)
            {
                var dictionaryPath = Path.Combine(dir, "words_ro.gz");
                ExtractEmbeddedFile(asm, dictionaryResourceName, dictionaryPath, "words_ro.gz");
            }

            _cachedPath = path;
            return _cachedPath;
        }

        var sideBySide = Path.Combine(AppContext.BaseDirectory, "fixsubs.ps1");
        if (!File.Exists(sideBySide))
            throw new InvalidOperationException(
                "Lipseste fixsubs.ps1 langa aplicatie si nici resursa embedded nu exista. Republish sau copiaza fixsubs.ps1 langa exe.");

        _cachedPath = sideBySide;
        return _cachedPath;
    }

    private static void ExtractEmbeddedFile(Assembly asm, string resourceName, string destinationPath, string displayName)
    {
        using var stream = asm.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Nu pot citi resursa {displayName}.");
        using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        stream.CopyTo(fs);
    }

    private static string? FindEmbeddedResourceName(Assembly asm, string suffix)
    {
        foreach (var n in asm.GetManifestResourceNames())
        {
            if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return n;
        }

        return null;
    }
}
