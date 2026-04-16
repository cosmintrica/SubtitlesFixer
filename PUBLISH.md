# Publicare Subtitles Fixer (Windows)

Versiunea curenta de release: `1.0.0`
Producator: `Cosmin Trica`

Cerințe: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (pentru `dotnet build` / `dotnet publish`). Interfața folosește pachetul NuGet **WPF-UI** (lepoco; numele corect pe nuget.org este `WPF-UI`, nu vechiul `Wpf.Ui`).

## Build local (Debug)

Din rădăcina repo-ului (folderul care conține `SubtitlesFixer.sln`):

```powershell
dotnet build SubtitlesFixer.sln -c Debug
```

Rulare:

```powershell
dotnet run --project SubtitlesFixer.App\SubtitlesFixer.App.csproj
```

## Publish single-file (win-x64)

### Self-contained (fără runtime .NET instalat pe PC)

Exe mai mare; nu cere .NET Desktop Runtime separat.

```powershell
dotnet publish SubtitlesFixer.App\SubtitlesFixer.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true
```

Output tipic: `SubtitlesFixer.App\bin\Release\net8.0-windows\win-x64\publish\SubtitlesFixer.App.exe` (și fișiere extra dacă nu e strict un singur fișier - verifică folderul `publish`).

### Framework-dependent (exe mai mic)

Necesită [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) instalat pe mașina țintă.

```powershell
dotnet publish SubtitlesFixer.App\SubtitlesFixer.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true
```

Aplicația încarcă `fixsubs.ps1` din același folder cu exe când e copiat acolo (Item `Content`); în plus, resursa încorporată permite extragere în `%TEMP%` dacă lipsește fișierul lângă exe (ex. anumite moduri de ambalare single-file).

## Opțional: MSI cu WiX

Pentru instalare clasică (shortcut, Add/Remove Programs), folosește [WiX Toolset v4+](https://wixtoolset.org/) sau [HeatWave](https://www.firegiant.com/wix/) în Visual Studio: creează un proiect WiX care empaquetă output-ul din `publish` (exe + `fixsubs.ps1` dacă nu e deja în bundle), definește `Product`, `Directory`, `Component`, `Shortcut`, `MajorUpgrade`. Detaliile depind de versiunea WiX și de semnarea pachetului; nu sunt incluse aici fiindcă sunt specifice mediului de build.

## Opțional: MSIX

Pentru distribuție prin Microsoft Store sau sideload cu certificat, generează un pachet MSIX din același `publish` (Packaging Project în Visual Studio sau `msbuild /t:publish` pe proiect Windows Application Packaging). Necesită configurare de identitate și semnare.

## Recomandare realista pentru installer + update

Pentru aplicația asta, ruta pragmatică este:

1. `publish` self-contained `win-x64`
2. împachetare într-un installer Windows
3. upload-ul fiecărei versiuni într-un loc stabil
4. pagina publică de lansare care trimite la download

Pentru update ai două variante serioase:

- `MSIX + App Installer`
  Bun dacă vrei experiență foarte "Windows" și ai certificat de semnare. Update-ul vine din feed-ul MSIX.
- `Velopack / Squirrel-style updater`
  Mai potrivit pentru o aplicație indie distribuită direct de pe site sau din GitHub Releases. Poți publica fiecare versiune nouă și aplicația verifică feed-ul de update.

Pentru cazul tău, alegerea recomandată este:

- `Installer:` MSI sau setup Velopack
- `Hosting release-uri:` GitHub Releases sau un bucket / storage simplu
- `Landing page:` Vercel

Motivul: Vercel este excelent pentru pagina produsului, dar nu este cel mai comod loc pentru un flux complet de update desktop cu pachete versionate. Cel mai simplu este să ții pagina pe Vercel și fișierele de release separat.

## De ce am nevoie ca să fac installer-ul complet

Detaliile esențiale sunt:

- `Nume produs:` Subtitles Fixer
- `Versiune:` 1.0.0
- `Producator / Company:` Cosmin Trica
- `Copyright:` Copyright © 2026 Cosmin Trica. All rights reserved.

Detaliile care încă lipsesc și chiar contează:

- `Icon final (.ico)` pentru exe și installer
- `URL final de download` pentru butonul mare de pe site
- `URL final pentru donate` (PayPal, Buy Me a Coffee, Stripe Payment Link etc.)
- `Locul unde publici release-urile`:
  GitHub Releases, domeniu propriu, storage bucket sau alt host
- `Certificat de code signing` dacă vrei să reduci avertismentele SmartScreen și să ai o experiență curată de instalare

Opționale, dar utile:

- `email de suport`
- `domeniu public`
- `1-3 screenshot-uri reale ale aplicației`

## Cum faci update după ce versiunea 1.0.0 e live

Fluxul sănătos este:

1. crești versiunea în `SubtitlesFixer.App.csproj`
2. faci `dotnet publish` Release
3. generezi noul installer / pachet de update
4. urci artefactele noii versiuni
5. actualizezi butonul de download sau feed-ul de update
6. publici notițele de release

Practic:

- `update manual:` utilizatorul descarcă noul installer de pe site
- `update automat:` aplicația citește un feed de versiuni și descarcă singură noul pachet

Dacă vrei varianta cea mai simplă de livrat repede:

- lansare `v1.0.0` cu installer și download manual
- apoi adăugăm fluxul de auto-update în pasul 2
