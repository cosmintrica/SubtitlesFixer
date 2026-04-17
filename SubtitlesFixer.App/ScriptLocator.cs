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
        var sideBySideDictionary = Path.Combine(AppContext.BaseDirectory, "words_ro.gz");
        if (File.Exists(sideBySide) && File.Exists(sideBySideDictionary))
        {
            _cachedPath = sideBySide;
            return _cachedPath;
        }

        var asm = Assembly.GetExecutingAssembly();
        var scriptResourceName = FindEmbeddedResourceName(asm, "fixsubs.ps1");
        if (scriptResourceName is null)
        {
            if (File.Exists(sideBySide))
            {
                _cachedPath = sideBySide;
                return _cachedPath;
            }

            throw new InvalidOperationException(
                "Lipsește fixsubs.ps1 lângă aplicație și nici resursă încorporată. Republish sau copiază fixsubs.ps1 lângă exe.");
        }

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
