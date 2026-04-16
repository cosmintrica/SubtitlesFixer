param(
  [string[]]$Paths = @((Get-Location).Path),
  [switch]$Recurse = $true,
  [switch]$NoPause,
  [string]$SummaryJsonPath = "",
  [string]$PreviewJsonPath = "",
  [string]$SelectionJsonPath = "",
  # Daca exista deja VideoName.ro.srt: muta varianta veche in backup si scrie una noua din sursa .srt
  [switch]$OverwriteRo = $false,
  [switch]$PreviewOnly
)

Write-Host "========================================="
Write-Host " FIX + RENAME SUBTITRARI (RECURSIV + BACKUP FOLDER)"
Write-Host "========================================="
Write-Host ""

# UTF-8 fara BOM (ideal pt VLC/Plex/TV)
$utf8NoBom  = New-Object System.Text.UTF8Encoding($false)
# UTF-8 strict (arunca exceptie daca bytes nu sunt UTF-8 valid)
$utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)

function Get-BomEncodingNameAndOffset {
  param([byte[]]$bytes)

  if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
    return @{ Name="UTF-8-BOM"; Encoding=[Text.Encoding]::UTF8; Offset=3 }
  }
  if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
    return @{ Name="UTF-16LE"; Encoding=[Text.Encoding]::Unicode; Offset=2 }
  }
  if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
    return @{ Name="UTF-16BE"; Encoding=[Text.Encoding]::BigEndianUnicode; Offset=2 }
  }
  return $null
}

function Score-TextForRO {
  param([string]$t)
  $diac = [regex]::Matches($t,'[\u0103\u0102\u00E2\u00C2\u00EE\u00CE\u0219\u0218\u021B\u021A\u015F\u015E\u0163\u0162]').Count
  $rep  = [regex]::Matches($t,'\uFFFD').Count
  $bad  = [regex]::Matches($t,'[^\u0009\u000A\u000D\u0020-\uFFFD]').Count
  return (3*$diac) - (5*$rep) - (3*$bad)
}

function Decode-Best {
  param([byte[]]$bytes)

  # 1) Daca are BOM, e clar
  $bom = Get-BomEncodingNameAndOffset $bytes
  if ($bom) {
    $slice = $bytes
    if ($bom.Offset -gt 0) { $slice = $bytes[$bom.Offset..($bytes.Length-1)] }
    return @{ Text = $bom.Encoding.GetString($slice); Name = $bom.Name }
  }

  # 2) UTF-8 doar daca e VALID (strict). Altfel nu-l punem candidat.
  $cands = @()
  try {
    [void]$utf8Strict.GetString($bytes)
    $cands += @{ n='UTF-8'; e=[Text.Encoding]::UTF8 }
  } catch { }

  $cands += @(
    @{ n='Win-1250';    e=[Text.Encoding]::GetEncoding(1250) },
    @{ n='ISO-8859-2';  e=[Text.Encoding]::GetEncoding(28592) },
    @{ n='IBM852';      e=[Text.Encoding]::GetEncoding(852) },
    @{ n='Win-1252';    e=[Text.Encoding]::GetEncoding(1252) }
  )

  $bestText=$null; $bestName=""; $bestScore=[double]::NegativeInfinity
  foreach ($c in $cands) {
    try {
      $t = $c.e.GetString($bytes)
      $s = Score-TextForRO $t
      if ($s -gt $bestScore) { $bestScore=$s; $bestText=$t; $bestName=$c.n }
    } catch {}
  }

  if (-not $bestText) { throw "Nu am putut detecta encoding-ul." }
  return @{ Text=$bestText; Name=$bestName }
}

function Normalize-RO {
  param([string]$s)
  $s = $s -replace "`r?`n","`r`n" # CRLF
  $s = $s.Replace([string][char]0x015F, [string][char]0x0219)  # s-cedilla -> s-comma
  $s = $s.Replace([string][char]0x015E, [string][char]0x0218)  # S-cedilla -> S-comma
  $s = $s.Replace([string][char]0x0163, [string][char]0x021B)  # t-cedilla -> t-comma
  $s = $s.Replace([string][char]0x0162, [string][char]0x021A)  # T-cedilla -> T-comma
  return $s
}

function Ensure-UniquePath {
  param([string]$Path)
  if (-not (Test-Path $Path)) { return $Path }
  $dir = Split-Path $Path -Parent
  $name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
  $ext  = [System.IO.Path]::GetExtension($Path)
  $i=1
  while ($true) {
    $p = Join-Path $dir ("{0}.{1}{2}" -f $name, $i, $ext)
    if (-not (Test-Path $p)) { return $p }
    $i++
  }
}

