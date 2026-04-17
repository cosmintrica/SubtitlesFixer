using System.IO;
using System.Text.RegularExpressions;

namespace SubtitlesFixer.App.Subtitles;

internal sealed record VideoInfo(
    string Title,
    int? Season,
    int? Episode,
    bool IsSeries,
    string? NumericSeriesKey = null,
    int? NumericEpisodeCandidate = null)
{
    public bool HasNumericEpisodeCandidate =>
        !IsSeries &&
        !string.IsNullOrWhiteSpace(NumericSeriesKey) &&
        NumericEpisodeCandidate.HasValue;

    public VideoInfo PromoteNumericEpisode()
    {
        if (!HasNumericEpisodeCandidate)
            return this;

        return this with
        {
            Season = 1,
            Episode = NumericEpisodeCandidate,
            IsSeries = true,
        };
    }
}

/// <summary>
/// Parseaza numele unui fisier video pentru a extrage titlul, sezonul si episodul.
/// Suporta patternuri SxxExx, episod numeric simplu si film (stop la an/calitate).
/// </summary>
internal static partial class VideoNameParser
{
    // S01E05 or S01E05E06 (double-episode)
    [GeneratedRegex(@"^(?<title>.*?)[.\s_-]+S(?<s>\d{1,2})E(?<e>\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex SxxEyyRx();

    // Exemplu: "pokemon indigo league - 001 - pokemon i choose you"
    [GeneratedRegex(@"^(?<title>.*?)[.\s_-]+(?<ep>\d{1,4})(?:[.\s_-]+(?<rest>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex NumericEpisodeRx();

    // Stop at quality/year tag for movies
    [GeneratedRegex(
        @"^(?<title>.*?)[.\s_-]+(?:(?:19|20)\d{2}\b|480p|720p|1080p|2160p|WEB[- ]DL|WEBRip|BluRay|BDRip|HDRip|DVDRip|AMZN|NF|H\.264|H\.265|x264|x265|HEVC)",
        RegexOptions.IgnoreCase)]
    private static partial Regex MovieTagRx();

    [GeneratedRegex(
        @"^(?:(?:19|20)\d{2}|480p|720p|1080p|2160p|WEB[- ]DL|WEBRip|BluRay|BDRip|BRRip|HDRip|DVDRip|AMZN|NF|H\.264|H\.265|x264|x265|HEVC)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LikelyMovieTailRx();

    // ────────────────────────────────────────────────────────────────────────

    public static VideoInfo Parse(string videoFileName)
    {
        var name = Path.GetFileNameWithoutExtension(videoFileName);

        // 1. SxxExx series
        var sm = SxxEyyRx().Match(name);
        if (sm.Success)
        {
            var title = CleanTitle(sm.Groups["title"].Value);
            return new VideoInfo(title,
                Season:  int.Parse(sm.Groups["s"].Value),
                Episode: int.Parse(sm.Groups["e"].Value),
                IsSeries: true);
        }

        // 2. Numeric episodic fallback. We only mark it as a candidate here;
        // the batch scan decides later whether multiple files form a real series.
        var nm = NumericEpisodeRx().Match(name);
        if (nm.Success)
        {
            var title = CleanTitle(nm.Groups["title"].Value);
            var rest = CleanTitle(nm.Groups["rest"].Value);

            if (!string.IsNullOrWhiteSpace(title) &&
                int.TryParse(nm.Groups["ep"].Value, out var numericEpisode) &&
                !LooksLikeMovieNumber(numericEpisode, rest))
            {
                return new VideoInfo(
                    Title: title,
                    Season: null,
                    Episode: null,
                    IsSeries: false,
                    NumericSeriesKey: NormalizeKey(title),
                    NumericEpisodeCandidate: numericEpisode);
            }
        }

        // 3. Movie — stop at first quality/year marker
        var mm = MovieTagRx().Match(name);
        var movieTitle = mm.Success
            ? CleanTitle(mm.Groups["title"].Value)
            : CleanTitle(name);

        return new VideoInfo(movieTitle, Season: null, Episode: null, IsSeries: false);
    }

    private static bool LooksLikeMovieNumber(int numericEpisode, string rest)
    {
        if (numericEpisode is >= 1900 and <= 2099 && string.IsNullOrWhiteSpace(rest))
            return true;

        return !string.IsNullOrWhiteSpace(rest) && LikelyMovieTailRx().IsMatch(rest);
    }

    private static string CleanTitle(string raw)
    {
        var s = Regex.Replace(raw, @"[._]+", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '_', '.');
        return s;
    }

    private static string NormalizeKey(string raw)
    {
        var s = CleanTitle(raw);
        return s.ToLowerInvariant();
    }
}
