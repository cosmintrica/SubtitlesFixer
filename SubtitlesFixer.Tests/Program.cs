using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SubtitlesFixer.App.Subtitles;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Body)[]
        {
            ("VideoNameParser classifica anii de film ca filme", TestVideoNameParser),
            ("SubtitleNormalizer pastreaza intrebarea finala ca punctuatie", TestSubtitleNormalizerQuestionPunctuation),
            ("fixsubs.ps1 preview clasifica filme/seriale si termina progresul", TestPowerShellPreview),
        };

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Body();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}");
                Console.Error.WriteLine(ex);
            }
        }

        return failed == 0 ? 0 : 1;
    }

    private static void TestVideoNameParser()
    {
        var movie = VideoNameParser.Parse("Boiler.Room.2000.1080p.BluRay.DTS.x264-decibeL.mkv");
        AssertFalse(movie.IsSeries, "Boiler.Room.2000 trebuie sa fie film, nu serial.");
        AssertEqual("Boiler Room", movie.Title, "Titlul filmului trebuie curatat pana la anul filmului.");

        var series = VideoNameParser.Parse("Some.Show.S02E03.1080p.WEB-DL.mkv");
        AssertTrue(series.IsSeries, "S02E03 trebuie sa fie serial.");
        AssertEqual(2, series.Season, "Sezon gresit.");
        AssertEqual(3, series.Episode, "Episod gresit.");

        var numeric = VideoNameParser.Parse("Pokemon Indigo League - 001 - Pokemon I Choose You.mkv");
        AssertTrue(numeric.HasNumericEpisodeCandidate, "Episodul numeric trebuie pastrat ca fallback candidat.");
    }

    private static void TestSubtitleNormalizerQuestionPunctuation()
    {
        var input = "Salut?\r\nCe faci?\r\n";
        var output = SubtitleNormalizer.Normalize(input);
        AssertTrue(output.Contains("Salut?"), "Intrebarea de la final de cuvant nu trebuie reparata ca diacritica.");
        AssertTrue(output.Contains("faci?"), "Semnul de intrebare final trebuie pastrat.");
    }

    private static void TestPowerShellPreview()
    {
        var root = FindRepoRoot();
        var script = Path.Combine(root, "fixsubs.ps1");
        var testRoot = Path.Combine(root, ".test-tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);

        try
        {
            WriteText(Path.Combine(testRoot, "Show.S01E01.mkv"), string.Empty);
            WriteText(Path.Combine(testRoot, "Show.S01E01.srt"), "1\r\n00:00:01,000 --> 00:00:02,000\r\nSalut?\r\n");
            WriteText(Path.Combine(testRoot, "The.Matrix.1999.1080p.BluRay.mkv"), string.Empty);
            WriteText(Path.Combine(testRoot, "The.Matrix.1999.srt"), "1\r\n00:00:01,000 --> 00:00:02,000\r\nBuna.\r\n");

            var boilerDir = Path.Combine(testRoot, "Boiler.Room.2000.1080p.BluRay.DTS.x264-decibeL");
            Directory.CreateDirectory(boilerDir);
            WriteText(
                Path.Combine(boilerDir, "Boiler.Room.2000.1080p.BluRay.DTS.x264-decibeL.ro.srt"),
                "1\r\n00:00:01,000 --> 00:00:02,000\r\nCe faci?\r\n");

            var previewPath = Path.Combine(testRoot, "preview.json");
            var result = RunPowerShell(script, testRoot, previewPath);
            AssertEqual(0, result.ExitCode, result.Output + result.Error);
            AssertTrue(result.Output.Contains("__SF_PROGRESS__|3|3|", StringComparison.Ordinal), "Progresul final trebuie sa fie 3/3.");

            using var doc = JsonDocument.Parse(File.ReadAllText(previewPath, Encoding.UTF8));
            var items = doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
            AssertEqual(3, items.Length, "Preview-ul trebuie sa contina 3 item-uri.");

            var show = FindItem(items, "Show.S01E01.mkv");
            AssertEqual("series", show.GetProperty("mediaKind").GetString(), "Show trebuie sa fie serial.");
            AssertEqual("S01E01", show.GetProperty("episode").GetString(), "Episod serial gresit.");

            var matrix = FindItem(items, "The.Matrix.1999.1080p.BluRay.mkv");
            AssertEqual("film", matrix.GetProperty("mediaKind").GetString(), "Matrix trebuie sa fie film.");
            AssertEqual(string.Empty, matrix.GetProperty("episode").GetString(), "Filmul nu trebuie sa aiba episod.");

            var boiler = FindItem(items, "Boiler.Room.2000.1080p.BluRay.DTS.x264-decibeL.ro.srt");
            AssertEqual("film", boiler.GetProperty("mediaKind").GetString(), "Subtitrarea standalone cu an de film trebuie sa fie film.");
            AssertEqual(string.Empty, boiler.GetProperty("episode").GetString(), "Anul filmului nu trebuie sa devina E2000.");
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    private static JsonElement FindItem(JsonElement[] items, string videoName)
    {
        foreach (var item in items)
        {
            if (string.Equals(item.GetProperty("videoName").GetString(), videoName, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        throw new InvalidOperationException("Nu am gasit item-ul " + videoName);
    }

    private static (int ExitCode, string Output, string Error) RunPowerShell(string script, string folder, string previewPath)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(script);
        process.StartInfo.ArgumentList.Add("-NoPause");
        process.StartInfo.ArgumentList.Add("-PreviewOnly");
        process.StartInfo.ArgumentList.Add("-Paths");
        process.StartInfo.ArgumentList.Add(folder);
        process.StartInfo.ArgumentList.Add("-PreviewJsonPath");
        process.StartInfo.ArgumentList.Add(previewPath);

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("fixsubs.ps1 nu a terminat in 30 secunde.");
        }

        return (process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "fixsubs.ps1")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Nu am gasit root-ul repo-ului.");
    }

    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.UTF8);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Asteptat: {expected}; primit: {actual}.");
    }
}