# RELDIR robust (fara Uri): daca dir incepe cu root, taiem prefixul
function Get-RelDir {
  param([string]$Base, [string]$Dir)

  $base = (Resolve-Path $Base).Path.TrimEnd('\')
  $dirp = (Resolve-Path $Dir).Path.TrimEnd('\')

  if ($dirp.Length -ge $base.Length -and
      $dirp.Substring(0, $base.Length).Equals($base, [StringComparison]::OrdinalIgnoreCase)) {
    return $dirp.Substring($base.Length).TrimStart('\')  # poate fi "" (direct in root)
  }

  # fallback: daca e pe alt drive etc.
  return (Split-Path $dirp -Leaf)
}

# Ex: "S01E13.E14" -> S01E13 si S01E14 (episoade in doua parti)
function Get-EpisodeTagsFromVideoName {
  param([string]$name)
  $list = New-Object System.Collections.Generic.List[string]
  $special = [regex]::Match($name, '(?i)S(\d{2})E(\d{2})\.E(\d{2})')
  if ($special.Success) {
    $s = $special.Groups[1].Value
    [void]$list.Add("S" + $s + "E" + $special.Groups[2].Value)
    [void]$list.Add("S" + $s + "E" + $special.Groups[3].Value)
  }
  foreach ($m in [regex]::Matches($name, '(?i)S\d{2}E\d{2}')) {
    $v = $m.Value.ToUpperInvariant()
    if (-not $list.Contains($v)) { [void]$list.Add($v) }
  }
  return ,([string[]]$list.ToArray())
}

function Normalize-MediaKey {
  param([string]$value)

  $normalized = [regex]::Replace($value, '[._]+', ' ')
  $normalized = [regex]::Replace($normalized, '\s+', ' ').Trim(' ', '-', '_', '.')
  if ([string]::IsNullOrWhiteSpace($normalized)) { return "" }
  return $normalized.ToLowerInvariant()
}

function Get-NumericEpisodeInfoFromVideoName {
  param([string]$name)

  $base = [System.IO.Path]::GetFileNameWithoutExtension($name)
  # Pattern: Titlu - 01 - Rest SAU Titlu - 01 SAU Titlu 01
  $match = [regex]::Match($base, '^(?<title>.*?)[\s._-]+(?<ep>\d{1,4})(?:[\s._-]+(?<rest>.*))?$')
  if (-not $match.Success) { return $null }

  $titleRaw = [string]$match.Groups['title'].Value
  if ([string]::IsNullOrWhiteSpace($titleRaw)) { return $null }

  $seriesKey = Normalize-MediaKey $titleRaw
  if ([string]::IsNullOrWhiteSpace($seriesKey) -or $seriesKey.Length -lt 2) { return $null }

  $episodeRaw = [string]$match.Groups['ep'].Value
  # Daca e 1 cifra, punem 0 in fata. Daca e deja 001, lasam asa.
  $episodeToken = if ($episodeRaw.Length -eq 1) { "0" + $episodeRaw } else { $episodeRaw }

  return [ordered]@{
    SeriesKey = $seriesKey
    EpisodeRaw = $episodeRaw
    EpisodeToken = $episodeToken
    DisplayEpisode = "E" + $episodeToken
    SeasonKey = "Nesazonat"
  }
}

# Ex: "...Shepherd.1.1080p" -> 1 (doar 1 sau 2, ca sa nu confundam ".38.1080p")
function Get-PartHintFromVideoName {
  param([string]$name)
  if ($name -match '(?i)\.(\d+)\.(1080p|720p|2160p|480p)') {
    $n = [int]$Matches[1]
    if ($n -ge 1 -and $n -le 2) { return $n }
  }
  return $null
}

function Select-SubtitleForVideo {
  param($subsAll, $video, [string[]]$tags)
  if ($tags.Count -eq 0) { return $null }

  $candidates = @(
    $subsAll | Where-Object {
      $n = $_.Name
      foreach ($t in $tags) {
        if ($n -match [regex]::Escape($t)) { return $true }
      }
      return $false
    }
  )

  if ($candidates.Count -eq 0) { return $null }
  if ($candidates.Count -eq 1) { return $candidates[0] }

  $partHint = Get-PartHintFromVideoName $video.Name
  if ($null -ne $partHint -and $tags.Count -ge $partHint) {
    $preferredTag = $tags[$partHint - 1]
    $pref = @(
      $candidates | Where-Object { $_.Name -match [regex]::Escape($preferredTag) } |
        Sort-Object { $_.Name.Length }
    )
    if ($pref.Count -ge 1) { return $pref[0] }
  }

  return ($candidates | Sort-Object { $_.Name.Length } | Select-Object -First 1)
}

function Get-CandidateSubtitles {
  param($subsAll, [string[]]$tags)
  if ($tags.Count -eq 0) { return @() }

  return @(
    $subsAll |
      Where-Object {
        $n = $_.Name
        foreach ($t in $tags) {
          if ($n -match [regex]::Escape($t)) { return $true }
        }
        return $false
      } |
      Sort-Object @{ Expression = { $_.Name.Length } }, @{ Expression = { $_.Name } }
  )
}

function Load-SelectionMap {
  param([string]$Path)

  $map = @{}
  if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) { return $map }

  $raw = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
  if ([string]::IsNullOrWhiteSpace($raw)) { return $map }

  $payload = $raw | ConvertFrom-Json
  $items = @()
  if ($payload -and $payload.items) {
    $items = @($payload.items)
  }

  foreach ($item in $items) {
    $videoPath = [string]$item.videoPath
    if ([string]::IsNullOrWhiteSpace($videoPath)) { continue }
    $map[$videoPath] = $item
  }

  return $map
}

function Get-ManualSelectionForVideo {
  param($SelectionMap, [string]$VideoPath, $Candidates)

  $result = [ordered]@{
    Selected = $null
    SelectionMode = "none"
    Message = ""
    InvalidSelection = $false
  }

  if (-not $SelectionMap.ContainsKey($VideoPath)) {
    return $result
  }

  $selectionMode = [string]$SelectionMap[$VideoPath].selectionMode
  if (-not $selectionMode.Equals("manual", [System.StringComparison]::OrdinalIgnoreCase)) {
    return $result
  }

  $selectedPath = [string]$SelectionMap[$VideoPath].selectedSubtitlePath
  if ([string]::IsNullOrWhiteSpace($selectedPath)) {
    return $result
  }

  foreach ($candidate in $Candidates) {
    if ($candidate.FullName.Equals($selectedPath, [System.StringComparison]::OrdinalIgnoreCase)) {
      $result.Selected = $candidate
      $result.SelectionMode = "manual"
      return $result
    }
  }

  $result.InvalidSelection = $true
  $result.Message = "Selectia manuala salvata nu mai este disponibila; am revenit la alegerea automata."
  return $result
}

function Get-DuplicateSelectionErrors {
  param($SelectionMap)

  $selectedToVideos = @{}
  foreach ($entry in $SelectionMap.GetEnumerator()) {
    $videoPath = [string]$entry.Key
    $selectedPath = [string]$entry.Value.selectedSubtitlePath
    if ([string]::IsNullOrWhiteSpace($videoPath) -or [string]::IsNullOrWhiteSpace($selectedPath)) { continue }

    $key = $selectedPath.ToLowerInvariant()
    if (-not $selectedToVideos.ContainsKey($key)) {
      $selectedToVideos[$key] = New-Object System.Collections.Generic.List[string]
    }
    [void]$selectedToVideos[$key].Add($videoPath)
  }

  $errors = @{}
  foreach ($entry in $selectedToVideos.GetEnumerator()) {
    if ($entry.Value.Count -le 1) { continue }
    foreach ($videoPath in $entry.Value) {
      $errors[$videoPath] = "Aceeasi subtitrare este selectata pentru mai multe episoade. Corecteaza selectiile in analiza inainte de rulare."
    }
  }

  return $errors
}

function Get-ItemsJson {
  param($Items)

  $arr = @($Items.ToArray())
  if ($arr.Count -eq 0) { return "[]" }

  $json = $arr | ConvertTo-Json -Depth 10 -Compress
  if ($json -notmatch '^\s*\[') { return "[" + $json + "]" }
  return $json
}

$totalOk = 0
$totalWarn = 0
$totalErr = 0
$hadFatalError = $false
$summaryItems = [System.Collections.ArrayList]::new()
$previewReady = 0
$previewReview = 0
$previewWarn = 0
$previewErr = 0
$previewItems = [System.Collections.ArrayList]::new()
$selectionMap = @{}
try {
  $selectionMap = Load-SelectionMap $SelectionJsonPath
} catch {
  Write-Host "  [EROARE] Nu am putut citi selectiile manuale: $($_.Exception.Message)" -ForegroundColor Red
  $hadFatalError = $true
}
$duplicateSelectionErrors = @{}
if (-not $PreviewOnly) {
  $duplicateSelectionErrors = Get-DuplicateSelectionErrors $selectionMap
  if ($duplicateSelectionErrors.Count -gt 0) {
    $hadFatalError = $true
  }
}

function Get-SeasonKey {
  param([string]$EpisodeTag)
  if ([string]::IsNullOrWhiteSpace($EpisodeTag)) { return "Other" }
  $m = [regex]::Match($EpisodeTag, '(?i)^(S\d{2})')
  if ($m.Success) { return $m.Groups[1].Value.ToUpper() }
  return "Other"
}

function Write-SummaryJson {
  if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) { return }
  $dir = Split-Path $SummaryJsonPath -Parent
  if (-not [string]::IsNullOrWhiteSpace($dir)) {
    New-Item -ItemType Directory -Force -Path $dir -ErrorAction SilentlyContinue | Out-Null
  }
  $itemsJson = Get-ItemsJson $summaryItems
  $totalsObj = [ordered]@{ ok = $totalOk; warn = $totalWarn; err = $totalErr }
  $totalsJson = $totalsObj | ConvertTo-Json -Compress
  $json = "{`"totals`": $totalsJson, `"items`": $itemsJson }"
  # BOM UTF-8: .NET si editorii recunosc corect encoding-ul la citirea JSON-ului
  $utf8Bom = New-Object System.Text.UTF8Encoding($true)
  [System.IO.File]::WriteAllText($SummaryJsonPath, $json, $utf8Bom)
}

