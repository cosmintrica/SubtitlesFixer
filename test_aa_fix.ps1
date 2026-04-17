# Test the ăă fix using fixsubs.ps1 functions
# Dot-source only the function definitions (lines 1-939)

$scriptPath = "c:\Users\cosmi\Desktop\Proiecte\Subtitles fixer\fixsubs.ps1"
$scriptLines = Get-Content $scriptPath
# Extract lines 1-939 (function definitions only)
$funcCode = ($scriptLines[0..938]) -join "`n"
Invoke-Expression $funcCode

# Decode the test file
$backupPath = "c:\Users\cosmi\Desktop\Proiecte\Subtitles fixer\ignore for tests\backup\Power Book III_ Raising Kanan - 04x03 - Bygones.FENiX.Romanian.srt"
$bytes = [System.IO.File]::ReadAllBytes($backupPath)
$res = Decode-Best $bytes
Write-Host "Encoding: $($res.Name), Length: $($res.Text.Length)"

# Normalize
$result = Normalize-RO $res.Text
$lines = $result -split "`r?`n"
Write-Host "Result lines: $($lines.Count)"

# Check for ăă and șă
$aa = [string]([char]0x0103) + [string]([char]0x0103)
$sa = [string]([char]0x0219) + [string]([char]0x0103)

Write-Host "`n=== Lines with aa or sa ==="
foreach ($i in 0..($lines.Count-1)) {
  $l = $lines[$i]
  if ($l.Contains($aa)) { Write-Host "L$($i+1) [aa]: $l" }
  if ($l.Contains($sa)) { Write-Host "L$($i+1) [sa]: $l" }
}

# Entry 838 area
Write-Host "`n=== Entry 838 ==="
for ($i = 3625; $i -le 3635; $i++) {
  if ($i -lt $lines.Count) { Write-Host "L$($i+1): $($lines[$i])" }
}

# Key lines check
Write-Host "`n=== Key lines ==="
$keyChecks = @(
  @{L=3; T="Creșterea prețurilor"},
  @{L=11; T="Nu puteți pur și simplu"},
  @{L=270; T="Armata te ține"},
  @{L=405; T="Credeam că știi"},
  @{L=528; T="față de țara noastră"},
  @{L=637; T="Îți dezamăgești țara"}
)
foreach ($c in $keyChecks) {
  $idx = $c.L - 1
  if ($idx -lt $lines.Count) {
    $ok = $lines[$idx].Contains($c.T)
    Write-Host "L$($c.L): $(if($ok){'OK'}else{'FAIL'}) - $($lines[$idx])"
  }
}
[System.Reflection.Assembly]::LoadFrom("c:\Users\cosmi\Desktop\Proiecte\Subtitles fixer\SubtitlesFixer.App\bin\TempBuild\SubtitlesFixer.dll") | Out-Null
$normType = [SubtitlesFixer.App.Subtitles.SubtitleNormalizer]
$decodeMethod = $normType.GetMethod("DecodeBytes", [System.Reflection.BindingFlags]'Public,Static')
$normMethod = $normType.GetMethod("Normalize")
$backupPath = "c:\Users\cosmi\Desktop\Proiecte\Subtitles fixer\ignore for tests\backup\Power Book III_ Raising Kanan - 04x03 - Bygones.FENiX.Romanian.srt"
$rawBytes = [System.IO.File]::ReadAllBytes($backupPath)
$decoded = $decodeMethod.Invoke($null, @(,$rawBytes))
$result = $normMethod.Invoke($null, @($decoded))
$lines = $result -split "`r?`n"
Write-Host "Lines: $($lines.Count)"
$aa = [string]([char]0x0103) + [string]([char]0x0103)
$sa = [string]([char]0x0219) + [string]([char]0x0103)
foreach ($i in 0..($lines.Count-1)) {
  $l = $lines[$i]
  if ($l.Contains($aa) -or $l.Contains($sa)) {
    Write-Host "L$($i+1): $l"
  }
}
Write-Host "---DONE---"
