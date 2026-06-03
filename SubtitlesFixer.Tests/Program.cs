using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
            ("fixsubs.ps1 apply repara encoding/diacritice/markeri si pastreaza punctuatia", TestPowerShellApplyRepair),
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

    private static void TestPowerShellApplyRepair()
    {
        // Apply (reparare reala) are nevoie de dictionar. Folosim perechea
        // fixsubs.ps1 + words_ro.gz copiata langa testul compilat (nu repo root,
        // care nu are dictionarul).
        var baseDir = AppContext.BaseDirectory;
        var script = Path.Combine(baseDir, "fixsubs.ps1");
        var dict = Path.Combine(baseDir, "words_ro.gz");
        AssertTrue(File.Exists(script), "fixsubs.ps1 lipseste langa testul compilat.");
        AssertTrue(File.Exists(dict), "words_ro.gz lipseste langa testul compilat (necesar pentru repararea cu dictionar).");

        var testRoot = Path.Combine(baseDir, ".apply-tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);

        try
        {
            const char sCedilla = 'ş';   // s-cedilla (gresit) -> trebuie s-virgula (ș)
            const char tCedilla = 'ţ';   // t-cedilla (gresit) -> trebuie t-virgula (ț)
            const char aCirc = 'â';      // a circumflex
            const char repl = '�';       // replacement char (marker)
            const char backtick = '`';        // backtick (marker)

            // SRT cu LF-only, spatii in coada, si toate tipurile de probleme.
            var lines = new[]
            {
                "1", "00:00:01,000 --> 00:00:02,000",
                $"Te rog, f?r? {sCedilla}i s?n?tate.   ",          // marker ?, cedilla, marker ?, trailing spaces
                "",
                "2", "00:00:02,000 --> 00:00:03,000",
                $"Nu m-am n{backtick}scut ieri, p{repl}dure.",      // marker backtick + marker FFFD
                "",
                "3", "00:00:03,000 --> 00:00:04,000",
                $"{aCirc}nainte de toate, pune-?i haina.",          // a-circ initial -> i-circ, context -?i
                "",
                "4", "00:00:04,000 --> 00:00:05,000",
                $"To{tCedilla}i au plecat. Ce faci? Serios???",     // cedilla + pastrare ? si ???
                "",
                "5", "00:00:05,000 --> 00:00:06,000",
                "? Hello darkness my old friend ?",                 // note muzicale
                "",
            };
            var broken = string.Join("\n", lines);  // LF only
            var file = Path.Combine(testRoot, "Inception.2010.1080p.BluRay.ro.srt");
            File.WriteAllText(file, broken, new UTF8Encoding(false));

            var res = RunPowerShellApply(script, testRoot);
            AssertEqual(0, res.ExitCode, res.Output + res.Error);

            var after = File.ReadAllText(file, Encoding.UTF8);

            // --- reparari care TREBUIE sa se intample ---
            AssertTrue(after.Contains("fără"), "f?r? trebuie reparat la fara (cu a-breve).");
            AssertTrue(after.Contains("sănătate"), "s?n?tate trebuie reparat la sanatate.");
            AssertTrue(after.Contains("și "), "s-cedilla trebuie convertit la s-virgula (si).");
            AssertTrue(after.Contains("născut"), "n`scut (backtick) trebuie reparat la nascut.");
            AssertTrue(after.Contains("pădure"), "p<FFFD>dure trebuie reparat la padure.");
            AssertTrue(after.Contains("înainte"), "a circumflex initial trebuie convertit la i circumflex (inainte).");
            AssertTrue(after.Contains("pune-ți"), "pune-?i trebuie reparat la pune-ti (regula de context).");
            AssertTrue(after.Contains("Toți"), "t-cedilla trebuie convertit la t-virgula (Toti).");
            AssertTrue(after.Contains("♪"), "? la marginea liniei trebuie sa devina nota muzicala.");

            // --- punctuatie care TREBUIE pastrata ---
            AssertTrue(after.Contains("faci?"), "Semnul de intrebare final trebuie pastrat.");
            AssertTrue(after.Contains("Serios???"), "Intrebarile multiple trebuie pastrate.");

            // --- structural ---
            AssertTrue(after.Contains("\r\n"), "Fisierul trebuie sa aiba CRLF.");
            AssertFalse(Regex.IsMatch(after, "(?<!\r)\n"), "Nu trebuie sa ramana LF singur.");
            AssertFalse(Regex.IsMatch(after, "[ \t]+(?=\r?\n|\\z)"), "Spatiile in coada trebuie eliminate.");
            AssertFalse(after.Contains('ş') || after.Contains('ţ') ||
                        after.Contains('Ş') || after.Contains('Ţ'), "Nu trebuie sa ramana cedile.");
            AssertFalse(after.Contains('�'), "Nu trebuie sa ramana caractere FFFD.");
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    private static (int ExitCode, string Output, string Error) RunPowerShellApply(string script, string folder)
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
        process.StartInfo.ArgumentList.Add("-Paths");
        process.StartInfo.ArgumentList.Add(folder);

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("fixsubs.ps1 (apply) nu a terminat in 60 secunde.");
        }

        return (process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
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
