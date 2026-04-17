# Subtitles Fixer

[![GitHub Release](https://img.shields.io/github/v/release/cosmintrica/SubtitlesFixer?style=for-the-badge&color=6366f1)](https://github.com/cosmintrica/SubtitlesFixer/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/cosmintrica/SubtitlesFixer/total?style=for-the-badge&color=22c55e)](https://github.com/cosmintrica/SubtitlesFixer/releases/latest)
[![License](https://img.shields.io/github/license/cosmintrica/SubtitlesFixer?style=for-the-badge&color=8b5cf6)](LICENSE)

**Subtitles Fixer** este un utilitar modern pentru Windows care automatizeaza repararea si organizarea subtitrarilor tale. Include cel mai avansat algoritm de reparare a diacriticelor romanesti - un pipeline de 5 pasi bazat pe un dictionar complet de **960.000 cuvinte** generat din Hunspell ro_RO, cu scoring bigram/trigram si validare combinatorica. Repara encoding-ul, scrie `.ro.srt`, muta sursele in backup, suporta drag & drop si primeste update automat din GitHub Releases.

---

## Caracteristici principale

- **Algoritm Avansat de Reparare** - Dicționar de 960K cuvinte românești cu toate formele flexionare. Fiecare marker (`?`, `�`, `` ` ``) este înlocuit cu toate combinațiile de ș, ț, ă, â, î și validat în dicționar. Scoring bigram + trigram pentru cazurile ambigue.
- **Reparare Encoding** - Converteste automat fisierele din formate vechi (Win-1250, ISO-8859-2, CP852) in UTF-8, reparand toate diacriticele.
- **Organizare Automata** - Redenumeste fisierele `.srt` in `.ro.srt` si le plaseaza langa fisierul video corespunzator.
- **Drag & Drop** - Trage orice folder sau fisier direct in fereastra. Fara dialog, fara click-uri inutile.
- **Backup Automat** - Subtitrarile originale si variantele vechi sunt mutate automat intr-un folder de backup. Nimic nu dispare.
- **Analiza Ierarhica** - Grupeaza inteligent serialele si filmele si ofera un preview clar al tuturor schimbarilor inainte de procesare.
- **Reparare Fara Video** - Butonul `Repara subtitrari` curata `.srt` direct din folder chiar daca nu exista fisier video pentru ele.
- **Corectare Diacritice Gresite** - Detecteaza si repara automat ș↔ț, ă↔â↔î greșit plasate, verificand fiecare varianta in dictionar.
- **Restore Instant** - Revii oricand la starea initiala cu un singur click.
- **Update Automat** - Versiunile instalate prin pachetul de release verifica GitHub Releases si pot instala update-ul automat.
- **Design Modern** - Interfata Fluent UI cu Mica backdrop, animatii fluide si dark mode nativ.
- **100% Local** - Ruleaza complet pe PC-ul tau. Fara cloud, fara cont, fara trackere.

---

## Cum il folosesti

1. **Descarca** ultima versiune de pe [Releases](https://github.com/cosmintrica/SubtitlesFixer/releases/latest):
   - **Setup** (`CosminTrica.SubtitlesFixer-win-Setup.exe`) - instaleaza aplicatia cu update automat.
   - **Portable** (`CosminTrica.SubtitlesFixer-win-Portable.zip`) - extrage oriunde, fara instalare.
   - Celelalte fisiere (`.nupkg`, `RELEASES`, `releases.win.json`) sunt interne - folosite de sistemul de auto-update.
2. **Ruleaza** `SubtitlesFixer.exe`.
3. **Selecteaza** folderul (sau trage-l direct in fereastra).
4. Apasa **Analiza** pentru a vedea exact ce se va schimba.
5. Apasa **Ruleaza fix** - backup e facut, `.ro.srt` e scris, diacriticele sunt corecte.
6. Daca ai doar fisiere `.srt` fara video, foloseste **Repara subtitrari** si aplicatia le repara in loc, pastrand backup.

## Algoritmul de reparare

Subtitles Fixer foloseste un pipeline de **5 pasi** — cel mai avansat sistem de reparare pentru subtitrari romanesti:

| Pas | Ce face |
|-----|---------|
| **0** | Normalizare CRLF |
| **1** | Reparare mojibake (secvente UTF-8 decodate gresit) |
| **2** | Conversie cedilla → comma (ş→ș, ţ→ț) |
| **3** | Reparare markeri (`?`, `�`, `` ` ``) — incearca toate combinatiile de ș, ț, ă, â, î si valideaza in dictionar. Fallback pe scoring bigram daca dictionarul nu decide. |
| **4** | Corectare circumflex la inceput de cuvant |
| **5** | Corectare diacritice gresite — detecteaza ș↔ț, ă↔â↔î plasate incorect si le repara pe baza dictionarului |

Dictionarul contine **~960.000 cuvinte** romanesti, generat din Hunspell `ro_RO` cu toate formele flexionare, comprimat GZip (~2.3 MB) si incarcat la pornire.

---

## Update automat

- Feed-ul de update este legat de **GitHub Releases** prin **Velopack**.
- Pentru utilizator, fluxul corect este simplu: descarci installer-ul sau build-ul de release din GitHub, instalezi aplicatia, apoi versiunile noi sunt verificate automat la pornire.
- Pentru publicare, workflow-ul din [`.github/workflows/release-velopack.yml`](.github/workflows/release-velopack.yml) genereaza si urca artefactele Velopack necesare pentru update.

> [!NOTE]
> **Cat ocupa aplicatia?**
> Build-ul curent self-contained pentru `win-x64` are aproximativ **71 MB**. Am ales sa o distribuiesc in modul **self-contained**, adica runtime-ul .NET necesar este inclus deja in executabil.
>
> Asta inseamna ca merge direct pe Windows 10/11 fara sa mai ceara instalarea separata a `.NET Desktop Runtime`.

---

## Tehnologii

- **C# / .NET 8 + WPF** - Aplicatia desktop cu interfata Fluent UI (Mica backdrop, dark mode nativ).
- **WPF-UI** - Fluent Design System cu animatii fluide si integrare vizuala moderna pe Windows 10/11.
- **Hunspell ro_RO** - Dictionar de 960K cuvinte cu toate formele flexionare ale limbii romane.
- **Velopack** - Update automat din GitHub Releases, fara interventia utilizatorului.
- **PowerShell** - Motor performant pentru manipularea fisierelor si detectarea encoding-ului.
- **Local-First** - Nicio conexiune la internet, nicio colectare de date.

---

## Sustine proiectul

Daca acest tool ti-a economisit timp:

- [Revolut](https://revolut.me/mtvtrk)
- [Stripe - Card/Apple/Google Pay](https://donate.stripe.com/eVq8wI9m9aOTcP88fv3VC01)

---

## Contact

Creat de **Cosmin Trica**
- [LinkedIn](https://www.linkedin.com/in/cosmintrica/)
- [GitHub](https://github.com/cosmintrica)

---

*Licenta: MIT*
