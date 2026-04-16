using System.Text.Json;

namespace SubtitlesFixer.App;

/// <summary>
/// PowerShell ConvertTo-Json poate serializa un singur element din „items” ca obiect, nu ca array.
/// </summary>
public static class FixSummaryPayloadParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static FixSummaryPayload? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var payload = new FixSummaryPayload();

        if (root.TryGetProperty("totals", out var totalsEl))
            payload.Totals = JsonSerializer.Deserialize<FixTotals>(totalsEl.GetRawText(), Options);

        if (root.TryGetProperty("items", out var itemsEl))
            payload.Items = ParseItems(itemsEl);

        return payload;
    }

    private static List<FixSummaryItem> ParseItems(JsonElement itemsEl)
    {
        switch (itemsEl.ValueKind)
        {
            case JsonValueKind.Array:
                return JsonSerializer.Deserialize<List<FixSummaryItem>>(itemsEl.GetRawText(), Options) ?? new List<FixSummaryItem>();
            case JsonValueKind.Object:
            {
                var one = JsonSerializer.Deserialize<FixSummaryItem>(itemsEl.GetRawText(), Options);
                return one == null ? new List<FixSummaryItem>() : new List<FixSummaryItem> { one };
            }
            default:
                return new List<FixSummaryItem>();
        }
    }
}
