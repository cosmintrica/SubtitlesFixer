namespace SubtitlesFixer.App.Subtitles;

internal sealed class SubtitleSearchResult
{
    public required string ReleaseName   { get; init; }
    public required string Language      { get; init; }      // ISO: "ro", "en"
    public required string LanguageDisplay { get; init; }    // "Română", "Engleză"
    public required string DownloadUrl   { get; init; }      // path: /subtitle/xxx.zip
    public required string ProviderName  { get; init; }
    public          double Score         { get; init; }
    public          bool   IsHearingImpaired { get; init; }
}
