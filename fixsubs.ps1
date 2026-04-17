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

# ══════════════════════════════════════════════════════════════════════════════
# Dictionar romanesc (~960K cuvinte) - lazy loaded din words_ro-RO.txt
# ══════════════════════════════════════════════════════════════════════════════
$script:RoDict = $null

function Get-RoDictionary {
  if ($null -ne $script:RoDict) { return $script:RoDict }
  $scriptDir = if ($PSCommandPath) { Split-Path $PSCommandPath -Parent } else { (Get-Location).Path }
  $dictPath = Join-Path $scriptDir "words_ro-RO.txt"
  if (-not (Test-Path $dictPath)) {
    $script:RoDict = $false  # sentinel: dictionar indisponibil
    return $script:RoDict
  }
  $script:RoDict = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
  foreach ($line in [System.IO.File]::ReadLines($dictPath, [System.Text.Encoding]::UTF8)) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -gt 0) { [void]$script:RoDict.Add($trimmed) }
  }
  return $script:RoDict
}

# ══════════════════════════════════════════════════════════════════════════════
# Date statice pentru normalizare (mutate la script scope pt helper functions)
# ══════════════════════════════════════════════════════════════════════════════
$script:loCandidates = @([char]0x0219, [char]0x021B, [char]0x0103, [char]0x00E2, [char]0x00EE)
$script:upCandidates = @([char]0x0218, [char]0x021A, [char]0x0102, [char]0x00C2, [char]0x00CE)

$script:bi = @{
  # ---- ș + urmatorul ----
  'șt'=100; 'și'=95; 'șe'=55; 'șa'=50; 'șu'=35; 'șo'=30; 'șc'=25; 'șn'=10
  # ---- precedentul + ș ----
  'eș'=70; 'aș'=65; 'uș'=60; 'oș'=35; 'iș'=25
  # ---- ț + urmatorul ----
  'ți'=100; 'ță'=85; 'ța'=80; 'țe'=55; 'țu'=65; 'țo'=10
  # ---- precedentul + ț ----
  'nț'=70; 'lț'=45; 'aț'=55; 'eț'=45; 'uț'=35; 'iț'=30; 'oț'=20; 'rț'=15
  # ---- ă ----
  'ăr'=40; 'ră'=40; 'ăt'=30; 'tă'=35; 'ăl'=30; 'lă'=30; 'ăm'=25; 'mă'=40
  'ăi'=35; 'că'=45; 'să'=45; 'gă'=35; 'fă'=35; 'dă'=25; 'pă'=30; 'bă'=25
  'ză'=35; 'jă'=25
  # ---- â ----
  'ân'=50; 'nâ'=15; 'âm'=30; 'ât'=70; 'câ'=40; 'tâ'=15; 'lâ'=10; 'râ'=25
  # ---- î ----
  'în'=60; 'îm'=30; 'îl'=25; 'îi'=30; 'îș'=15; 'ît'=20; 'nî'=5
  # ---- extra ----
  'ăț'=40; 'țâ'=25
  'ău'=35; 'vă'=40; 'nă'=30
}

$script:triSet = [System.Collections.Generic.HashSet[string]]::new(
  [System.StringComparer]::OrdinalIgnoreCase)
@('ște','ști','ați','eți','iți','oți','uți',
  'ție','ții','ția','țiu','țio','țel','țil',
  'ănț','anț','enț','inț','unț','onț',
  'ață','eță','oță','ăsc','esc','isc','șco') |
  ForEach-Object { [void]$script:triSet.Add($_) }

$script:shortWords = [System.Collections.Generic.HashSet[string]]::new(
  [System.StringComparer]::OrdinalIgnoreCase)
