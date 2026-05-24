# Plan de portare macOS - Subtitles Fixer

**Versiune document:** 1.0  
**Data:** Mai 2026  
**Stare curenta:** Windows-only (`net8.0-windows`, WPF, Velopack `win`)  
**Versiune aplicatie la planificare:** 1.0.9

---

## 1. Rezumat executiv

Subtitles Fixer nu poate fi „recompilat” direct pe macOS. Aplicatia depinde de **WPF** (interfata) si de un motor principal in **PowerShell** (`fixsubs.ps1`, ~1721 linii), lansat explicit prin `powershell.exe`. Pe Mac exista deja bucati portabile in C# (`SubtitleNormalizer`, `RomanianDictionary`, modele JSON), dar **fluxul complet Analiza / Ruleaza fix / Restore** trece prin script.

**Recomandare strategica:** portare in 3 straturi, nu „fork UI rapid”:

1. **SubtitlesFixer.Core** (`net8.0`) - motor unic in C#, fara UI, fara PowerShell la runtime.
2. **SubtitlesFixer.Windows** - WPF existent, rescris ca shell subtire peste Core.
3. **SubtitlesFixer.Mac** - UI cross-platform (Avalonia recomandat) peste acelasi Core.

**Varianta intermedia (MVP Mac, risc tehnic mai mare):** pastrezi `fixsubs.ps1`, rulezi **PowerShell 7 (`pwsh`)** bundlat in `.app`, UI Avalonia minimala. Util pentru validare piata, dar mentine doua motoare si complexitate de distributie.

**Estimare orientativa (echipa 1 dev, timp part-time):**

| Faza | Durata | Rezultat |
|------|--------|----------|
| 0 - Pregatire & audit | 1-2 saptamani | Inventar, spike-uri, decizie arhitectura |
| 1 - Core C# (motor) | 6-10 saptamani | Paritate functionala cu `fixsubs.ps1` |
| 2 - Abstractii platforma | 1-2 saptamani | Paths, dialoguri, update, logging |
| 3 - UI macOS (Avalonia) | 4-6 saptamani | MVP utilizabil |
| 4 - CI/CD + Velopack osx | 1-2 saptamani | Release automat |
| 5 - Semnare & notarizare | 1-2 saptamani | Gatekeeper OK |
| 6 - QA & lansare | 2-3 saptamani | Beta publica |
| **Total recomandat** | **~4-6 luni** | macOS production-ready |

---

## 2. Inventar stare curenta

### 2.1. Stack tehnologic

| Componenta | Tehnologie | Portabilitate macOS |
|-------------|------------|---------------------|
| Runtime UI | .NET 8 `net8.0-windows` + WPF | **Nu** - WPF este Windows-only |
| Design UI | WPF-UI 4.2 (FluentWindow, Mica) | **Nu** - depinde de WPF + DWM Windows |
| Motor principal | `fixsubs.ps1` via `powershell.exe` | **Partial** - PS7 (`pwsh`) merge pe Mac, dar trebuie bundlat/detectat |
| Motor secundar C# | `SubtitleNormalizer`, `RomanianDictionary` | **Da** - deja .NET standard |
| Motor C# nefolosit | `StandaloneSubtitleRepairer` | **Da** - nu e apelat din UI; logica duplicata partial in PS |
| Update | Velopack 0.0.1298, channel `win` | **Da** - Velopack suporta `osx` (arm64 + x64) |
| Distributie | `SubtitlesFixer-win-Setup.exe`, portable zip | Trebuie `.app` / `.dmg` / `.pkg` |
| Site | Static HTML + GitHub Pages | Trebuie sectiune download macOS |

### 2.2. Fisiere si dependinte Windows-specifice

