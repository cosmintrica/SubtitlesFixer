using System.Text.Json.Serialization;

namespace SubtitlesFixer.App;

public sealed class FixSummaryPayload
{
    [JsonPropertyName("totals")]
    public FixTotals? Totals { get; set; }

    [JsonPropertyName("items")]
    public List<FixSummaryItem>? Items { get; set; }
}

public sealed class FixTotals
{
    [JsonPropertyName("ok")]
    public int Ok { get; set; }

    [JsonPropertyName("warn")]
    public int Warn { get; set; }

    [JsonPropertyName("err")]
    public int Err { get; set; }
}

public sealed class FixSummaryItem
{
    public string? Season { get; set; }
    public string? Episode { get; set; }
    public string? VideoName { get; set; }
    public string? VideoPath { get; set; }
    public string? SubtitleBefore { get; set; }
    public string? SubtitleAfter { get; set; }
    public string? EncodingDetected { get; set; }
    public string? BackupPath { get; set; }
    public string? SourceOriginalPath { get; set; }
    public string? SourceBackupPath { get; set; }
    public string? TargetPath { get; set; }
    public string? ReplacedTargetBackupPath { get; set; }
    public string? Status { get; set; }
    public string? Message { get; set; }
    public string? RootPath { get; set; }

    [JsonIgnore]
    public bool IsSelectedForRestore { get; set; }

    public string? RestoreStatus { get; set; }
    public string? RestoreMessage { get; set; }
}
