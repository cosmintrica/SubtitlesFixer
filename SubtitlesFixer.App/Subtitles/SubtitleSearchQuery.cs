namespace SubtitlesFixer.App.Subtitles;

internal sealed class SubtitleSearchQuery
{
    public required string                Title     { get; init; }
    public          int?                  Season    { get; init; }
    public          int?                  Episode   { get; init; }
    public required IReadOnlyList<string> Languages { get; init; }   // ["ro","en"]
    public          bool   IsSeries => Season.HasValue && Episode.HasValue;
}
