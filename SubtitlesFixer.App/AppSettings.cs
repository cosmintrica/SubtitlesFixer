using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace SubtitlesFixer.App;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsPath => UserDataPaths.SettingsFile;

    // ── Setari existente ────────────────────────────────────────────────────
    public string? LastFolderPath    { get; set; }
    public bool    IncludeSubfolders { get; set; } = true;

    /// <summary>Salvează copia veche .ro.srt în backup și refacă din sursa .srt.</summary>
    public bool OverwriteExistingRo  { get; set; }

    // ── Setari subtitrari online ─────────────────────────────────────────────
    /// <summary>Cheia API personala SubDL. Obtinuta gratuit de pe subdl.com/panel/api</summary>
    public string? SubDLApiKey { get; set; }

    /// <summary>
    /// Limbile preferate in ordine de prioritate.
    /// Prima din lista = prioritara. Ex: ["ro","en"] cauta intai romana, apoi engleza ca fallback.
    /// </summary>
    public List<string> PreferredLanguages { get; set; } = new List<string> { "ro", "en" };

    /// <summary>Ultima verificare automata de update, pentru a evita interogari prea dese.</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    // ────────────────────────────────────────────────────────────────────────

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            var s = JsonSerializer.Deserialize<AppSettings>(json);
            return s ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
