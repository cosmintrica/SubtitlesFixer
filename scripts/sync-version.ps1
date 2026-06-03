param(
  [switch]$Verify
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$versionPath = Join-Path $root "version.json"
$versionPayload = Get-Content -LiteralPath $versionPath -Raw | ConvertFrom-Json
$version = [string]$versionPayload.version

if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^\d+\.\d+\.\d+$') {
  throw "version.json trebuie sa contina o versiune SemVer simpla, ex: 1.0.10."
}

$fileVersion = "$version.0"
$changes = [System.Collections.Generic.List[string]]::new()

function Update-FileText {
  param(
    [string]$Path,
    [scriptblock]$Transform
  )

  $fullPath = Join-Path $root $Path
  $before = Get-Content -LiteralPath $fullPath -Raw -Encoding UTF8
  $after = & $Transform $before

  if ($before -eq $after) { return }

  [void]$script:changes.Add($Path)
  if (-not $script:Verify) {
    [System.IO.File]::WriteAllText($fullPath, $after, [System.Text.Encoding]::UTF8)
  }
}

Update-FileText "SubtitlesFixer.App/SubtitlesFixer.App.csproj" {
  param($text)
  $text = $text -replace '<Version>[^<]+</Version>', "<Version>$version</Version>"
  $text = $text -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$fileVersion</FileVersion>"
  $text = $text -replace '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$version</InformationalVersion>"
  return $text
}

Update-FileText "SubtitlesFixer.App/Properties/AssemblyInfo.cs" {
  param($text)
  $text = $text -replace 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$fileVersion`")"
  $text = $text -replace 'AssemblyInformationalVersion\("[^"]+"\)', "AssemblyInformationalVersion(`"$version`")"
  return $text
}

Update-FileText "site.js" {
  param($text)
  return ($text -replace 'version:\s*"[^"]+"', "version: `"$version`"")
}

Update-FileText "SubtitlesFixer.App/MainWindow.xaml" {
  param($text)
  return ($text -replace 'Subtitles Fixer v\d+\.\d+\.\d+', "Subtitles Fixer v$version")
}

Update-FileText "index.html" {
  param($text)
  return ($text -replace 'v\d+\.\d+\.\d+', "v$version")
}

if ($Verify -and $changes.Count -gt 0) {
  throw "Versiunea nu este sincronizata. Ruleaza scripts/sync-version.ps1. Fisiere: $($changes -join ', ')"
}

if ($changes.Count -gt 0) {
  Write-Host "Versiune sincronizata la $version in: $($changes -join ', ')"
} else {
  Write-Host "Versiunea $version este deja sincronizata."
}
