# Subtitles Fixer

[![GitHub Release](https://img.shields.io/github/v/release/cosmintrica/SubtitlesFixer?style=for-the-badge&color=6366f1)](https://github.com/cosmintrica/SubtitlesFixer/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/cosmintrica/SubtitlesFixer/total?style=for-the-badge&color=22c55e)](https://github.com/cosmintrica/SubtitlesFixer/releases/latest)
[![License](https://img.shields.io/github/license/cosmintrica/SubtitlesFixer?style=for-the-badge&color=8b5cf6)](LICENSE)

**Subtitles Fixer** este un utilitar modern pentru Windows care automatizeaza repararea si organizarea subtitrarilor tale. Spune adio diacriticelor lipsa sau fisierelor ratacite prin foldere.

---

## Caracteristici principale

- **Reparare Encoding** - Converteste automat fisierele din formate vechi (Win-1250, ISO-8859) in UTF-8, reparand caracterele speciale si diacriticele.
- **Organizare Automata** - Redenumeste fisierele `.srt` in `.ro.srt` si le plaseaza langa fisierul video corespunzator.
- **Backup Automat** - Subtitrarile originale si variantele vechi sunt mutate automat intr-un folder de backup. Nimic nu dispare.
- **Analiza Ierarhica** - Grupeaza inteligent serialele pe sezoane si ofera un preview clar al tuturor schimbarilor inainte de procesare.
- **Restore Instant** - Revii oricand la starea initiala cu un singur click.
- **100% Local** - Ruleaza complet pe PC-ul tau. Fara cloud, fara cont, fara trackere.

---

## Cum il folosesti

1. **Descarca** ultima versiune de pe [Releases](https://github.com/cosmintrica/SubtitlesFixer/releases/latest).
2. **Ruleaza** `SubtitlesFixer.exe`.
3. **Selecteaza** folderul care contine filmele/serialele tale.
4. Apasa **Analiza** pentru a vedea exact ce se va schimba.
5. Apasa **Run Fix** - backup e facut, `.ro.srt` e scris, diacriticele sunt corecte.

> [!NOTE]
> **De ce are aplicația ~160 MB?**
> Am ales să o distribui în modul **„Self-Contained”**. Aceasta înseamnă că tot ecosistemul Microsoft .NET este complet integrat în acel fișier.
>
> Astfel, aplicația ta merge direct din prima secundă, fără ecrane plictisitoare de instalare auxiliare (cum ar fi *"You need to install .NET Desktop Runtime"*), care deranjează experiența oricărui utilizator.

---

## Tehnologii

- **C# / WPF** - Interfata moderna cu design Fluent UI (Windows 11 style).
- **PowerShell** - Motor performant pentru manipularea fisierelor si detectarea encoding-ului.
- **Local-First** - Nicio conexiune la internet, nicio colectare de date.

---

## Sustine proiectul

Daca acest tool ti-a economisit timp:

- [Revolut](https://revolut.me/mtvtrk)
- [Stripe - Card/Apple/Google Pay](https://donate.stripe.com/cNi4gs9dB8wv4GXa6Vbsc00)

---

## Contact

Creat de **Cosmin Trica**
- [LinkedIn](https://www.linkedin.com/in/cosmintrica/)
- [GitHub](https://github.com/cosmintrica)

---

*Licenta: MIT*
