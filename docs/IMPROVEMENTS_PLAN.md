# Plan de imbunatatiri - Subtitles Fixer

**Versiune document:** 1.0  
**Data:** Iunie 2026  
**Stare curenta:** v1.0.10 (live, "Latest")  
**Status:** propunere - nimic inceput (notat la cererea utilizatorului pe 2026-06-03)

---

## 1. Rezumat

v1.0.10 repara deja un set larg de probleme de subtitrari (encoding, cedile,
diacritice cu marker, `â`→`î`, note muzicale, line endings, spatii in coada) si
detecteaza filme/seriale. Documentul listeaza urmatorul nivel de imbunatatiri,
in ordinea impactului, fiecare cu abordare, risc si criterii de acceptare.

Regula: **release nou (v1.0.11) doar cand exista o schimbare reala in aplicatie**
(`SubtitlesFixer.App` sau `fixsubs.ps1`), nu pentru CI/teste.

---

## 2. Ce repara deja vs. ce NU (confirmat prin test pe 2026-06-03)

**Repara corect:** marker `?` / `` ` `` / `U+FFFD` → diacritice (via dictionar ~960K
cuvinte); cedila→virgula (`ş ţ`→`ș ț`); `â`→`î` la inceput de cuvant; reguli de
context (`-?i`→`-ți`); note muzicale; capitalizare la inceput de fraza; encoding
(Win-1250 / ISO-8859-2 / IBM852 / Win-1252 → UTF-8 fara BOM); LF/CR→CRLF; spatii
in coada; BOM.

**NU repara (prin design):**
1. Diacritice lipsa **fara marker** - text ASCII curat (`deasa`→ar trebui `deasă`).
2. `?` singur la final de cuvant (`c?` ramane `c?`) - intentionat, e fix-ul
   anti-blocaj din v1.0.10.

Test de regresie permanent: `SubtitlesFixer.Tests/Program.cs` (apply-repair).

---

## 3. Directii de imbunatatire

### 3.1. Restaurare diacritice inteligenta (fara marker)

**Scop:** repara diacriticele lipsa chiar cand textul e ASCII curat, fara `?`
(ex. `deasa`→`deasă`, `tara`→`țară`, `inseamna`→`înseamnă`).

**Abordare:**
- Tokenizare; pentru fiecare cuvant ASCII care **nu** e cuvant romanesc valid in
  dictionar, genereaza variantele cu diacritice si verifica in dictionar.
- Inlocuieste **doar** cand exista o singura varianta valida; altfel dezambiguizare
  pe context (bigrame existente) / frecventa, sau lasa neschimbat.
- Lista de exceptii pentru cuvinte care sunt valide si cu, si fara diacritice.

**Risc:** ridicat - poate strica cuvinte corecte (`fata`→`față`/`fată`, nume proprii,
cuvinte englezesti din titluri). Mitigare: prag conservator + teste extinse pe
fixture-uri reale + mod "doar sugereaza" optional.

**Acceptare:** fixture cu >50 cuvinte (reparabile + capcane), 0 regresii pe cuvinte
corecte, plus testul existent apply-repair verde.

### 3.2. Migrarea motorului de reparare in C#

**Scop:** elimina divergenta C#/PowerShell (riscul #1 de mentenanta) si dependenta
de `powershell.exe` la runtime.

**Abordare (pe etape, cu paritate verificata):**
1. Port `Normalize-RO` (cedile, markeri+dictionar, `â`→`î`, note, CRLF) in C#.
2. Port incarcarea dictionarului (`words_ro.gz`) + detectia de encoding (`Decode-Best`).
3. Apeleaza motorul C# din UI; pastreaza `fixsubs.ps1` doar ca fallback/CLI legacy.
4. Dupa paritate completa, retrage scriptul din fluxul principal.

**Beneficii:** mai rapid (fara spawn + reincarcarea a ~960K cuvinte/rulare),
testabil direct (fara shell-out), o singura sursa de adevar.

**Risc:** mediu-ridicat (refactor mare). Mitigare: teste de paritate care compara
output C# vs `fixsubs.ps1` pe acelasi set de fisiere, etapa cu etapa.

**Acceptare:** pentru un corpus de fixture, output C# === output PowerShell, byte cu byte.

### 3.3. Tipuri noi de reparatii

**Scop:** acopera probleme pe care app-ul nu le atinge acum.

| Problema | Reparatie |
|----------|-----------|
| Timestamp-uri suprapuse / in ordine gresita | reordonare / clamp la urmatorul cue |
| Cue-uri cu durata 0 sau negativa | durata minima / eliminare |
| Numerotare lipsa, sarita sau dublata | renumerotare 1..N |
| Tag-uri HTML / formatare reziduale (`<i>`, `{\an8}`) | curatare optionala |
| Spatiere dubla, linii prea lungi | normalizare spatii / wrap |
| Cue-uri goale | eliminare |

**Risc:** scazut-mediu (fiecare regula e izolata si optionala).

**Acceptare:** fixture per regula; fiecare reparatie are toggle in UI (sa nu strice
fisiere deja corecte).

---

## 4. Prioritizare si efort (orientativ)

| Prioritate | Directie | Impact utilizator | Risc | Efort |
|-----------|----------|-------------------|------|-------|
| 1 | 3.1 Diacritice inteligente | mare (calitate vizibila) | ridicat | mediu |
| 2 | 3.3 Reparatii noi | mediu (acoperire) | scazut | mediu |
| 3 | 3.2 Motor in C# | indirect (mentenanta/viteza) | mediu-ridicat | mare |

Ordine recomandata: 3.1 → 3.3 → 3.2 (intai valoare vizibila, apoi acoperire, apoi
rearhitecturarea care le sustine pe toate). 3.2 este si premisa pentru portarea
macOS (vezi `MACOS_PORT_PLAN.md`).

---

## 5. Proces de release (memento)

1. Bump `version.json`.
2. `scripts/sync-version.ps1` (sincronizeaza csproj, AssemblyInfo, site, XAML, index.html).
3. Commit + push pe `main`.
4. Tag `vX.Y.Z` + push → workflow-ul `release-velopack.yml` ruleaza testele, publica
   si genereaza release-ul Velopack (Setup.exe, Portable.zip, delta update).
5. Note de release complete in `docs/release-vX.Y.Z.md` + `gh release edit --notes-file`.
