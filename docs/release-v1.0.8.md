# Subtitles Fixer v1.0.8

- analiză și reparare mult mai rapide pentru foldere cu doar subtitrări
- motorul PowerShell a redevenit fluxul principal pentru scanare și reparare, cu comportament mai stabil
- repară mai corect cazuri precum `pre?` → `preț`, `ora?` → `oraș`, `?i-a` → `ți-a`, `?i-am` → `ți-am`, `?i-ai` → `ți-ai`
- normalizează și liniile de dialog de la început de rând: `- Salut` devine `-Salut`
- progresul pentru un singur fișier nu mai afișează un procent fals până la final
- aplicația și pagina de prezentare au fost ajustate pentru un flux Windows mai clar

## Instalare

- **Setup:** descarcă `SubtitlesFixer-win-Setup.exe` pentru instalare normală și update automat
- **Portable:** descarcă `SubtitlesFixer-win-Portable.zip` și extrage arhiva oriunde vrei

## Notă

Subtitles Fixer rulează local pe Windows și mută automat sursele în `backup` înainte de rescriere.
