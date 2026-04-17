namespace SubtitlesFixer.App.Subtitles;

internal interface ISubtitleProvider
{
    string Name         { get; }
    bool   IsConfigured { get; }

    Task<IReadOnlyList<SubtitleSearchResult>> SearchAsync(
        SubtitleSearchQuery query,
        CancellationToken   ct = default);
}
