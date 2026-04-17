using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SubtitlesFixer.App.Subtitles;

/// <summary>
/// Provider SubDL — https://subdl.com
/// API gratuita cu cheie personala (2000 req/zi).
/// </summary>
internal sealed class SubDLProvider : ISubtitleProvider
{
    // Doua HttpClient-uri statice => o singura instanta per proces, evita socket exhaustion
    private static readonly HttpClient SearchHttp = new()
    {
        BaseAddress = new Uri("https://api.subdl.com/"),
        Timeout     = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders = { { "User-Agent", "SubtitlesFixer/1.2.0" } },
    };

    private static readonly HttpClient DownloadHttp = new()
    {
        BaseAddress = new Uri("https://dl.subdl.com/"),
        Timeout     = TimeSpan.FromSeconds(60),
        DefaultRequestHeaders = { { "User-Agent", "SubtitlesFixer/1.2.0" } },
    };

    // ────────────────────────────────────────────────────────────────────────

    private readonly string _apiKey;

    public string Name         => "SubDL";
    public bool   IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public SubDLProvider(string? apiKey) => _apiKey = apiKey?.Trim() ?? string.Empty;

    // ────────────────────────────────────────────────────────────────────────
    // Search
    // ────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SubtitleSearchResult>> SearchAsync(
        SubtitleSearchQuery query,
        CancellationToken   ct = default)
    {
        if (!IsConfigured) return [];

        var langs = string.Join(",", query.Languages);
        var url   = BuildSearchUrl(query, langs);

        SubDLResponse? response;
        try
        {
            response = await SearchHttp.GetFromJsonAsync<SubDLResponse>(url, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Cheia SubDL este invalida sau expirata. Verifica setarile.", ex);
        }

        if (response?.Status != true)
            return [];

        // API poate intoarce subtitrarile fie la nivel top-level (subtitles[])
        // fie in results[].subtitles[] — gestionam ambele variante
        var allSubs = new List<SubDLSubtitle>();

        if (response.Subtitles is { Count: > 0 })
            allSubs.AddRange(response.Subtitles);

        if (response.Results is { Count: > 0 })
            foreach (var r in response.Results)
                if (r.Subtitles is { Count: > 0 })
                    allSubs.AddRange(r.Subtitles);

        var results = allSubs
            .Where(s => !string.IsNullOrWhiteSpace(s.Url))
            .Select(s => MapResult(s, query))
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.IsHearingImpaired)
            .ToList();

        return results;
    }

    private string BuildSearchUrl(SubtitleSearchQuery q, string langs)
    {
        var sb = new System.Text.StringBuilder(
            $"api/v1/subtitles?api_key={Uri.EscapeDataString(_apiKey)}" +
            $"&film_name={Uri.EscapeDataString(q.Title)}" +
            $"&languages={Uri.EscapeDataString(langs)}");

        if (q.IsSeries)
        {
            sb.Append($"&type=tv&season_number={q.Season}&episode_number={q.Episode}");
        }
        else
        {
            sb.Append("&type=movie");
        }

        return sb.ToString();
    }

    private SubtitleSearchResult MapResult(SubDLSubtitle s, SubtitleSearchQuery q)
    {
        var lang = s.Lang?.Trim().ToLowerInvariant() ?? string.Empty;
        var name = s.Name ?? string.Empty;
        // Sterge extensia .zip din numele afisat (vine din API)
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return new SubtitleSearchResult
        {
            ReleaseName     = name,
            Language        = lang,
            LanguageDisplay = LangDisplay(lang),
            DownloadUrl     = s.Url!,
            ProviderName    = Name,
            Score           = ComputeScore(s, q),
            IsHearingImpaired = s.Hi,
        };
    }

    private static double ComputeScore(SubDLSubtitle s, SubtitleSearchQuery q)
    {
        double score = 0;

        // Preferinta limba in ordinea din query
        var langList = q.Languages.Select(l => l.ToLowerInvariant()).ToList();
        var idx      = langList.IndexOf(s.Lang?.ToLowerInvariant() ?? string.Empty);
        if (idx >= 0)
            score += (langList.Count - idx) * 100;

        if (!s.Hi)      score += 5;
        if (s.Rating.HasValue) score += Math.Min(s.Rating.Value, 10);

        return score;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Download
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Descarca zip-ul, extrage primul .srt util si intoarce textul normalizat.
    /// Persistenta pe disk se face in fereastra UI, unde exista logica de backup/restore.
    /// </summary>
    public static async Task<SubtitleDownloadResult> DownloadAsync(
        SubtitleSearchResult result,
        CancellationToken    ct = default)
    {
        try
        {
            // URL-ul vine ca "/subtitle/xxx.zip" — taiem slash-ul initial
            var relUrl   = result.DownloadUrl.TrimStart('/');
            var zipBytes = await DownloadHttp.GetByteArrayAsync(relUrl, ct);

            using var zipStream = new MemoryStream(zipBytes);
            using var archive   = new ZipArchive(zipStream, ZipArchiveMode.Read);

            // Alegem SRT-ul potrivit din zip (cel mai scurt nume = cel mai generic)
            var srtEntry = archive.Entries
                .Where(e => e.Name.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Name.Length)
                .FirstOrDefault();

            if (srtEntry is null)
                return new SubtitleDownloadResult
                {
                    Success = false,
                    ErrorMessage = "Fisierul zip nu contine niciun .srt.",
                };

            await using var entryStream = srtEntry.Open();
            using var       mem         = new MemoryStream();
            await entryStream.CopyToAsync(mem, ct);

            var srtBytes   = mem.ToArray();
            var decoded    = SubtitleNormalizer.DecodeBytes(srtBytes);
            var normalized = SubtitleNormalizer.Normalize(decoded);

            return new SubtitleDownloadResult
            {
                Success = true,
                DownloadedFileName = srtEntry.Name,
                NormalizedText = normalized,
            };
        }
        catch (Exception ex)
        {
            return new SubtitleDownloadResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    public static string LangDisplay(string code) => code switch
    {
        "ro" => "Română",
        "en" => "Engleză",
        "fr" => "Franceză",
        "de" => "Germană",
        "es" => "Spaniolă",
        "it" => "Italiană",
        "hu" => "Maghiară",
        "pl" => "Polonă",
        "pt" => "Portugheză",
        "nl" => "Olandeză",
        "ru" => "Rusă",
        "tr" => "Turcă",
        _    => code.ToUpperInvariant(),
    };

    // ────────────────────────────────────────────────────────────────────────
    // JSON DTOs
    // ────────────────────────────────────────────────────────────────────────

    private sealed class SubDLResponse
    {
        [JsonPropertyName("status")]    public bool               Status    { get; init; }
        [JsonPropertyName("subtitles")] public List<SubDLSubtitle>? Subtitles { get; init; }
        [JsonPropertyName("results")]   public List<SubDLResult>?  Results   { get; init; }
    }

    private sealed class SubDLResult
    {
        [JsonPropertyName("subtitles")] public List<SubDLSubtitle>? Subtitles { get; init; }
    }

    private sealed class SubDLSubtitle
    {
        [JsonPropertyName("name")]     public string? Name     { get; init; }
        [JsonPropertyName("lang")]     public string? Lang     { get; init; }
        [JsonPropertyName("language")] public string? Language { get; init; }
        [JsonPropertyName("url")]      public string? Url      { get; init; }
        [JsonPropertyName("hi")]       public bool    Hi       { get; init; }
        [JsonPropertyName("rating")]   public double? Rating   { get; init; }
    }
}
