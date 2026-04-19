using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SubtitlesFixer.App.Subtitles;

internal static class StandaloneSubtitleRepairer
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static async Task<FixPlanPayload> AnalyzeFolderAsync(
        string folder,
        bool recurse,
        IProgress<SubtitleRepairProgress>? progress = null,
        CancellationToken ct = default)
    {
        var files = EnumerateSubtitleFiles(folder, recurse).ToList();
        var items = new List<FixPlanItem>(files.Count);
        var ready = 0;
        var review = 0;
        var warn = 0;
        var err = 0;
        var index = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            progress?.Report(new SubtitleRepairProgress(index, files.Count, Path.GetFileName(file)));

            try
            {
                var item = await AnalyzeFileAsync(file, ct).ConfigureAwait(false);
                items.Add(item);

                switch ((item.Status ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "review":
                        review++;
                        break;
                    case "warn":
                        warn++;
                        break;
                    case "error":
                        err++;
                        break;
                    default:
                        ready++;
                        break;
                }
            }
            catch (Exception ex)
            {
                items.Add(BuildErrorPlanItem(file, ex.Message));
                err++;
            }
        }

        return new FixPlanPayload
        {
            Totals = new FixPlanTotals
            {
                Ready = ready,
                Review = review,
                Warn = warn,
                Err = err,
            },
            Items = items,
        };
    }

    public static async Task<FixSummaryPayload> RepairFolderAsync(
        string folder,
        bool recurse,
        IProgress<SubtitleRepairProgress>? progress = null,
        CancellationToken ct = default)
    {
        var files = EnumerateSubtitleFiles(folder, recurse).ToList();

        var items = new List<FixSummaryItem>(files.Count);
        var ok = 0;
        var warn = 0;
        var err = 0;
        var index = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            progress?.Report(new SubtitleRepairProgress(index, files.Count, Path.GetFileName(file)));

            try
            {
                var item = await RepairFileAsync(file, ct).ConfigureAwait(false);
                items.Add(item);

                switch ((item.Status ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "warn":
                        warn++;
                        break;
                    case "error":
                        err++;
                        break;
                    default:
                        ok++;
                        break;
                }
            }
            catch (Exception ex)
            {
                items.Add(new FixSummaryItem
                {
                    VideoName = Path.GetFileName(file),
                    VideoPath = file,
                    TargetPath = file,
                    SubtitleBefore = Path.GetFileName(file),
                    SubtitleAfter = Path.GetFileName(file),
                    Status = "error",
                    Message = ex.Message,
                    RootPath = Path.GetDirectoryName(file),
                });
                err++;
            }
        }

        return new FixSummaryPayload
        {
            Totals = new FixTotals
            {
                Ok = ok,
                Warn = warn,
                Err = err,
            },
            Items = items,
        };
    }

    private static IEnumerable<string> EnumerateSubtitleFiles(string folder, bool recurse)
    {
        return Directory
            .EnumerateFiles(folder, "*.srt", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "backup" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<FixPlanItem> AnalyzeFileAsync(string path, CancellationToken ct)
    {
        var analysis = await AnalyzeContentAsync(path, ct).ConfigureAwait(false);
        var meta = GetMetadata(path);

        return new FixPlanItem
        {
            ItemMode = "subtitle-only",
            Season = meta.Season,
            Episode = meta.Episode,
            VideoName = Path.GetFileName(path),
            VideoPath = path,
            TargetName = Path.GetFileName(path),
            TargetPath = path,
            ExistingTarget = true,
            SelectedSubtitleName = string.Empty,
            SelectedSubtitlePath = string.Empty,
            SelectionMode = "none",
            Action = analysis.Changed ? "repair" : "already-ok",
            Status = "ready",
            Message = analysis.Changed
                ? "Voi repara subtitrarea in acelasi fisier si voi muta originalul in backup."
                : "Subtitrarea este deja curata. Nu schimb nimic aici.",
            SubtitleCount = 0,
            Candidates = [],
        };
    }

    private static async Task<FixSummaryItem> RepairFileAsync(string path, CancellationToken ct)
    {
        var analysis = await AnalyzeContentAsync(path, ct).ConfigureAwait(false);
        var meta = GetMetadata(path);

        if (!analysis.Changed)
        {
            return new FixSummaryItem
            {
                ItemMode = "subtitle-only",
                Season = meta.Season,
                Episode = meta.Episode,
                VideoName = Path.GetFileName(path),
                VideoPath = path,
                TargetPath = path,
                SubtitleBefore = Path.GetFileName(path),
                SubtitleAfter = Path.GetFileName(path),
                EncodingDetected = "Nicio schimbare necesara",
                Status = "ok",
                Message = "Subtitrarea era deja curata. Nu am schimbat nimic.",
                RootPath = Path.GetDirectoryName(path),
            };
        }

        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("Fisierul nu are director parinte.");
        var backupDirectory = Path.Combine(directory, "backup");
        Directory.CreateDirectory(backupDirectory);

        var backupPath = CreateUniquePath(Path.Combine(backupDirectory, Path.GetFileName(path)));

        File.Move(path, backupPath);
        try
        {
            await File.WriteAllTextAsync(path, analysis.NormalizedText, Utf8NoBom, ct).ConfigureAwait(false);
        }
        catch
        {
            if (File.Exists(path))
                File.Delete(path);

            File.Move(backupPath, path);
            throw;
        }

        return new FixSummaryItem
        {
            ItemMode = "subtitle-only",
            Season = meta.Season,
            Episode = meta.Episode,
            VideoName = Path.GetFileName(path),
            VideoPath = path,
            TargetPath = path,
            SubtitleBefore = Path.GetFileName(path),
            SubtitleAfter = Path.GetFileName(path),
            ReplacedTargetBackupPath = backupPath,
            EncodingDetected = "Reparata si rescrisa UTF-8",
            Status = "ok",
            Message = "Subtitrarea a fost reparata in loc. Originalul a fost mutat in backup.",
            RootPath = directory,
        };
    }

    private static async Task<StandaloneFileAnalysis> AnalyzeContentAsync(string path, CancellationToken ct)
    {
        var originalBytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var decoded = SubtitleNormalizer.DecodeBytes(originalBytes);
        var normalized = SubtitleNormalizer.Normalize(decoded);
        var normalizedBytes = Utf8NoBom.GetBytes(normalized);

        return new StandaloneFileAnalysis(
            Changed: !originalBytes.AsSpan().SequenceEqual(normalizedBytes),
            NormalizedText: normalized);
    }

    private static (string Season, string Episode) GetMetadata(string path)
    {
        var info = VideoNameParser.Parse(Path.GetFileName(path));
        if (info.HasNumericEpisodeCandidate)
            info = info.PromoteNumericEpisode();

        if (info.IsSeries && info.Season.HasValue && info.Episode.HasValue)
        {
            return ($"S{info.Season.Value:00}", $"S{info.Season.Value:00}E{info.Episode.Value:00}");
        }

        return ("Nesazonat", string.Empty);
    }

    private static FixPlanItem BuildErrorPlanItem(string path, string message)
    {
        var meta = GetMetadata(path);
        return new FixPlanItem
        {
            ItemMode = "subtitle-only",
            Season = meta.Season,
            Episode = meta.Episode,
            VideoName = Path.GetFileName(path),
            VideoPath = path,
            TargetName = Path.GetFileName(path),
            TargetPath = path,
            ExistingTarget = true,
            SelectedSubtitleName = string.Empty,
            SelectedSubtitlePath = string.Empty,
            SelectionMode = "none",
            Action = "none",
            Status = "error",
            Message = message,
            SubtitleCount = 0,
            Candidates = [],
        };
    }

    private static string CreateUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("Calea nu are director parinte.");
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var index = 1;

        while (true)
        {
            var candidate = Path.Combine(directory, $"{fileName}.{index}{extension}");
            if (!File.Exists(candidate))
                return candidate;

            index++;
        }
    }
}

internal readonly record struct SubtitleRepairProgress(int Current, int Total, string Label);
internal readonly record struct StandaloneFileAnalysis(bool Changed, string NormalizedText);
