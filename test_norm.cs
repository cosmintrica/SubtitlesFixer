using System;
using SubtitlesFixer.App.Subtitles;

class Program {
    static void Main() {
        string t1 = "Totu?i, de ce ai le?inat?";
        Console.WriteLine(SubtitleNormalizer.Normalize(t1));
        string t2 = "Mul?umesc, unchiule Marvin.";
        Console.WriteLine(SubtitleNormalizer.Normalize(t2));
    }
}
