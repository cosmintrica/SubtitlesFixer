using System.Text;
using System.Reflection;

// Load the assembly
var asm = Assembly.LoadFrom(@"SubtitlesFixer.App\bin\Release\net8.0-windows\SubtitlesFixer.App.dll");
var normType = asm.GetType("SubtitlesFixer.App.Subtitles.SubtitleNormalizer");

// Read file bytes
var bytes = File.ReadAllBytes(@"ignore for tests\Power Book III_ Raising Kanan - 04x03 - Bygones.FENiX.Romanian.srt");

// Call DecodeBytes
var decodeMethod = normType.GetMethod("DecodeBytes", BindingFlags.Public | BindingFlags.Static);
var decoded = (string)decodeMethod.Invoke(null, new object[] { bytes });

// Call Normalize
var normalizeMethod = normType.GetMethod("Normalize", BindingFlags.Public | BindingFlags.Static);
var normalized = (string)normalizeMethod.Invoke(null, new object[] { decoded });

// Count remaining FFFD
var fffdCount = normalized.Count(c => c == '\uFFFD');
var qmarkCount = 0;
for (int i = 0; i < normalized.Length; i++)
{
    if (normalized[i] == '?' && i > 0 && char.IsLetter(normalized[i-1]) && i < normalized.Length-1 && char.IsLetter(normalized[i+1]))
        qmarkCount++;
}

Console.WriteLine($"FFFD remaining: {fffdCount}");
Console.WriteLine($"? between letters remaining: {qmarkCount}");

// Show first 80 lines
var lines = normalized.Split('\n');
for (int i = 0; i < Math.Min(120, lines.Length); i++)
    Console.WriteLine(lines[i].TrimEnd('\r'));