@('și','să','vă','mă','în','că','îți','ăla','ăia','ăsta','ăstea',
  'niște','încă','până','câți','câte','câtă','câțiva',
  # ți-words: ați, toți sunt ambigue cu ași, toși (ambele in dictionar!)
  'ați','toți',
  # ș-words (bigram scorer greseste spre ț)
  'totuși','reușit','reușită','reușesc','leșin','leșinat','leșinată',
  'ieșit','ieșire','greșit','greșeală','greșesc','cunoaște','cunoaștem',
  'cunoștință','meșter',
  # ț-words (fara trigram ar merge spre ș)
  'puteți','faceți','mergeți','aveți','luați','dați','vreți',
  'spuneți','vedeți','credeți','băieți','trăiți','iubiți','doriți',
  # â-words (bigram scorer greseste spre ș/ț)
  'atât','când','câteva','mâine','pâine','pământ','sfânt','câmp',
  # ă/ș-words ambigue
  'așa','rău','tău','său','grău',
  # ș-words: bigram e?i/r?i favorizează ț greșit
  'ieși','ieșind','sfârșit','sfârșitul','sfârșesc',
  'uciși','ucis','detașament','detașamentele',
  'înfricoșător','înfricoșătoare','înfricoșători',
  'aceeași','aceleași','depășit','depășesc',
  'așadar','făptași','însăși','însuși',
  'obișnuit','obișnuiau','obișnuiesc','obișnuiți',
  'lași','greșiți',
  # ș-words: multi-? (cuvintele au 2+ marcaje)
  'orașul','orașului','orășenesc',
  'înșine','înșiși',
  'păpuși','păpușă','păpușar',
  'hârțogăraie','hârțogărie',
  # ț la inceput de cuvant (ambigue cu ș: țara/șara, ține/șine)
  'țara','țară','țării','țări','țărilor',
  'ține','ținea','ținut','ținută') |
  ForEach-Object { [void]$script:shortWords.Add($_) }

# ══════════════════════════════════════════════════════════════════════════════
# Helper functions pentru normalizare
# ══════════════════════════════════════════════════════════════════════════════

function Test-IsBrokenMarker {
  param([char]$ch)
  return ($ch -eq '?' -or $ch -eq [char]0xFFFD -or $ch -eq '`')
}

function Test-IsStartOfSentence {
  param([char[]]$chars, [int]$pos)
  for ($j = $pos - 1; $j -ge 0; $j--) {
    $ch = $chars[$j]
    if ($ch -eq ' ' -or $ch -eq "`t" -or $ch -eq "`r") { continue }
    if ($ch -eq "`n") {
      for ($k = $j - 1; $k -ge 0; $k--) {
        $prev = $chars[$k]
        if ($prev -eq ' ' -or $prev -eq "`t" -or $prev -eq "`r") { continue }
        return ($prev -eq "`n" -or $prev -eq '>' -or [char]::IsDigit($prev) -or
                $prev -eq '.' -or $prev -eq '!' -or $prev -eq '?')
      }
      return $true
    }
    return ($ch -eq '.' -or $ch -eq '!' -or $ch -eq '?')
  }
  return $true
}

function Get-ShouldBeUpperCase {
  param([char[]]$chars, [int]$pos)
  $hasLetterLeft  = ($pos -gt 0) -and [char]::IsLetter($chars[$pos-1])
  $hasLetterRight = ($pos -lt $chars.Length-1) -and [char]::IsLetter($chars[$pos+1])
  if (-not $hasLetterLeft) {
    # Daca vecinul stang e un marker stricat, suntem in mijlocul cuvantului
    if ($pos -gt 0 -and (Test-IsBrokenMarker $chars[$pos-1])) { return $false }
    return ($hasLetterRight -and (Test-IsStartOfSentence $chars $pos))
  }
  return ($hasLetterLeft -and $hasLetterRight -and
          [char]::IsUpper($chars[$pos-1]) -and [char]::IsUpper($chars[$pos+1]))
}