| Fisier / zona | Blocaj | Actiune necesara |
|---------------|--------|------------------|
| `SubtitlesFixer.App.csproj` | `UseWPF`, `UseWindowsForms`, `net8.0-windows` | Proiect separat Mac sau Avalonia multi-target |
| `MainWindow.xaml` | `ui:FluentWindow`, `WindowBackdropType="Mica"` | Inlocuire cu Avalonia / eliminare Mica |
| `MainWindow.xaml.cs` | `powershell.exe`, `FolderBrowserDialog`, `MessageBox` WPF | Abstractii `IShellService`, `IScriptRunner` |
| `SubtitleSearchWindow.xaml.cs` | WinForms folder dialog, WPF controls | Port in Avalonia sau amanare feature Mac v2 |
| `App.xaml.cs` | `VelopackApp`, WPF `Application` | Entry point separat Mac |
| `app.manifest` | Windows compatibility XML | Ignorat pe Mac |
| `Properties/AssemblyInfo.cs` | `SupportedOSPlatform("Windows7.0")` | Atribute per-TFM |
| `UpdateService.cs` | `\\bin\\`, `\\obj\\` in path check | `Path.DirectorySeparatorChar` / helper cross-platform |
| `UserDataPaths.cs` | `ApplicationData` OK; portable flags OK | Pe Mac: `~/Library/Application Support/SubtitlesFixer` |
| `ScriptLocator.cs` | Extrage in `%TEMP%` | `Path.GetTempPath()` - OK cross-platform |
| `fixsubs.ps1` | .NET encodings 1250, 852 - disponibile pe .NET Mac | Test pe `pwsh`; ideal port C# |
| `.github/workflows/release-velopack.yml` | `windows-latest`, `win-x64` | Job `macos-14` matrix `osx-arm64`, `osx-x64` |
| `run_fixsubs.bat` | Windows batch | `run_fixsubs.sh` sau eliminare |

### 2.3. Fluxuri functionale de portat (paritate 1.0.9)

1. Selectare folder (dialog + drag & drop)
2. Analiza recursiva (preview JSON)
3. Plan UI (episoade, standalone `.srt`, alegeri manuale)
4. Rulare fix (backup, rename `.ro.srt`, encoding, diacritice)
5. Jurnal tehnic (stdout script)
6. Ultima rulare + restore selectiv
7. Setari persistente (`settings.json`)
8. Mod portable (`portable.flag` / `.portable`)
9. Update automat Velopack
10. Fereastra cautare subtitrari (`SubtitleSearchWindow`) - optional v2 Mac

---

## 3. Analiza motorului (`fixsubs.ps1`)

### 3.1. Ce face scriptul (module majore)

- Detectie si decodare encoding (UTF-8 BOM, UTF-16, Win-1250, ISO-8859-2, IBM852, Win-1252)
- Dictionar Hunspell `words_ro.gz` / `words_ro-RO.txt` (~960K cuvinte)
- Pipeline normalizare diacritice (markeri `?`, U+FFFD, backtick)
- Scoring bigram / trigram
- Scanare recursiva foldere video + `.srt`
- Match episod/film, redenumire `VideoName.ro.srt`
- Mod **standalone** subtitle (fara video)
- Backup in subfolder `backup`
- Export JSON: preview, summary, selection
- Restore din backup

### 3.2. Portabilitate PowerShell pe macOS

