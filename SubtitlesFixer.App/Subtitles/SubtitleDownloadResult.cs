namespace SubtitlesFixer.App.Subtitles;

internal sealed class SubtitleDownloadResult
{
    public required bool    Success            { get; init; }
    public          string? DownloadedFileName { get; init; }
    public          string? NormalizedText     { get; init; }
    public          string? ErrorMessage       { get; init; }
}