function Get-PenalizeImpossible {
  param([char[]]$chars, [int]$pos, [char]$lower)
  $penalty = 0
  $hasLetterLeft  = ($pos -gt 0) -and [char]::IsLetter($chars[$pos-1])
  $hasLetterRight = ($pos -lt $chars.Length-1) -and [char]::IsLetter($chars[$pos+1])

  # ț urmat de consoana este extrem de rar
  if ($lower -eq [char]0x021B -and $hasLetterRight) {
    $r = [char]::ToLowerInvariant($chars[$pos+1])
    if ($r -eq 't' -or $r -eq [char]0x021B -or $r -eq [char]0x0219) { $penalty -= 200 }
    elseif (-not 'aăâeîiou'.Contains($r)) { $penalty -= 100 }
  }
  # ș urmat de ș sau ț e imposibil
  if ($lower -eq [char]0x0219 -and $hasLetterRight) {
    $r = [char]::ToLowerInvariant($chars[$pos+1])
    if ($r -eq [char]0x0219 -or $r -eq [char]0x021B) { $penalty -= 200 }
  }
  # â nu apare la sfarsit de cuvant
  if ($lower -eq [char]0x00E2 -and -not $hasLetterRight) { $penalty -= 50 }
  # â nu apare la inceput de cuvant
  if ($lower -eq [char]0x00E2 -and -not $hasLetterLeft) { $penalty -= 100 }
  # î apare in principal la inceput de cuvant
  if ($lower -eq [char]0x00EE -and $hasLetterLeft) { $penalty -= 40 }
  # La inceput de cuvant: restrictii
  if (-not $hasLetterLeft) {
    # ț la inceput de cuvant + vocala e OK (țara, ține, țări)
    # ț la inceput de cuvant + consoana e imposibil (țcoală)
    if ($lower -eq [char]0x021B) {
      if ($hasLetterRight -and -not 'aăâeîiou'.Contains([char]::ToLowerInvariant($chars[$pos+1]))) {
        $penalty -= 200  # țcoală, țnui etc.
      }
    }
    if ($lower -eq [char]0x0103) { $penalty -= 100 }  # ă la inceput
  }
  return $penalty
}

function Get-ScoreCandidate {
  param([char[]]$chars, [int]$pos, [char]$candidate)
  $score = 0
  $lo = [char]::ToLowerInvariant($candidate)
  $len = $chars.Length

  # Bigram stanga
  if ($pos -gt 0 -and [char]::IsLetter($chars[$pos-1])) {
    $left = [char]::ToLowerInvariant($chars[$pos-1])
    $bg = [string]::new(@($left, $lo))
    if ($script:bi.ContainsKey($bg)) { $score += $script:bi[$bg] }
  } elseif ($pos -gt 0 -and (Test-IsBrokenMarker $chars[$pos-1])) {
    $bestL = 0
    foreach ($lc in $script:loCandidates) {
      $bg = [string]::new(@($lc, $lo))
      if ($script:bi.ContainsKey($bg) -and $script:bi[$bg] -gt $bestL) { $bestL = $script:bi[$bg] }
    }
    $score += $bestL
  }

  # Bigram dreapta
  if ($pos -lt $len-1 -and [char]::IsLetter($chars[$pos+1])) {
    $right = [char]::ToLowerInvariant($chars[$pos+1])
    $bg = [string]::new(@($lo, $right))
    if ($script:bi.ContainsKey($bg)) { $score += $script:bi[$bg] }
  } elseif ($pos -lt $len-1 -and (Test-IsBrokenMarker $chars[$pos+1])) {
    $bestR = 0
    foreach ($rc in $script:loCandidates) {
      $bg = [string]::new(@($lo, $rc))
      if ($script:bi.ContainsKey($bg) -and $script:bi[$bg] -gt $bestR) { $bestR = $script:bi[$bg] }
    }
    $score += $bestR
  }

  # Trigram bonus
  if ($pos -gt 0 -and $pos -lt $len-1 -and
      [char]::IsLetter($chars[$pos-1]) -and [char]::IsLetter($chars[$pos+1])) {
    $tri = [string]::new(@(
      [char]::ToLowerInvariant($chars[$pos-1]),
      $lo,
      [char]::ToLowerInvariant($chars[$pos+1])))
    if ($script:triSet.Contains($tri)) { $score += 30 }
  }

  # Cuvant scurt bonus
  $wStart = $pos
  while ($wStart -gt 0 -and ([char]::IsLetter($chars[$wStart-1]) -or $chars[$wStart-1] -eq '?')) { $wStart-- }
  $wEnd = $pos
  while ($wEnd -lt $len-1 -and ([char]::IsLetter($chars[$wEnd+1]) -or $chars[$wEnd+1] -eq '?')) { $wEnd++ }
  $wChars = [char[]]::new($wEnd - $wStart + 1)
  for ($w = $wStart; $w -le $wEnd; $w++) {
    $wChars[$w - $wStart] = if ($w -eq $pos) { $lo } else { [char]::ToLowerInvariant($chars[$w]) }
  }
  $word = [string]::new($wChars)
  $wordMatch = $false
  if ($word.Length -le 15) {
    if ($script:shortWords.Contains($word)) {
      $wordMatch = $true
    } elseif ($word.IndexOf('?') -ge 0) {
      $qPos = [System.Collections.Generic.List[int]]::new()
      for ($p = 0; $p -lt $wChars.Length; $p++) {
        if ($wChars[$p] -eq '?') { [void]$qPos.Add($p) }
      }
      $wc = $wChars.Clone()
      if ($qPos.Count -eq 1) {
        foreach ($cc in $script:loCandidates) {
          $wc[$qPos[0]] = $cc
          if ($script:shortWords.Contains([string]::new($wc))) { $wordMatch = $true; break }
        }
      } elseif ($qPos.Count -ge 2) {
        :outerLoop foreach ($cc1 in $script:loCandidates) {
          $wc[$qPos[0]] = $cc1
          foreach ($cc2 in $script:loCandidates) {
            $wc[$qPos[1]] = $cc2
            if ($script:shortWords.Contains([string]::new($wc))) { $wordMatch = $true; break outerLoop }
          }
        }
      }
    }
  }
  if ($wordMatch) { $score += 200 }

  # Penalizari combinatii imposibile
  $score += Get-PenalizeImpossible $chars $pos $lo

  return $score
}

