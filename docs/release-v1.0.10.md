# Subtitles Fixer v1.0.10

Actualizare mare de fiabilitate: analiză rapidă fără blocaje, suport real pentru
filme, control mai bun în interfață, teste automate de regresie și automatizare
completă a versiunii. Plus fundal dark stabil pe site (desktop și iPhone).

## Performanță și analiză

- **Preview rapid pentru subtitrări standalone** — analiza nu mai face normalizare
  completă doar ca să decidă dacă „se schimbă". Dicționarul românesc (~960K cuvinte)
  nu se mai încarcă în faza de analiză, ci doar la repararea efectivă.
- **Blocajul eliminat** — fișiere precum `Boiler.Room.2000...ro.srt` nu mai blochează
  analiza. Pe un folder cu 11 elemente analiza durează acum sub o secundă.
- **`?` la final de cuvânt** este tratat ca punctuație, nu ca diacritică lipsă (era
  ambiguu și costisitor pe fișiere cu multe întrebări). Sincronizat în PowerShell și C#.

## Detecție filme

- **Suport nou pentru filme fără pattern `SxxEyy`** — detecție titlu și an, plus
  potrivire automată a subtitrării prin scoring (titlu, an, acoperire de cuvinte).
- **Anii de film (1900–2099) nu mai sunt confundați cu episoade** — `Boiler.Room.2000`
  este recunoscut ca film, nu ca episodul 2000. Corecție aplicată identic în C#
  (`VideoNameParser`) și în `fixsubs.ps1`.
- **Tip media (film/serial) propagat consistent** prin tot fluxul: script → interfață,
  subtitrări standalone și căutarea online (SubDL). Etichetele din UI reflectă corect
  „Film" vs „Episod".

## Interfață și control

- **Buton „Oprește"** pentru anularea analizei sau rulării în curs — oprește procesul
  și afișează stare clară, fără eroare falsă de aplicație.
- **Progres real în taskbar-ul Windows** (bară + stare), pe lângă progresul din fereastră.
- **Progres corect `N/N`** — s-a reparat cazul în care progresul rămânea blocat la
  „0 din 11"; indexul afișat este acum consistent de la început până la final.
- **Drag & drop acceptă și fișiere**, nu doar foldere.

## Fiabilitate și release

- **Script rulat din resursa embedded** — `fixsubs.ps1` este extras din aplicație,
  nu citit dintr-un fișier de lângă exe care poate fi modificat după instalare
  (fișierul side-by-side rămâne doar ca fallback).
- **User-Agent SubDL** derivat din versiunea reală a aplicației, nu dintr-un string fix.
- **Teste automate de regresie** pentru parser, normalizer și preview-ul PowerShell,
  fără dependențe externe (rulate și în CI înainte de publicare).
- **Versiune centralizată** în `version.json` + `scripts/sync-version.ps1`, cu verificare
  automată în workflow.
- **Workflow Velopack** mai sigur: citește versiunea din `version.json`, rulează testele
  înainte de publish și eșuează clar dacă versiunea există deja în channel.
- Artefactele mari (`.exe`/`.zip`) au fost scoase din tracking-ul git; rămân disponibile
  doar în GitHub Releases.

## Site

- **Fundal dark stabil pe tot viewport-ul**, pe desktop și pe iPhone/Safari: `html`/`body`
  pe `#09090e`, `color-scheme: dark` și gradient pe un strat `::before` fix — fără mai
  apărea zona albă la overscroll.

## Instalare

- **Setup:** descarcă `SubtitlesFixer-win-Setup.exe` pentru instalare normală și update automat.
- **Portable:** descarcă `SubtitlesFixer-win-Portable.zip` și extrage arhiva oriunde vrei.