| Aspect | Windows (`powershell.exe` 5.1) | macOS (`pwsh` 7+) |
|--------|----------------------------------|-------------------|
| `[Text.Encoding]::GetEncoding(1250)` | Da | Da (.NET) |
| `Get-ChildItem -Recurse` | Da | Da |
| Cai fisier | `\` acceptat | Preferat `/` - scriptul foloseste `Join-Path` (OK) |
| Performanta dictionary load | Testat | De benchmark-uit pe Apple Silicon |
| Distributie | Preinstalat | Trebuie **bundlat** sau cerut userului |

**Concluzie:** scriptul este *probabil* rulabil pe Mac cu PS7, dar:

- nu este acceptabil sa ceri utilizatorului sa instaleze PowerShell manual pentru o app consumer;
- bundling `pwsh` + script creste masiv bundle-ul (~150-200 MB suplimentar);
- mentii doua surse de adevar (PS + C# partial).

### 3.3. Cod C# existent relevant pentru port

| Modul | Linii approx | Acoperire fata de PS |
|-------|--------------|---------------------|
| `SubtitleNormalizer.cs` | ~1000 | Pas 1-4 partial, fara dictionar complet in fluxul UI |
| `RomanianDictionary.cs` | ~40 | Incarcare dict - echivalent `Get-RoDictionary` |
| `StandaloneSubtitleRepairer.cs` | ~315 | Analiza/reparare folder `.srt` - **neconectat la UI** |
| `VideoNameParser.cs` | - | Parsing nume video - de verificat paritatea |
| `FixPlanPayloadParser.cs` | - | Compatibil JSON cross-platform |

**Prioritate faza 1:** extrage logica din `fixsubs.ps1` in `SubtitlesFixer.Core`, folosind `RomanianDictionary` + extindere `SubtitleNormalizer`, cu teste de regresie pe foldere reale.

---

## 4. Optiuni arhitecturale

### Optiunea A - Avalonia UI + Core C# (RECOMANDAT)

```
SubtitlesFixer.Core          (net8.0, class library)
SubtitlesFixer.Windows       (net8.0-windows, WPF - shell)
SubtitlesFixer.Mac           (net8.0, Avalonia - shell)
SubtitlesFixer.Core.Tests    (xUnit, golden files)
```

**Pro:**

- Un motor, doua UI-uri native ca experienta
- Avalonia suporta macOS, Windows, Linux (optional)
- JSON models deja in C#
- Velopack + .NET self-contained mature pe arm64

**Contra:**

- Efort migrare WPF -> shell subtire (4-6 sapt UI)
- Mica/Fluent trebuie reimplementate vizual in Avalonia (fara Mica real)

### Optiunea B - MAUI

**Pro:** Microsoft stack, un proiect multi-target  
**Contra:** Migrare mare din WPF-UI, macOS polish variabil, mai putin control decat Avalonia pentru desktop dense UI

### Optiunea C - macOS nativ (SwiftUI) + Core C# bindings

**Pro:** Cel mai bun UX Mac (menubar, sandbox, Shortcuts)  
**Contra:** Doua limbaje, P/Invoke sau gRPC catre Core, cost dublu mentenanta

### Optiunea D - MVP `pwsh` bundlat + UI minima Avalonia

**Pro:** Cel mai rapid la functionalitate (~8-12 sapt)  
**Contra:** Bundle urias, doua motoare, debugging dificil, datorie tehnica mare

### Optiunea E - Electron/Tauri + Core CLI

**Pro:** UI web rapid  
**Contra:** Pierde WPF existent, overhead runtime, nealiniat cu stackul curent

**Decizie recomandata:** **Optiunea A**, cu spike-uri din Optiunea D doar pentru validare risc PS pe Mac (saptamana 1).

---

## 5. Plan detaliat pe faze

### Faza 0 - Pregatire (1-2 saptamani)

**Obiective:**

- Confirma cerinte produs Mac (Intel + Apple Silicon, sandbox da/nu, Mac App Store da/nu)
- Spike: ruleaza `fixsubs.ps1` pe `macos-14` cu `pwsh` pe 3 foldere test
- Spike: `dotnet new avalonia.app` + Velopack hello-world pack osx
- Inventar golden tests: 20-50 fisiere `.srt` + 5 foldere seriale reale (anonimizate)

**Livrabile:**

- `docs/MACOS_SPIKE_RESULTS.md`
- Lista gap-uri PS vs C#
- Decizie finala A vs D

**Criterii trecere:**

- Cel putin un folder test procesat pe Mac (manual sau script)
- Confirmare ca Velopack `vpk pack` genereaza `.app` valid

---

### Faza 1 - SubtitlesFixer.Core (6-10 saptamani)

**Obiective:** inlocuieste `fixsubs.ps1` pentru toate fluxurile UI.

#### 1.1. Structura proiect

```
SubtitlesFixer.Core/
  Encoding/
    EncodingDetector.cs
    EncodingCandidate.cs
  Dictionary/
    RomanianDictionary.cs      (mutat)
  Repair/
    SubtitleNormalizer.cs        (mutat)
    SubtitleRepairPipeline.cs
    CombinatorialRepair.cs
    DiacriticCorrector.cs
  Scan/
    FolderScanner.cs
    VideoSubtitleMatcher.cs
    VideoNameParser.cs           (mutat)
  Workflow/
    AnalyzeService.cs
    RepairService.cs
    RestoreService.cs
  Models/                          (mutat din App)
  Serialization/                   (parsers mutati)
