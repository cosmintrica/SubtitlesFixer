using System.IO;
using System.IO.Compression;
using System.Text;

namespace SubtitlesFixer.App.Subtitles;

/// <summary>
/// Dictionar de ~960K cuvinte romanesti (forme flexionate din Hunspell ro_RO).
/// Incarcat lazy din resursa embedded comprimata cu GZip.
/// </summary>
internal static class RomanianDictionary
{
    private static readonly Lazy<HashSet<string>> _words = new(LoadDictionary);

    /// <summary>Verifica daca un cuvant exista in dictionar (case-insensitive).</summary>
    public static bool Contains(string word) => _words.Value.Contains(word);

    /// <summary>Numarul total de cuvinte din dictionar.</summary>
    public static int Count => _words.Value.Count;

    private static HashSet<string> LoadDictionary()
    {
        var asm = typeof(RomanianDictionary).Assembly;
        using var stream = asm.GetManifestResourceStream("SubtitlesFixer.App.words_ro.gz")
            ?? throw new InvalidOperationException("Embedded resource 'words_ro.gz' not found.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                words.Add(trimmed);
        }

        return words;
    }
}
