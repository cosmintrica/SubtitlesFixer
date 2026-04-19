using System.IO;
using System.Text.Json;

namespace SubtitlesFixer.App;

internal static class LastRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string LastRunPath => UserDataPaths.LastRunFile;

    public static FixSummaryPayload? Load()
    {
        try
        {
            if (!File.Exists(LastRunPath))
                return null;

            var json = File.ReadAllText(LastRunPath);
            return FixSummaryPayloadParser.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(FixSummaryPayload payload)
    {
        var dir = Path.GetDirectoryName(LastRunPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(LastRunPath, JsonSerializer.Serialize(payload, JsonOptions));
    }
}