```

#### 1.2. Mapare functii PS -> C#

| PS (fixsubs.ps1) | C# tinta | Prioritate |
|------------------|----------|------------|
| `Decode-Best` | `EncodingDetector.Decode` | P0 |
| `Get-RoDictionary` | `RomanianDictionary` (existent) | P0 |
| `Normalize-RO` / repair pipeline | `SubtitleRepairPipeline` | P0 |
| `Score-TextForRO` | `EncodingScorer` | P0 |
| Scan video + `.srt` | `FolderScanner` | P0 |
| `Process-StandaloneSubtitle` | `StandaloneRepairService` | P0 |
| Backup / restore | `BackupService` | P0 |
| Preview JSON | `AnalyzeService.BuildPlan` | P0 |
| Summary JSON | `RepairService.BuildSummary` | P0 |
| Selection JSON | deja in UI - mutat in Core | P1 |

#### 1.3. Teste regresie

- **Golden file tests:** input `.srt` bytes -> output UTF-8 asteptat
- **Folder integration tests:** structura `{Show}/Season 01/S01E01...}` -> plan JSON comparat cu snapshot Windows
- **Performance:** incarcare dictionar < 3s pe M1, analiza 100 `.srt` < 30s

#### 1.4. Deprecare PowerShell in Windows app

- Inlocuieste `RunScriptAsync` cu `IRepairEngine` din Core
- Pastreaza `fixsubs.ps1` inca 1-2 release-uri behind flag `--legacy-ps` pentru rollback
- Elimina `EmbeddedResource fixsubs.ps1` dupa paritate confirmata

**Criterii trecere faza 1:**

- Windows app functioneaza 100% pe Core, zero apel `powershell.exe`
- Rezultate identice pe setul golden (toleranta: 0 diffs critice diacritice)

---

### Faza 2 - Abstractii platforma (1-2 saptamani)

```csharp
public interface IPlatformServices
{
    string AppDataDirectory { get; }
    bool IsPortableMode { get; }
    Task<string?> PickFolderAsync(CancellationToken ct);
    void OpenUrl(string url);
    Task ShowMessageAsync(string title, string message, MessageKind kind);
}

public interface IUpdateService
{
    Task<PreparedUpdate?> CheckForUpdatesAsync(CancellationToken ct);
    void Apply(PreparedUpdate update);
    bool IsInstalledDistribution { get; }
}
```

**Implementari:**

| Serviciu | Windows | macOS |
|----------|---------|-------|
| App data | `%APPDATA%\SubtitlesFixer` | `~/Library/Application Support/SubtitlesFixer` |
| Portable | `portable.flag` langa exe | `portable.flag` langa `.app/Contents/MacOS/` |
| Folder picker | WinForms (temporar) / Core wrapper | Avalonia `StorageProvider` |
| Update | Velopack `win` | Velopack `osx` |

**Refactor fisier:**

- `UserDataPaths.cs` -> foloseste `IPlatformServices`
- `UpdateService.cs` -> elimina check `\\bin\\`
- `MainWindow.xaml.cs` -> nu mai refera WinForms direct

---

### Faza 3 - UI macOS cu Avalonia (4-6 saptamani)

#### 3.1. Ecran MVP (paritate cu Windows 1.0.9)

1. Title bar custom (fara Mica - gradient dark similar site-ului)
2. Selectare folder + drag & drop
3. Checkbox recursiv / overwrite `.ro.srt`
4. Tab-uri: Plan | Ultima rulare | Jurnal
5. Butoane: Analiza, Ruleaza fix, Restore
6. Lista plan cu chip-uri status (echivalent `_currentPlan` UI)
7. Progress bar + status text
8. Meniu: Verifica update, Despre, Iesire

#### 3.2. De amanat pe Mac v1.1+

- `SubtitleSearchWindow` (SubDL / provideri online)
- Mica / acrylic exact Windows 11
- Toate animatiile WPF-UI

#### 3.3. Localizare

- Reutilizeaza cheile din `i18n.js` conceptual in `.resx` sau JSON `locales/ro.json`, `en.json`
- macOS: respecta limba sistemului (ro vs en) - aceeasi logica ca site-ul

#### 3.4. Drag & drop pe Mac

- Avalonia `DragDrop` pe fereastra principala
- Accepta `file://` URI-uri folder

**Criterii trecere faza 3:**

- Utilizator Mac poate analiza + repara + restaura un folder serial fara terminal
- UI usor de folosit pe 1280x800 si pe MacBook Air screen

---

### Faza 4 - CI/CD si distributie (1-2 saptamani)

#### 4.1. Workflow GitHub Actions

```yaml
# .github/workflows/release-velopack.yml (extins)
jobs:
  release-windows:
    runs-on: windows-latest
    # existent: win-x64

  release-macos:
    strategy:
      matrix:
        rid: [osx-arm64, osx-x64]
    runs-on: macos-14
    steps:
      - dotnet publish SubtitlesFixer.Mac -c Release -r ${{ matrix.rid }} --self-contained
      - vpk pack --packId SubtitlesFixer --packVersion $version --packDir publish --mainExe SubtitlesFixer
      - vpk upload github --channel osx --merge ...
```

