using System.Text.Json;

namespace SubtitlesFixer.App;

public static class FixPlanPayloadParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static FixPlanPayload? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var payload = new FixPlanPayload();

        if (root.TryGetProperty("totals", out var totalsEl))
            payload.Totals = JsonSerializer.Deserialize<FixPlanTotals>(totalsEl.GetRawText(), Options);

        if (root.TryGetProperty("items", out var itemsEl))
            payload.Items = ParseItems(itemsEl);

        return payload;
    }

    private static List<FixPlanItem> ParseItems(JsonElement itemsEl)
    {
        return itemsEl.ValueKind switch
        {
            JsonValueKind.Array => JsonSerializer.Deserialize<List<FixPlanItem>>(itemsEl.GetRawText(), Options) ?? new List<FixPlanItem>(),
            JsonValueKind.Object => JsonSerializer.Deserialize<FixPlanItem>(itemsEl.GetRawText(), Options) is { } one
                ? new List<FixPlanItem> { one }
                : new List<FixPlanItem>(),
            _ => new List<FixPlanItem>(),
        };
    }
}
