using System.Text.Json.Serialization;

namespace SubtitlesFixer.App;

public sealed class FixPlanPayload
{
    [JsonPropertyName("totals")]
    public FixPlanTotals? Totals { get; set; }

    [JsonPropertyName("items")]
    public List<FixPlanItem>? Items { get; set; }
}

public sealed class FixPlanTotals
{
    [JsonPropertyName("ready")]
    public int Ready { get; set; }

    [JsonPropertyName("review")]
    public int Review { get; set; }

    [JsonPropertyName("warn")]
    public int Warn { get; set; }

    [JsonPropertyName("err")]
    public int Err { get; set; }
}

public sealed class FixPlanItem
{
    public string? Season { get; set; }
    public string? Episode { get; set; }
    public string? VideoName { get; set; }
    public string? VideoPath { get; set; }
    public string? TargetName { get; set; }
    public string? TargetPath { get; set; }
    public bool ExistingTarget { get; set; }
    public string? SelectedSubtitleName { get; set; }
    public string? SelectedSubtitlePath { get; set; }
    public string? SelectionMode { get; set; }
    public string? Action { get; set; }
    public string? Status { get; set; }
    public string? Message { get; set; }
    public List<FixPlanCandidate>? Candidates { get; set; }
}

public sealed class FixPlanCandidate
{
    public string? Name { get; set; }
    public string? Path { get; set; }
}