**Canal Velopack:**

- Pastreaza `win` separat
- Adauga `osx` (Velopack gestioneaza arm64/x64 in `releases.osx.json`)

#### 4.2. Artefacte release GitHub

| Fisier | Platforma |
|--------|-----------|
| `SubtitlesFixer-win-Setup.exe` | Windows (existent) |
| `SubtitlesFixer-win-Portable.zip` | Windows (existent) |
| `SubtitlesFixer-macOS-arm64.dmg` | Apple Silicon (nou) |
| `SubtitlesFixer-macOS-x64.dmg` | Intel Mac (nou) |
| optional: `SubtitlesFixer-macOS-universal.dmg` | Universal Binary (v2) |

#### 4.3. Versiune unificata

- Acelasi `Version` din `SubtitlesFixer.App.csproj` / `Directory.Build.props`
- Tag `v*` declanseaza ambele joburi; esec pe un RID blocheaza release (ca la fix 1.0.9)

---

### Faza 5 - Semnare, notarizare, distributie (1-2 saptamani)

**Cerinte Apple (app distribuita in afara Mac App Store):**

1. **Developer ID Application** certificate (Apple Developer Program - 99 USD/an)
2. **Code signing** toate binarele din `.app`
3. **Hardened Runtime** + entitlements (acces fisiere user-selected)
4. **Notarization** (`notarytool submit`) + stapling
5. **DMG** semnat pentru download

**Entitlements tipice (desktop tool local):**

- `com.apple.security.cs.allow-unsigned-executable-memory` - evitat daca posibil
- Fara sandbox App Store initial -> acces direct la foldere alese de user
- Daca sandbox: doar **security-scoped bookmarks** pentru foldere drag/drop

**Secrets CI:**

- `APPLE_CERTIFICATE_P12` (base64)
- `APPLE_CERTIFICATE_PASSWORD`
- `APPLE_ID`, `APPLE_APP_SPECIFIC_PASSWORD`
- `KEYCHAIN_PASSWORD`

**Gatekeeper UX:**

- Prima deschidere: click dreapta -> Open (pana la notarizare stapled)
- Dupa notarizare: dublu-click normal

---

### Faza 6 - QA si lansare (2-3 saptamani)

#### 6.1. Matrice test

| Mediu | Versiune macOS | Chip | Teste |
|-------|----------------|------|-------|
| VM | Sonoma 14 | arm64 | Install, analiza, fix, restore |
| HW | Ventura 13 | x64 | Acelasi |
| HW | Sequoia 15 | arm64 | Update Velopack |
| Parallels | Windows vs Mac same folder | - | Compatibilitate `.ro.srt` |

#### 6.2. Cazuri limita

- Foldere pe iCloud Drive / NAS SMB
- Fisiere doar citire
- Nume fisiere cu diacritice in path (`/Users/.../Müller/`)
- Subtitrari foarte mari (>5 MB)
- Anulare operatiune la jumatate
- Lipsa permisiuni folder

#### 6.3. Beta

- Release `v1.1.0-beta1` cu canal GitHub pre-release
- Feedback form in app + issue template `macOS`

#### 6.4. Site & docs

- `index.html`: buton download macOS (detectare `navigator.platform`)
- `docs/release-v1.x.md` cu instructiuni Gatekeeper
- README: sectiune macOS

---

## 6. Modificari repository (checklist)

### Proiecte noi

- [ ] `SubtitlesFixer.Core/SubtitlesFixer.Core.csproj`
- [ ] `SubtitlesFixer.Mac/SubtitlesFixer.Mac.csproj` (Avalonia)
- [ ] `SubtitlesFixer.Core.Tests/`
- [ ] `Directory.Build.props` (versiune centralizata)

### Refactor existent

- [ ] `SubtitlesFixer.App` -> redenumit `SubtitlesFixer.Windows` sau pastrat nume cu referinta la Core
- [ ] Elimina `UseWindowsForms` dupa port dialog Avalonia pe Windows (optional)
- [ ] `ScriptLocator.cs` - deprecat post-Core
- [ ] `fixsubs.ps1` - arhivat in `legacy/` sau mentinut doar pentru debug

### CI/CD

- [ ] Job `release-macos` in workflow
- [ ] Cache `words_ro.gz` / verificare checksum
- [ ] Release notes template bilingual

### Documentatie