function Add-PreviewItem {
  param($Item)

  [void]$previewItems.Add($Item)
  switch ([string]$Item.status) {
    "ready"  { $script:previewReady++ }
    "review" { $script:previewReview++ }
    "warn"   { $script:previewWarn++ }
    "error"  { $script:previewErr++ }
  }
}

function Write-PreviewJson {
  if ([string]::IsNullOrWhiteSpace($PreviewJsonPath)) { return }
  $dir = Split-Path $PreviewJsonPath -Parent
  if (-not [string]::IsNullOrWhiteSpace($dir)) {
    New-Item -ItemType Directory -Force -Path $dir -ErrorAction SilentlyContinue | Out-Null
  }
  $itemsJson = Get-ItemsJson $previewItems
  $totalsObj = [ordered]@{ ready = $previewReady; review = $previewReview; warn = $previewWarn; err = $previewErr }
  $totalsJson = $totalsObj | ConvertTo-Json -Compress
  $json = "{`"totals`": $totalsJson, `"items`": $itemsJson }"
  $utf8Bom = New-Object System.Text.UTF8Encoding($true)
  [System.IO.File]::WriteAllText($PreviewJsonPath, $json, $utf8Bom)
}

foreach ($root in $Paths) {
  $rootInput = $root
  try {
    $root = (Resolve-Path $root -ErrorAction Stop).Path
  } catch {
    Write-Host "  [EROARE] Root invalid: $rootInput" -ForegroundColor Red
    Write-Host "          $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    $totalErr++
    $hadFatalError = $true
    [void]$summaryItems.Add([ordered]@{
        season  = ""
        episode = ""
        videoName = ""
        videoPath = ""
        status  = "error"
        message = "Folderul radacina nu exista sau nu poate fi accesat: $rootInput"
        rootPath = $rootInput
      })
    Add-PreviewItem ([ordered]@{
        season = ""
        episode = ""
        videoName = ""
        videoPath = ""
        targetName = ""
        targetPath = ""
        existingTarget = $false
        selectedSubtitleName = ""
        selectedSubtitlePath = ""
        selectionMode = "none"
        action = "none"
        status = "error"
        message = "Folderul radacina nu exista sau nu poate fi accesat: $rootInput"
        candidates = @()
      })
    continue
  }

  $backupRoot = Join-Path $root "backup"
  if (-not $PreviewOnly) {
    New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
  }

  Write-Host "ROOT:   $root"
  Write-Host "BACKUP: $backupRoot"
  Write-Host ""

  $videos = Get-ChildItem -Path $root -File -Recurse:$Recurse -ErrorAction SilentlyContinue |
    Where-Object {
      $_.Extension -match '\.(mkv|mp4|avi)$' -and $_.FullName -notmatch '\\backup\\'
    }

  if (-not $videos) {
    Write-Host "  [WARN] Nu am gasit video in acest root." -ForegroundColor Yellow
    Write-Host ""
    $totalWarn++
    [void]$summaryItems.Add([ordered]@{
        season  = ""
        episode = ""
        videoName = ""
        videoPath = ""
        status  = "warn"
        message = "Nu am gasit fisiere video (.mkv, .mp4, .avi) in acest folder."
        rootPath = $root
      })
    Add-PreviewItem ([ordered]@{
        season = ""
        episode = ""
        videoName = ""
        videoPath = ""
        targetName = ""
        targetPath = ""
        existingTarget = $false
        selectedSubtitleName = ""
        selectedSubtitlePath = ""
        selectionMode = "none"
        action = "none"
        status = "warn"
        message = "Nu am gasit fisiere video (.mkv, .mp4, .avi) in acest folder."
        candidates = @()
      })
    continue
  }

  $totalVideosInRoot = @($videos).Count
  $processedVideosInRoot = 0
  $groups = $videos | Group-Object DirectoryName

  foreach ($g in $groups) {
    $dir = $g.Name
    if ($dir -match '\\backup($|\\)') { continue }

    $subsAll = Get-ChildItem -Path $dir -File -Filter *.srt -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -notlike "*.ro.srt" }

    $numericSeriesCounts = @{}
    foreach ($groupVideo in $g.Group) {
      $numericInfoForGroup = Get-NumericEpisodeInfoFromVideoName $groupVideo.Name
      if (-not $numericInfoForGroup) { continue }

      $seriesKey = [string]$numericInfoForGroup.SeriesKey
      if (-not $numericSeriesCounts.ContainsKey($seriesKey)) {
        $numericSeriesCounts[$seriesKey] = 0
      }
      $numericSeriesCounts[$seriesKey]++
    }

    foreach ($video in $g.Group) {
      $processedVideosInRoot++
      Write-Host "__SF_PROGRESS__|$processedVideosInRoot|$totalVideosInRoot|$($video.Name)"
      Write-Host "-----------------------------------------"
      Write-Host "VIDEO:"
      Write-Host "  $($video.FullName)"

      $newName = [System.IO.Path]::ChangeExtension($video.Name, ".ro.srt")
      $newPath = Join-Path $dir $newName

      $episodeTags = Get-EpisodeTagsFromVideoName $video.Name
      $numericEpisodeInfo = $null
      if ($episodeTags.Count -eq 0) {
        $numericCandidate = Get-NumericEpisodeInfoFromVideoName $video.Name
        if ($numericCandidate -and
            $numericSeriesCounts.ContainsKey([string]$numericCandidate.SeriesKey) -and
            $numericSeriesCounts[[string]$numericCandidate.SeriesKey] -ge 2) {
          $numericEpisodeInfo = $numericCandidate
          $episodeTags = @([string]$numericCandidate.EpisodeToken)
          if (-not [string]::Equals([string]$numericCandidate.EpisodeRaw, [string]$numericCandidate.EpisodeToken, [System.StringComparison]::OrdinalIgnoreCase)) {
            $episodeTags += [string]$numericCandidate.EpisodeRaw
          }
        }
      }

      if ($episodeTags.Count -eq 0) {
        Write-Host "  [WARN] Nu pot detecta SxxEyy." -ForegroundColor Yellow
        $totalWarn++
        [void]$summaryItems.Add([ordered]@{
            season  = "Nesazonat"
            episode = ""
            videoName = $video.Name
            videoPath = $video.FullName
            status  = "warn"
            message = "Nu pot detecta pattern SxxEyy in numele fisierului video."
          })
        Add-PreviewItem ([ordered]@{
            season = "Nesazonat"
            episode = ""
            videoName = $video.Name
            videoPath = $video.FullName
            targetName = $newName
            targetPath = $newPath
            existingTarget = (Test-Path $newPath)
            selectedSubtitleName = ""
            selectedSubtitlePath = ""
            selectionMode = "none"
            action = "none"
            status = "warn"
            message = "Nu pot detecta pattern SxxEyy in numele fisierului video."
            candidates = @()
          })
        Write-Host ""
        continue
      }

      $episode = if ($numericEpisodeInfo) { [string]$numericEpisodeInfo.DisplayEpisode } else { $episodeTags[0] }
      $seasonKey = if ($numericEpisodeInfo) { [string]$numericEpisodeInfo.SeasonKey } else { (Get-SeasonKey $episode) }
      Write-Host "  EPISOD DETECTAT: $episode"
      if ($episodeTags.Count -gt 1) {
        Write-Host "  (tag-uri episod: $($episodeTags -join ', '))"
      }
      Write-Host ""

      $candidateSubs = Get-CandidateSubtitles -subsAll $subsAll -tags $episodeTags
      $candidateDtos = @()
      foreach ($candidate in $candidateSubs) {
        $candidateDtos += [ordered]@{
          name = $candidate.Name
          path = $candidate.FullName
        }
      }

      $manualSelection = Get-ManualSelectionForVideo -SelectionMap $selectionMap -VideoPath $video.FullName -Candidates $candidateSubs
      $selectionMode = [string]$manualSelection.SelectionMode
      $selectionMessage = [string]$manualSelection.Message
      $subtitle = $manualSelection.Selected
      if (-not $subtitle -and $candidateSubs.Count -gt 0) {
        $subtitle = Select-SubtitleForVideo -subsAll $candidateSubs -video $video -tags $episodeTags
        if ($subtitle -and $selectionMode -eq "none") {
          $selectionMode = "auto"
        }
      }

      Write-Host "  REDENUMESC IN:"
      Write-Host "    $newName"

      if ($PreviewOnly) {
        $previewStatus = "warn"
        $previewAction = "none"
        $previewMessage = ""

        if (-not $subtitle) {
          $tagHint = ($episodeTags -join ' sau ')
          if (Test-Path $newPath) {
            $previewStatus = "ready"
            $previewAction = "already-ok"
            $previewMessage = "Exista deja subtitrarea finala. Nu schimb nimic aici."
          } elseif (-not [string]::IsNullOrWhiteSpace($selectionMessage)) {
            $previewMessage = $selectionMessage
          } else {
            $previewMessage = "Nu am gasit nicio subtitrare potrivita pentru $tagHint in acest folder."
          }
        } else {
          if (Test-Path $newPath) {
            if ($OverwriteRo) {
              $previewAction = "overwrite"
              $previewMessage = "Voi reface subtitrarea finala si voi muta varianta veche in backup."
            } else {
              $previewAction = "skip-existing"
              $previewStatus = "warn"
              $previewMessage = "Exista deja subtitrarea finala. Daca vrei sa o refaci, lasa activa suprascrierea."
            }
          } else {
            $previewAction = "create"
            $previewMessage = "Voi crea subtitrarea finala si voi muta subtitrarea sursa in backup."
          }

          if ($previewAction -ne "skip-existing") {
            if ($candidateSubs.Count -gt 1 -and $selectionMode -ne "manual") {
              $previewStatus = "review"
              $previewMessage = "Am gasit mai multe subtitrari. Alege varianta buna inainte sa rulezi."
            } else {
              $previewStatus = "ready"
            }
          }

          if ($selectionMode -eq "manual" -and [string]::IsNullOrWhiteSpace($previewMessage)) {
            $previewMessage = "Folosesc alegerea facuta de tine."
          }
        }

        if (-not [string]::IsNullOrWhiteSpace($selectionMessage)) {
          if ([string]::IsNullOrWhiteSpace($previewMessage)) {
            $previewMessage = $selectionMessage
          } elseif ($previewMessage.IndexOf($selectionMessage, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            $previewMessage = $selectionMessage + " " + $previewMessage
          }
        }

        $selectedSubtitleName = ""
        $selectedSubtitlePath = ""
        if ($subtitle) {
          $selectedSubtitleName = $subtitle.Name
          $selectedSubtitlePath = $subtitle.FullName
        }

        Add-PreviewItem ([ordered]@{
            season = $seasonKey
            episode = $episode
            videoName = $video.Name
            videoPath = $video.FullName
            targetName = $newName
            targetPath = $newPath
            existingTarget = (Test-Path $newPath)
            selectedSubtitleName = $selectedSubtitleName
            selectedSubtitlePath = $selectedSubtitlePath
            selectionMode = $selectionMode
            action = $previewAction
            status = $previewStatus
            message = $previewMessage
            candidates = $candidateDtos
          })
        Write-Host ""
        continue
      }

      if ($duplicateSelectionErrors.ContainsKey($video.FullName)) {
        $errorMessage = [string]$duplicateSelectionErrors[$video.FullName]
        Write-Host "  [EROARE] $errorMessage" -ForegroundColor Red
        $totalErr++
        [void]$summaryItems.Add([ordered]@{
            season  = $seasonKey
            episode = $episode
            videoName = $video.Name
            videoPath = $video.FullName
            subtitleAfter = $newName
            targetPath = $newPath
            status  = "error"
            message = $errorMessage
          })
        Write-Host ""
        continue
      }

      if (-not $subtitle) {
        $tagHint = ($episodeTags -join ' sau ')
        $roForEp = Get-ChildItem -Path $dir -File -Filter *.srt -ErrorAction SilentlyContinue |
          Where-Object {
            $n = $_.Name
            if ($n -notmatch '\.ro\.srt$') { return $false }
            foreach ($t in $episodeTags) {
              if ($n -match [regex]::Escape($t)) { return $true }
            }
            return $false
          } |
          Select-Object -First 1

        if (Test-Path $newPath) {
          $msg = "Exista deja subtitrarea finala. Nu schimb nimic aici."
          Write-Host "  [OK] $msg" -ForegroundColor Green
          $totalOk++
          [void]$summaryItems.Add([ordered]@{
              season  = $seasonKey
              episode = $episode
              videoName = $video.Name
              videoPath = $video.FullName
              subtitleAfter = $newName
              targetPath = $newPath
              status  = "ok"
              message = $msg
            })
          Write-Host ""
          continue
        }

        if (-not [string]::IsNullOrWhiteSpace($selectionMessage)) {
          $msg = $selectionMessage
          Write-Host "  [WARN] $msg" -ForegroundColor Yellow
        } elseif ($roForEp) {
          $msg = "Exista deja un .ro.srt pentru $tagHint, dar subtitrarea sursa nu mai este in folder. Daca vrei sa refac episodul, pune inapoi fisierul .srt original."
          Write-Host "  [WARN] $msg" -ForegroundColor Yellow
        } else {
          $msg = "Nu am gasit nicio subtitrare .srt potrivita pentru $tagHint in acest folder."
          Write-Host "  [WARN] Nu exista subtitrare .srt pentru $tagHint in folder." -ForegroundColor Yellow
        }

        $totalWarn++
        [void]$summaryItems.Add([ordered]@{
            season  = $seasonKey
            episode = $episode
            videoName = $video.Name
            videoPath = $video.FullName
            subtitleAfter = $newName
            targetPath = $newPath
            status  = "warn"
            message = $msg
          })
        Write-Host ""
        continue
      }

      Write-Host "  SUBTITRARE GASITA:"
      Write-Host "    $($subtitle.Name)"
      if ($selectionMode -eq "manual") {
        Write-Host "  -> Selectie manuala pastrata." -ForegroundColor Cyan
      }

      if (Test-Path $newPath) {
        if (-not $OverwriteRo) {
          Write-Host "  [WARN] Exista deja target-ul -> sar peste (nu suprascriu)." -ForegroundColor Yellow
          Write-Host "  [HINT] Ruleaza cu -OverwriteRo ca sa refaci din subtitrarea sursa (.srt)." -ForegroundColor DarkGray
          $totalWarn++
          [void]$summaryItems.Add([ordered]@{
              season  = $seasonKey
              episode = $episode
              videoName = $video.Name
              videoPath = $video.FullName
              subtitleBefore = $subtitle.Name
              subtitleAfter = $newName
              targetPath = $newPath
              status  = "warn"
              message = "Exista deja subtitrarea finala. Activeaza suprascrierea daca vrei sa o refaci."
            })
          Write-Host ""
          continue
        }
      }

      $oldRoDest = $null
      $tempNewPath = $null

      try {
        # Muta sursa in backup (acelasi nume .srt), apoi citeste de acolo - fara dublu in folder
        $sourceOriginalPath = $subtitle.FullName
        $subDir = Split-Path $subtitle.FullName -Parent
        $relDir = Get-RelDir -Base $root -Dir $subDir

        $bakDir = if ([string]::IsNullOrWhiteSpace($relDir)) { $backupRoot } else { Join-Path $backupRoot $relDir }
        New-Item -ItemType Directory -Force -Path $bakDir -ErrorAction Stop | Out-Null

        Write-Host "  -> Backup folder: $bakDir"

        $bakPath = Join-Path $bakDir $subtitle.Name
        $bakPath = Ensure-UniquePath $bakPath

        Move-Item -LiteralPath $subtitle.FullName -Destination $bakPath -Force -ErrorAction Stop
        Write-Host "  -> Mutat sursa in backup: $bakPath"

        Write-Host "  -> Detect + repar encoding (AUTO -> UTF-8 fara BOM) + normalize s/t"

        $bytes = [System.IO.File]::ReadAllBytes($bakPath)
        $res   = Decode-Best $bytes
        $fixed = Normalize-RO $res.Text

        $tempNewPath = Ensure-UniquePath (Join-Path $dir ($newName + ".tmp"))
        [System.IO.File]::WriteAllText($tempNewPath, $fixed, $utf8NoBom)

        if (Test-Path $newPath) {
          $subDirRo = Split-Path $newPath -Parent
          $relDirRo = Get-RelDir -Base $root -Dir $subDirRo
          $bakDirRo = if ([string]::IsNullOrWhiteSpace($relDirRo)) { $backupRoot } else { Join-Path $backupRoot $relDirRo }
          New-Item -ItemType Directory -Force -Path $bakDirRo -ErrorAction Stop | Out-Null
          $oldRoDest = Join-Path $bakDirRo $newName
          $oldRoDest = Ensure-UniquePath $oldRoDest
          Move-Item -LiteralPath $newPath -Destination $oldRoDest -Force -ErrorAction Stop
          Write-Host "  -> Exista deja .ro.srt -> mutat vechiul fisier in backup: $oldRoDest" -ForegroundColor Cyan
        }

        Move-Item -LiteralPath $tempNewPath -Destination $newPath -Force -ErrorAction Stop
        $tempNewPath = $null
        Write-Host "  -> Scris: UTF-8 fara BOM (detectat: $($res.Name))"

        # scoate din lista cache ca sa nu-l mai "vada" la alte episoade
        $subsAll = $subsAll | Where-Object { $_.FullName -ne $subtitle.FullName }

        Write-Host "  [OK]" -ForegroundColor Green
        $totalOk++
        [void]$summaryItems.Add([ordered]@{
            season  = $seasonKey
            episode = $episode
            videoName = $video.Name
            videoPath = $video.FullName
            subtitleBefore = $subtitle.Name
            subtitleAfter = $newName
            encodingDetected = $res.Name
            backupPath = $bakPath
            sourceOriginalPath = $sourceOriginalPath
            sourceBackupPath = $bakPath
            targetPath = $newPath
            replacedTargetBackupPath = $oldRoDest
            status  = "ok"
            message = ""
          })
      } catch {
        $errorMessage = $_.Exception.Message
        if ($tempNewPath -and (Test-Path $tempNewPath)) {
          Remove-Item -LiteralPath $tempNewPath -Force -ErrorAction SilentlyContinue
        }
        if ($oldRoDest -and (Test-Path $oldRoDest) -and -not (Test-Path $newPath)) {
          try {
            Move-Item -LiteralPath $oldRoDest -Destination $newPath -Force -ErrorAction Stop
            Write-Host "  -> Restaurat .ro.srt anterior dupa eroare." -ForegroundColor Yellow
          } catch {
            Write-Host "  -> [WARN] Nu am putut restaura .ro.srt anterior: $($_.Exception.Message)" -ForegroundColor Yellow
          }
        }
        Write-Host "  [EROARE] $errorMessage" -ForegroundColor Red
        $totalErr++
        [void]$summaryItems.Add([ordered]@{
            season  = $seasonKey
            episode = $episode
            videoName = $video.Name
            videoPath = $video.FullName
            subtitleBefore = $subtitle.Name
            subtitleAfter = $newName
            targetPath = $newPath
            status  = "error"
            message = $errorMessage
          })
      }

      Write-Host ""
    }
  }
}

Write-Host "========================================="
Write-Host " FINALIZAT."
Write-Host " OK: $totalOk | WARN: $totalWarn | ERR: $totalErr"

try {
  Write-SummaryJson
} catch {
  Write-Host "  [WARN] Nu am putut scrie SummaryJson: $($_.Exception.Message)" -ForegroundColor Yellow
}

try {
  Write-PreviewJson
} catch {
  Write-Host "  [WARN] Nu am putut scrie PreviewJson: $($_.Exception.Message)" -ForegroundColor Yellow
}

if (-not $NoPause) {
  Write-Host " Apasa ENTER pentru inchidere."
  Read-Host | Out-Null
}

if ($hadFatalError) {
  exit 1
}