# ── Reparare cu dictionar (word-level, ≤5 markeri) ────────────────────────
function Invoke-TryDictionaryRepair {
  param([char[]]$chars, [int]$wordStart, [int]$wordEnd,
        [System.Collections.Generic.List[int]]$markers)

  $dict = Get-RoDictionary
  if ($dict -eq $false) { return $false }

  $markerCount = $markers.Count

  # Salveaza originalele
  $saved = [char[]]::new($markerCount)
  for ($k = 0; $k -lt $markerCount; $k++) { $saved[$k] = $chars[$markers[$k]] }

  # Determina majuscula/minuscula
  $isUpper = [bool[]]::new($markerCount)
  for ($k = 0; $k -lt $markerCount; $k++) { $isUpper[$k] = Get-ShouldBeUpperCase $chars $markers[$k] }

  # ── Branch B: inlocuieste TOTI markerii ──
  $bestValues = $null
  $bestScore = [int]::MinValue
  $totalCombs = [int][Math]::Pow(5, $markerCount)

  for ($comb = 0; $comb -lt $totalCombs; $comb++) {
    $c = $comb
    for ($k = 0; $k -lt $markerCount; $k++) {
      $idx = $c % 5; $c = [Math]::Floor($c / 5)
      $cands = if ($isUpper[$k]) { $script:upCandidates } else { $script:loCandidates }
      $chars[$markers[$k]] = $cands[$idx]
    }
    $word = [string]::new($chars, $wordStart, $wordEnd - $wordStart)
    if (-not $dict.Contains($word)) { continue }

    # Scor bigram ca tiebreaker + bonus dictionar
    $score = 500
    for ($k = 0; $k -lt $markerCount; $k++) {
      $pos = $markers[$k]
      if ($pos -gt 0 -and [char]::IsLetter($chars[$pos-1])) {
        $bg = [string]::new(@([char]::ToLowerInvariant($chars[$pos-1]), [char]::ToLowerInvariant($chars[$pos])))
        if ($script:bi.ContainsKey($bg)) { $score += $script:bi[$bg] }
      }
      if ($pos -lt $chars.Length-1 -and [char]::IsLetter($chars[$pos+1])) {
        $bg = [string]::new(@([char]::ToLowerInvariant($chars[$pos]), [char]::ToLowerInvariant($chars[$pos+1])))
        if ($script:bi.ContainsKey($bg)) { $score += $script:bi[$bg] }
      }
      # Trigram bonus (acelasi ca in BigramRepairFallback)
      if ($pos -gt $wordStart -and $pos -lt ($wordEnd - 1) `
          -and [char]::IsLetter($chars[$pos-1]) -and [char]::IsLetter($chars[$pos+1])) {
        $tri = [string]::new(@([char]::ToLowerInvariant($chars[$pos-1]), [char]::ToLowerInvariant($chars[$pos]), [char]::ToLowerInvariant($chars[$pos+1])))
        if ($script:triSet.Contains($tri)) { $score += 30 }
      }
      # Penalizare combinatii imposibile (fonotactica)
      $score += (Get-PenalizeImpossible $chars $pos ([char]::ToLowerInvariant($chars[$pos])))
    }
    # Bonus pentru cuvinte scurte comune (și, să, că, așa, etc.)
    if ($script:shortWords.Contains($word)) { $score += 200 }
    if ($score -gt $bestScore) {
      $bestScore = $score
      $bestValues = [char[]]::new($markerCount)
      for ($k = 0; $k -lt $markerCount; $k++) { $bestValues[$k] = $chars[$markers[$k]] }
    }
  }

  # Restaureaza originalele
  for ($k = 0; $k -lt $markerCount; $k++) { $chars[$markers[$k]] = $saved[$k] }

  # ── Branch A: trailing ? sau ` → pastreaza ca punctuatie ──
  $trailingBestValues = $null
  $trailingBestScore = [int]::MinValue
  $lastPos = $markers[$markerCount - 1]
  $lastIsEnd = ($lastPos -eq $wordEnd - 1)
  $lastMarkerChar = $saved[$markerCount - 1]

  if ($lastIsEnd -and ($lastMarkerChar -eq '?' -or $lastMarkerChar -eq '`' -or $lastMarkerChar -eq [char]0xFFFD)) {
    # Bonus mare: trailing ?/`/FFFD dupa un cuvant valid e aproape sigur
    # semn de intrebare real sau apostrof corupt, nu diacritica
    # (ă/â/î sunt reprezentabile in Windows-1250, deci nu ar fi inlocuite cu ?)
    $trailingPunctuationBonus = 1000
    if ($markerCount -eq 1) {
      # Singurul marker e la sfarsit → verifica cuvantul fara el
      $wordWithout = [string]::new($chars, $wordStart, $wordEnd - $wordStart - 1)
      if ($dict.Contains($wordWithout)) { $trailingBestScore = $trailingPunctuationBonus }
    } else {
      # Mai multi markeri → repara pe toti in afara de ultimul
      $subCount = $markerCount - 1
      $subTotalCombs = [int][Math]::Pow(5, $subCount)
      for ($comb = 0; $comb -lt $subTotalCombs; $comb++) {
        $c = $comb
        for ($k = 0; $k -lt $subCount; $k++) {
          $idx = $c % 5; $c = [Math]::Floor($c / 5)
          $cands = if ($isUpper[$k]) { $script:upCandidates } else { $script:loCandidates }
          $chars[$markers[$k]] = $cands[$idx]
        }
        $word = [string]::new($chars, $wordStart, $wordEnd - $wordStart - 1)
        if (-not $dict.Contains($word)) { continue }

        $score = 500
        for ($k = 0; $k -lt $subCount; $k++) {
          $pos = $markers[$k]
          if ($pos -gt 0 -and [char]::IsLetter($chars[$pos-1])) {
            $bg = [string]::new(@([char]::ToLowerInvariant($chars[$pos-1]), [char]::ToLowerInvariant($chars[$pos])))
            if ($script:bi.ContainsKey($bg)) { $score += $script:bi[$bg] }
          }
          if ($pos -lt $chars.Length-1 -and [char]::IsLetter($chars[$pos+1])) {
            $bg = [string]::new(@([char]::ToLowerInvariant($chars[$pos]), [char]::ToLowerInvariant($chars[$pos+1])))
            if ($script:bi.ContainsKey($bg)) { $score += $script:bi[$bg] }
          }
        }
        if ($score -gt $trailingBestScore) {
          $trailingBestScore = $score
          $trailingBestValues = [char[]]::new($subCount)
          for ($k = 0; $k -lt $subCount; $k++) { $trailingBestValues[$k] = $chars[$markers[$k]] }
        }
      }
      # Restaureaza sub-markers
      for ($k = 0; $k -lt $subCount; $k++) { $chars[$markers[$k]] = $saved[$k] }
      # Adauga bonus pentru trailing punctuation
      if ($trailingBestScore -gt [int]::MinValue) {
        $trailingBestScore += ($trailingPunctuationBonus - 500)
      }
    }
  }

  # Alege cel mai bun rezultat
  if ($bestScore -ge $trailingBestScore -and $null -ne $bestValues) {
    for ($k = 0; $k -lt $markerCount; $k++) { $chars[$markers[$k]] = $bestValues[$k] }
    return $true
  }
  if ($trailingBestScore -gt [int]::MinValue) {
    if ($null -ne $trailingBestValues) {
      $subCount = $markerCount - 1
      for ($k = 0; $k -lt $subCount; $k++) { $chars[$markers[$k]] = $trailingBestValues[$k] }
    }
    $chars[$lastPos] = if ($lastMarkerChar -eq '`' -or $lastMarkerChar -eq [char]0xFFFD) { [char]"'" } else { $lastMarkerChar }
    return $true
  }
  return $false
}

# ── Fallback pe bigram scoring (per-caracter, multi-pass) ─────────────────
function Invoke-BigramRepairFallback {
  param([char[]]$chars, [int]$wordStart, [int]$wordEnd,
        [System.Collections.Generic.List[int]]$markers)

  $lastPos = $markers[$markers.Count - 1]
  $lastIsEnd = ($lastPos -eq $wordEnd - 1)
  $lastChar = $chars[$lastPos]

  # ? la sfarsit de cuvant → semn de intrebare real
  if ($lastIsEnd -and $lastChar -eq '?' -and $lastPos -gt $wordStart -and [char]::IsLetter($chars[$lastPos - 1])) {
    $markers = [System.Collections.Generic.List[int]]::new($markers.GetRange(0, $markers.Count - 1))
  }
  # ` sau FFFD la sfarsit de cuvant → apostrof
  elseif ($lastIsEnd -and ($lastChar -eq '`' -or $lastChar -eq [char]0xFFFD) -and $lastPos -gt $wordStart -and [char]::IsLetter($chars[$lastPos - 1])) {
    $chars[$lastPos] = [char]"'"
    $markers = [System.Collections.Generic.List[int]]::new($markers.GetRange(0, $markers.Count - 1))
  }

  if ($markers.Count -eq 0) { return }

  # Multi-pass bigram repair
  for ($pass = 0; $pass -lt 3; $pass++) {
    $changed = $false
    foreach ($pos in $markers) {
      if (-not (Test-IsBrokenMarker $chars[$pos])) { continue }
      $hasLeft  = ($pos -gt $wordStart) -and [char]::IsLetter($chars[$pos-1])
      $hasRight = ($pos -lt $wordEnd-1) -and [char]::IsLetter($chars[$pos+1])
      if (-not $hasLeft -and -not $hasRight) { continue }

      $isUp = Get-ShouldBeUpperCase $chars $pos
      $candidates = if ($isUp) { $script:upCandidates } else { $script:loCandidates }
      $bestChar = [char]0
      $bestScore = [int]::MinValue

      foreach ($cand in $candidates) {
        $score = Get-ScoreCandidate $chars $pos $cand
        if ($score -gt $bestScore) { $bestScore = $score; $bestChar = $cand }
      }

      if ($bestScore -gt 0) { $chars[$pos] = $bestChar; $changed = $true }
    }
    if (-not $changed) { break }
  }
}

# ══════════════════════════════════════════════════════════════════════════════
# Normalize-RO  – CRLF → cedilla → dictionar+bigram repair → â→î → fix diacritice
# ══════════════════════════════════════════════════════════════════════════════

function Normalize-RO {
  param([string]$s)
  $s = $s -replace "`r?`n","`r`n" # CRLF
  $s = $s.Replace([string][char]0x015F, [string][char]0x0219)  # s-cedilla -> s-comma
  $s = $s.Replace([string][char]0x015E, [string][char]0x0218)  # S-cedilla -> S-comma
  $s = $s.Replace([string][char]0x0163, [string][char]0x021B)  # t-cedilla -> t-comma
  $s = $s.Replace([string][char]0x0162, [string][char]0x021A)  # T-cedilla -> T-comma

  # --------------------------------------------------------------------------
  # Pas 3: Reparare bazata pe dictionar + fallback bigram
  #
  # Algoritm:
  # 1. Imparte textul in "cuvinte" (secvente de litere + markeri)
  # 2. Pentru fiecare cuvant cu markeri (? / U+FFFD / `):
  #    a. Daca ≤5 markeri: incearca toate combinatiile de candidati
  #       si verifica dictionarul (~960K cuvinte romanesti)
  #    b. Daca dictionarul nu gaseste nimic: fallback la bigram scoring
  # --------------------------------------------------------------------------

  $chars = $s.ToCharArray()
  $len = $chars.Length
  $i = 0

  while ($i -lt $len) {
    if (-not [char]::IsLetter($chars[$i]) -and -not (Test-IsBrokenMarker $chars[$i])) {
      $i++
      continue
    }

    # Gaseste limitele cuvantului (litere + markeri)
    $wordStart = $i
    while ($i -lt $len -and ([char]::IsLetter($chars[$i]) -or (Test-IsBrokenMarker $chars[$i]))) { $i++ }
    $wordEnd = $i

    # Colecteaza pozitiile markerilor
    $markers = [System.Collections.Generic.List[int]]::new()
    for ($j = $wordStart; $j -lt $wordEnd; $j++) {
      if (Test-IsBrokenMarker $chars[$j]) { [void]$markers.Add($j) }
    }
    if ($markers.Count -eq 0) { continue }

    # Regula context: ?i dupa cratima (-?i) → ți (pronume reflexiv/dativ)
    # Exemple: Pune-ți, să-ți, fă-ți, Lasă-ți
    if ($markers.Count -eq 1 -and ($wordEnd - $wordStart) -eq 2 `
        -and (Test-IsBrokenMarker $chars[$wordStart]) -and $chars[$wordStart + 1] -eq 'i' `
        -and $wordStart -gt 0 -and $chars[$wordStart - 1] -eq '-') {
      $chars[$wordStart] = if (Get-ShouldBeUpperCase $chars $wordStart) { [char]'Ț' } else { [char]'ț' }
      continue
    }

    # Incearca reparare cu dictionar (≤5 markeri)
    if ($markers.Count -le 5 -and (Invoke-TryDictionaryRepair $chars $wordStart $wordEnd $markers)) {
      continue
    }

    # Fallback: reparare per-caracter cu bigram scoring
    Invoke-BigramRepairFallback $chars $wordStart $wordEnd $markers
  }

  # Pas 4: â la inceput de cuvant → î (regula ortografica romana)
  for ($i = 0; $i -lt $chars.Length; $i++) {
    if ($chars[$i] -ne [char]0x00E2 -and $chars[$i] -ne [char]0x00C2) { continue }
    if ($i -gt 0 -and [char]::IsLetter($chars[$i-1])) { continue }
    $chars[$i] = if ($chars[$i] -eq [char]0x00E2) { [char]0x00EE } else { [char]0x00CE }
  }

  # --------------------------------------------------------------------------
  # Pas 5: Corectare diacritice gresite cu dictionar
  #
  # Unele fisiere au diacritice valide dar incorecte (ex: ț in loc de ă).
  # Pentru fiecare cuvant cu diacritice care NU exista in dictionar,
  # incearca sa inlocuiasca diacriticele cu alternative.
  # --------------------------------------------------------------------------
  $dict = Get-RoDictionary
  if ($dict -ne $false) {
    $len = $chars.Length
    $i = 0
    while ($i -lt $len) {
      if (-not [char]::IsLetter($chars[$i])) { $i++; continue }

      # Gaseste limitele cuvantului
      $wordStart = $i
      while ($i -lt $len -and [char]::IsLetter($chars[$i])) { $i++ }
      $wordEnd = $i

      # Colecteaza pozitiile diacriticelor
      $diacPositions = [System.Collections.Generic.List[int]]::new()
      for ($j = $wordStart; $j -lt $wordEnd; $j++) {
        $lo = [char]::ToLowerInvariant($chars[$j])
        if ($lo -eq [char]0x0219 -or $lo -eq [char]0x021B -or $lo -eq [char]0x0103 -or
            $lo -eq [char]0x00E2 -or $lo -eq [char]0x00EE) {
          [void]$diacPositions.Add($j)
        }
      }
      if ($diacPositions.Count -eq 0) { continue }

      # Daca cuvantul e format DOAR din diacritice (ex: ăă, ă),
      # e probabil o interjectie/filler — nu incerca sa o repari
      if ($diacPositions.Count -eq ($wordEnd - $wordStart)) { continue }

      # Cuvantul e deja corect?
      $word = [string]::new($chars, $wordStart, $wordEnd - $wordStart)
      if ($dict.Contains($word)) { continue }

      # Prea multe diacritice → skip (explozie combinatoriala)
      if ($diacPositions.Count -gt 5) { continue }

      # Salvare originale
      $diacCount = $diacPositions.Count
      $savedDiac = [char[]]::new($diacCount)
      for ($k = 0; $k -lt $diacCount; $k++) { $savedDiac[$k] = $chars[$diacPositions[$k]] }

      # Incearca toate combinatiile de diacritice alternative
      $diacBestValues = $null
      $diacBestScore = [int]::MinValue
      $diacTotalCombs = [int][Math]::Pow(5, $diacCount)

      for ($comb = 0; $comb -lt $diacTotalCombs; $comb++) {
        $c = $comb
        for ($k = 0; $k -lt $diacCount; $k++) {
          $idx = $c % 5; $c = [Math]::Floor($c / 5)
          $isUp = [char]::IsUpper($chars[$diacPositions[$k]])
          $cands = if ($isUp) { $script:upCandidates } else { $script:loCandidates }
          $chars[$diacPositions[$k]] = $cands[$idx]
        }

        $word = [string]::new($chars, $wordStart, $wordEnd - $wordStart)
        if (-not $dict.Contains($word)) { continue }

        # Calculeaza scor bigram ca tiebreaker
        $score = 0
        for ($k = 0; $k -lt $diacCount; $k++) {
          $pos = $diacPositions[$k]
          $lo = [char]::ToLowerInvariant($chars[$pos])
          if ($pos -gt $wordStart) {
            $bg = [string]::new(@([char]::ToLowerInvariant($chars[$pos-1]), $lo))
            if ($script:bi.ContainsKey($bg)) { $score += $script:bi[$bg] }
          }
          if ($pos -lt $wordEnd - 1) {
            $bg = [string]::new(@($lo, [char]::ToLowerInvariant($chars[$pos+1])))
            if ($script:bi.ContainsKey($bg)) { $score += $script:bi[$bg] }
          }
        }
        if ($score -gt $diacBestScore) {
          $diacBestScore = $score
          $diacBestValues = [char[]]::new($diacCount)
          for ($k = 0; $k -lt $diacCount; $k++) { $diacBestValues[$k] = $chars[$diacPositions[$k]] }
        }
      }

      # Restaureaza originalele
      for ($k = 0; $k -lt $diacCount; $k++) { $chars[$diacPositions[$k]] = $savedDiac[$k] }

      # Aplica corectia daca am gasit ceva diferit
      if ($null -ne $diacBestValues) {
        $different = $false
        for ($k = 0; $k -lt $diacCount; $k++) {
          if ($diacBestValues[$k] -ne $savedDiac[$k]) { $different = $true; break }
        }
        if ($different) {
          for ($k = 0; $k -lt $diacCount; $k++) { $chars[$diacPositions[$k]] = $diacBestValues[$k] }
        }
      }
    }
  }

  return [string]::new($chars)
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

  Write-Host "ROOT:   $root"
  Write-Host "BACKUP: $backupRoot"
  Write-Host ""

  $videos = Get-ChildItem -Path $root -File -Recurse:$Recurse -ErrorAction SilentlyContinue |
    Where-Object {
      $_.Extension -match '\.(mkv|mp4|avi)$' -and $_.FullName -notmatch '\\backup\\'
    }

  $subtitleFilesInRoot = @(
    Get-ChildItem -Path $root -File -Filter *.srt -Recurse:$Recurse -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -notmatch '\\backup\\' }
  ).Count

  if (-not $videos) {
    if ($subtitleFilesInRoot -gt 0) {
      Write-Host "  [WARN] Nu am gasit video in acest root, dar exista $subtitleFilesInRoot subtitrari .srt." -ForegroundColor Yellow
    } else {
      Write-Host "  [WARN] Nu am gasit video in acest root." -ForegroundColor Yellow
    }
    Write-Host ""
    $totalWarn++
    $noVideoMessage = if ($subtitleFilesInRoot -gt 0) {
      "Am gasit $subtitleFilesInRoot subtitrari .srt in acest folder, dar niciun fisier video (.mkv, .mp4, .avi). Daca vrei doar repararea subtitrarilor, foloseste butonul Repara subtitrari."
    } else {
      "Nu am gasit fisiere video (.mkv, .mp4, .avi) in acest folder."
    }
    [void]$summaryItems.Add([ordered]@{
        season  = ""
        episode = ""
        videoName = ""
        videoPath = ""
        status  = "warn"
        message = $noVideoMessage
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
        message = $noVideoMessage
        subtitleCount = $subtitleFilesInRoot
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
            subtitleCount = 0
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
            subtitleCount = $candidateDtos.Count
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