- [x] `docs/MACOS_PORT_PLAN.md` (acest document)
- [ ] `docs/MACOS_BUILD.md` (build local pe Mac)
- [ ] `docs/MACOS_SIGNING.md` (notarizare pas cu pas)

---

## 7. Riscuri si mitigari

| Risc | Impact | Probabilitate | Mitigare |
|------|--------|---------------|----------|
| Paritate incompleta PS vs C# | Regresii diacritice | Medie | Golden tests, beta lunga |
| Performanta dict pe Mac ARM | UX lent | Medie | Lazy load, mmap, benchmark |
| Notarizare esuata | Blocaj distributie | Medie | Documentatie signing, cont dev activ |
| WPF-UI gap vizual | Recenzii negative | Mare | Design Avalonia apropiat de site, nu copie Mica |
| Scope creep (Subtitle Search pe Mac) | Intarziere | Mare | Mac v1 fara cautare online |
| Velopack osx channel conflict | Update stricat | Mica | Channel separat `osx`, teste incrementale |
| Cale fisier NFC Unicode (macOS) | Match episod gresit | Medie | Normalizare NFC in `VideoNameParser` |

---

## 8. Criterii de succes (definition of done)

1. **Functional:** Analiza + Fix + Restore + Backup pe macOS 13+ (arm64 si x64)
2. **Calitate:** 0 regressions pe setul golden fata de Windows 1.0.9
3. **Distributie:** DMG notarizat, update Velopack functional
4. **UX:** Fara terminal, fara instalare manuala PowerShell/.NET de catre user
5. **Marime:** Target bundle < 120 MB per arch (self-contained .NET 8)
6. **Legal:** MIT + Privacy/Terms actualizate (macOS paths mentionate)

---

## 9. Roadmap versiuni propus

| Versiune | Continut |
|---------|----------|
| **1.1.0-beta** | Core C# pe Windows (feature flag), spike Mac intern |
| **1.1.0** | Windows pe Core only, fara PS |
| **1.2.0-beta** | macOS Avalonia MVP, fara auto-update |
| **1.2.0** | macOS + Velopack osx + DMG notarizat |
| **1.3.0** | Subtitle Search pe Mac, universal binary optional |
| **2.0.0** | UI unificat Avalonia si pe Windows (optional) |

---

## 10. Appendix A - Detaliu tehnic `RunScriptAsync` (inlocuire)

**Curent** (`MainWindow.xaml.cs`):

```csharp
FileName = "powershell.exe",
// -File fixsubs.ps1 -Paths {folder} -PreviewJsonPath ...
```

**Tinta:**

```csharp
await _engine.AnalyzeAsync(new AnalyzeRequest(folder, recurse, overwriteRo, selection), ct);
await _engine.RepairAsync(new RepairRequest(...), progress, ct);
```

Progress reporting: `IProgress<EngineLogLine>` in loc de stdout parsing.

---

## 11. Appendix B - Cai si date utilizator macOS

| Scop | Windows | macOS |
|------|---------|-------|
| Config instalat | `%APPDATA%\SubtitlesFixer` | `~/Library/Application Support/SubtitlesFixer` |
| Portable data | `{exeDir}\data` | `{MacOS}/data` in bundle sau langa `.app` |
| Temp JSON plan | `%TEMP%` | `/var/folders/...` via `Path.GetTempPath()` |
| Backup | `{folder}\backup` | Acelasi, relativ la folder ales |

---

## 12. Appendix C - Resurse externe

- [Avalonia UI](https://avaloniaui.net/) - cross-platform XAML
- [Velopack docs](https://docs.velopack.io/) - packaging osx
- [Apple notarization](https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution)
- [PowerShell on macOS](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-macos) - doar pentru spike
- [.NET 8 macOS RID](https://learn.microsoft.com/dotnet/core/rid-catalog) - `osx-arm64`, `osx-x64`

---

## 13. Urmatorul pas imediat (saptamana 1)

1. Creaza issue GitHub `Epic: macOS port` cu link la acest plan
2. Ruleaza spike `pwsh ./fixsubs.ps1` pe `macos-14` runner (workflow manual `workflow_dispatch`)
3. Creaza branch `feature/macos-core-spike`
4. Extrage `RomanianDictionary` + test golden pentru 10 fisiere `.srt`
5. Estimeaza din nou dupa spike (ajusteaza timeline 4-6 luni)

---

*Document intocmit pentru repository-ul SubtitlesFixer. Actualizeaza versiunea documentului la fiecare schimbare majora de scope.*
