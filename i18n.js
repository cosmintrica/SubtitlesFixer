window.SubtitlesFixerI18n = {
  storageKey: "sf-lang",

  ro: {
    meta: {
      title: "Subtitles Fixer - Repară și organizează subtitrările tale",
      description:
        "Subtitles Fixer repară și organizează rapid subtitrările .srt pentru seriale și filme. Backup automat, corecție encoding (diacritice) și restore instant. Gratis pe Windows.",
      keywords:
        "subtitrări, srt, fix, repair, diacritice, România, seriale, filme, rename, windows, open source",
      ogTitle: "Subtitles Fixer - Repară și Organizează Subtitrări",
      ogDescription:
        "Utilitar Windows gratuit pentru repararea encoding-ului și organizarea automată a subtitrărilor .ro.srt."
    },
    nav: {
      algorithm: "Algoritm",
      features: "Features",
      how: "Cum funcționează",
      download: "Download",
      langLabel: "Limba site-ului"
    },
    hero: {
      badge: "v{version} · Windows 10/11 · Gratis",
      titleHtml: 'Subtitrări impecabile,<br><span class="accent">foldere curate.</span>',
      sub: "Fără diacritice lipsă sau fișiere dezorganizate. Selectează folderul, previzualizează modificările și repară totul dintr-un singur click.",
      download: "Download",
      support: "Susține proiectul",
      revolutDesc: "Susținere rapidă cu Revolut Pay",
      stripeDesc: "Card bancar sau Google/Apple Pay",
      noteHtml:
        "<strong>Noutăți în v{version}:</strong> publicare Velopack fără coliziune cu release-ul anterior, dropdown de donații refăcut pentru mobil și fundal full-width mai fluid pentru pagină."
    },
    mockup: {
      chipChanging: "Se schimbă",
      chipOk: "OK",
      chipChoose: "Alege",
      unchanged: "Nu se schimbă",
      fixing: "Se reface",
      progress: "Procesez - {current} din {total}",
      float1: "?tiai c&#x0103; &#x2192; reparat",
      float2: "&#x0219;tiai c&#x0103; &#x2713;"
    },
    algorithm: {
      label: "Sub capotă",
      titleHtml: "Cel mai avansat algoritm de reparare<br>pentru subtitrări românești.",
      sub: "Nu e un simplu find & replace. Este un pipeline de 5 pași care înțelege limba română.",
      dictTitle: "Dicționar complet",
      dictBody:
        "Generat din Hunspell <code>ro_RO</code> cu toate formele flexionare. Fiecare cuvânt reparat este verificat în dicționar - nu se ghicește.",
      encTitle: "Detecție encoding",
      encBody:
        "Win-1250, ISO-8859-2, CP852, UTF-8 BOM - scorul de încredere alege automat encoding-ul corect.",
      combTitle: "Reparare combinatorică",
      combBody:
        "Markerii <code>?</code> <code>\uFFFD</code> <code>`</code> sunt înlocuiți cu toate combinațiile posibile de ș, ț, ă, â, î și validați în dicționar.",
      scoreTitle: "Scoring bigram + trigram",
      scoreBody:
        "Când dicționarul nu decide singur, scorul statistic pe bigramele limbii române departajează candidații.",
      diacTitle: "Corectare diacritice greșite",
      diacBody:
        "Pas 5 detectează și repară ș↔ț, ă↔â↔î greșit plasate, verificând fiecare variantă în dicționar.",
      cedillaTitle: "Cedillă → virgulă",
      cedillaBody:
        "Convertește automat ş/ţ (cedillă) în ș/ț (virgulă) - forma corectă conform standardului Unicode.",
      backupTitle: "Zero pierderi de date",
      backupBody:
        "Backup automat înainte de orice modificare. Restore cu un click. Nimic nu dispare vreodată."
    },
    features: {
      label: "Ce face",
      title: "Tot ce ai nevoie, nimic în plus.",
      encTitle: "Repară encoding",
      encBody: "Win-1250, ISO-8859-2, UTF-8 BOM - toate devin UTF-8 curat, cu diacritice corecte.",
      renameTitle: "Redenumește .ro.srt",
      renameBody: "Fiecare episod primește subtitrarea cu numele corect, direct lângă video.",
      dndTitle: "Drag & drop",
      dndBody: "Trage orice folder sau fișier direct în fereastră. Fără dialog, fără click-uri inutile.",
      backupTitle: "Backup automat",
      backupBody: "Sursa și varianta veche merg în backup. Nimic nu dispare fără urmă.",
      previewTitle: "Preview înainte de fix",
      previewBody: "Analizezi tot, vezi exact ce se schimbă, alegi manual unde trebuie, apoi rulezi.",
      restoreTitle: "Restore instant",
      restoreBody: "Un click readuce totul cum era. Selectezi ce vrei, apeși restore, gata.",
      localTitle: "100% local",
      localBody: "Totul rulează pe PC-ul tău. Fără cloud, fără cont, fără trackere.",
      updateTitle: "Update automat",
      updateBody: "Noi versiuni sunt instalate automat din GitHub Releases. Fără download manual."
    },
    how: {
      label: "Cum funcționează",
      title: "Trei pași. Zero complicații.",
      step1Title: "Alegi folderul",
      step1Body: "Trage folderul în fereastră sau selectează-l din dialog.",
      step2Title: "Verifici analiza",
      step2Body: "Vezi clar ce se repară, ce rămâne și unde alegi manual.",
      step3Title: "Rulezi fix",
      step3Body: "Un buton. Gata. Backup e făcut, .ro.srt e scris."
    },
    cta: {
      title: "Gata de download?",
      sub: "Gratis. Fără cont. Fără reclame."
    },
    footer: {
      privacy: "Confidențialitate",
      terms: "Termeni"
    },
    downloads: {
      count: "{count} descărcări pe GitHub"
    },
    legal: {
      back: "Înapoi la site",
      updated: "Ultima actualizare: Aprilie 2026",
      privacy: {
        metaTitle: "Confidențialitate - Subtitles Fixer",
        title: "Politica de confidențialitate",
        s1Title: "1. Introducere",
        s1Body:
          "Subtitles Fixer este o aplicație Windows care funcționează exclusiv local. Nu colectăm, nu transmitem și nu stocăm date personale pe servere externe.",
        s2Title: "2. Date procesate",
        s2Body:
          "Aplicația procesează exclusiv fișierele de pe calculatorul tău (fișiere video și subtitrări). Toate operațiunile se desfășoară pe dispozitivul tău, fără conexiune la internet.",
        s3Title: "3. Backup și fișiere",
        s3Body:
          "Aplicația creează copii de backup ale fișierelor originale într-un subfolder local dedicat. Aceste fișiere nu sunt transmise nicăieri.",
        s4Title: "4. Contact",
        s4Body:
          'Pentru orice întrebare, poți lua legătura pe <a href="https://www.linkedin.com/in/cosmintrica/">LinkedIn</a> sau pe <a href="https://github.com/cosmintrica/SubtitlesFixer">GitHub</a>.'
      },
      terms: {
        metaTitle: "Termeni și condiții - Subtitles Fixer",
        title: "Termeni și condiții",
        s1Title: "1. Acceptarea termenilor",
        s1Body:
          "Prin descărcarea și utilizarea aplicației Subtitles Fixer, accepți termenii și condițiile prezentate în acest document.",
        s2Title: "2. Utilizare",
        s2Body:
          'Aplicația este furnizată gratuit, în regim "as-is". Este destinată exclusiv uzului personal pentru organizarea fișierelor de subtitrare aflate în proprietatea ta.',
        s3Title: "3. Limitarea răspunderii",
        s3Body:
          "Autorul nu este răspunzător pentru pierderi de date rezultate în urma utilizării aplicației. Îți recomandăm să verifici backup-urile create automat de aplicație înainte de orice operațiune critică.",
        s4Title: "4. Licență",
        s4Body:
          'Aplicația este distribuită sub licența MIT. Codul sursă este disponibil pe <a href="https://github.com/cosmintrica/SubtitlesFixer">GitHub</a>.',
        s5Title: "5. Contact",
        s5Body:
          'Pentru întrebări, contactează autorul pe <a href="https://www.linkedin.com/in/cosmintrica/">LinkedIn</a>.'
      }
    }
  },

  en: {
    meta: {
      title: "Subtitles Fixer - Repair and organize your subtitles",
      description:
        "Subtitles Fixer quickly repairs and organizes .srt subtitles for TV shows and movies. Automatic backup, encoding fixes (Romanian diacritics), and instant restore. Free on Windows.",
      keywords:
        "subtitles, srt, fix, repair, diacritics, Romanian, TV series, movies, rename, windows, open source",
      ogTitle: "Subtitles Fixer - Repair and Organize Subtitles",
      ogDescription:
        "Free Windows utility for fixing subtitle encoding and automatically organizing .ro.srt files."
    },
    nav: {
      algorithm: "Algorithm",
      features: "Features",
      how: "How it works",
      download: "Download",
      langLabel: "Site language"
    },
    hero: {
      badge: "v{version} · Windows 10/11 · Free",
      titleHtml: 'Flawless subtitles,<br><span class="accent">clean folders.</span>',
      sub: "No missing diacritics or messy files. Pick a folder, preview changes, and fix everything in one click.",
      download: "Download",
      support: "Support the project",
      revolutDesc: "Quick support via Revolut Pay",
      stripeDesc: "Bank card or Google/Apple Pay",
      noteHtml:
        "<strong>What's new in v{version}:</strong> Velopack publish without colliding with the previous release, redesigned donate dropdown for mobile, and smoother full-width page background."
    },
    mockup: {
      chipChanging: "Changing",
      chipOk: "OK",
      chipChoose: "Choose",
      unchanged: "No change",
      fixing: "Repairing",
      progress: "Processing - {current} of {total}",
      float1: "?tiai c&#x0103; &#x2192; fixed",
      float2: "&#x0219;tiai c&#x0103; &#x2713;"
    },
    algorithm: {
      label: "Under the hood",
      titleHtml: "The most advanced repair algorithm<br>for Romanian subtitles.",
      sub: "Not a simple find & replace. A 5-step pipeline built for the Romanian language.",
      dictTitle: "Full dictionary",
      dictBody:
        "Generated from Hunspell <code>ro_RO</code> with all inflected forms. Every repaired word is dictionary-checked - never guessed.",
      encTitle: "Encoding detection",
      encBody:
        "Win-1250, ISO-8859-2, CP852, UTF-8 BOM - a confidence score picks the correct encoding automatically.",
      combTitle: "Combinatorial repair",
      combBody:
        "Markers <code>?</code> <code>\uFFFD</code> <code>`</code> are replaced with all valid combinations of ș, ț, ă, â, î and validated in the dictionary.",
      scoreTitle: "Bigram + trigram scoring",
      scoreBody:
        "When the dictionary alone cannot decide, statistical scoring on Romanian bigrams breaks the tie.",
      diacTitle: "Wrong diacritic fixes",
      diacBody:
        "Step 5 detects and fixes misplaced ș↔ț, ă↔â↔î, checking every variant in the dictionary.",
      cedillaTitle: "Cedilla → comma below",
      cedillaBody:
        "Automatically converts ş/ţ (cedilla) to ș/ț (comma below) - the correct Unicode form.",
      backupTitle: "Zero data loss",
      backupBody:
        "Automatic backup before any change. One-click restore. Nothing is ever lost."
    },
    features: {
      label: "What it does",
      title: "Everything you need, nothing extra.",
      encTitle: "Fix encoding",
      encBody: "Win-1250, ISO-8859-2, UTF-8 BOM - all become clean UTF-8 with correct diacritics.",
      renameTitle: "Rename to .ro.srt",
      renameBody: "Each episode gets the correctly named subtitle file right next to the video.",
      dndTitle: "Drag & drop",
      dndBody: "Drop any folder or file into the window. No dialogs, no extra clicks.",
      backupTitle: "Automatic backup",
      backupBody: "Source and previous versions go to backup. Nothing disappears without a trace.",
      previewTitle: "Preview before fix",
      previewBody: "Analyze everything, see exactly what changes, choose manually where needed, then run.",
      restoreTitle: "Instant restore",
      restoreBody: "One click brings everything back. Select what you want, hit restore, done.",
      localTitle: "100% local",
      localBody: "Everything runs on your PC. No cloud, no account, no trackers.",
      updateTitle: "Automatic updates",
      updateBody: "New versions install automatically from GitHub Releases. No manual downloads."
    },
    how: {
      label: "How it works",
      title: "Three steps. Zero hassle.",
      step1Title: "Pick a folder",
      step1Body: "Drag the folder into the window or select it from the dialog.",
      step2Title: "Review the analysis",
      step2Body: "See clearly what gets repaired, what stays, and where you choose manually.",
      step3Title: "Run fix",
      step3Body: "One button. Done. Backup is created, .ro.srt is written."
    },
    cta: {
      title: "Ready to download?",
      sub: "Free. No account. No ads."
    },
    footer: {
      privacy: "Privacy",
      terms: "Terms"
    },
    downloads: {
      count: "{count} downloads on GitHub"
    },
    legal: {
      back: "Back to site",
      updated: "Last updated: April 2026",
      privacy: {
        metaTitle: "Privacy - Subtitles Fixer",
        title: "Privacy Policy",
        s1Title: "1. Introduction",
        s1Body:
          "Subtitles Fixer is a Windows application that runs entirely locally. We do not collect, transmit, or store personal data on external servers.",
        s2Title: "2. Data processed",
        s2Body:
          "The app only processes files on your computer (video files and subtitles). All operations happen on your device, without an internet connection.",
        s3Title: "3. Backup and files",
        s3Body:
          "The app creates backup copies of original files in a dedicated local subfolder. These files are never transmitted anywhere.",
        s4Title: "4. Contact",
        s4Body:
          'For any questions, reach out on <a href="https://www.linkedin.com/in/cosmintrica/">LinkedIn</a> or <a href="https://github.com/cosmintrica/SubtitlesFixer">GitHub</a>.'
      },
      terms: {
        metaTitle: "Terms and Conditions - Subtitles Fixer",
        title: "Terms and Conditions",
        s1Title: "1. Acceptance of terms",
        s1Body:
          "By downloading and using Subtitles Fixer, you agree to the terms and conditions in this document.",
        s2Title: "2. Use",
        s2Body:
          'The app is provided free of charge, as-is. It is intended for personal use only, to organize subtitle files you own.',
        s3Title: "3. Limitation of liability",
        s3Body:
          "The author is not liable for data loss resulting from use of the app. We recommend checking the automatic backups before any critical operation.",
        s4Title: "4. License",
        s4Body:
          'The app is distributed under the MIT license. Source code is available on <a href="https://github.com/cosmintrica/SubtitlesFixer">GitHub</a>.',
        s5Title: "5. Contact",
        s5Body:
          'For questions, contact the author on <a href="https://www.linkedin.com/in/cosmintrica/">LinkedIn</a>.'
      }
    }
  },

  detectDefaultLang() {
    const saved = localStorage.getItem(this.storageKey);
    if (saved === "ro" || saved === "en") return saved;

    const langs = navigator.languages?.length ? navigator.languages : [navigator.language || ""];
    if (langs.some(l => /^ro(-|$)/i.test(String(l)))) return "ro";

    try {
      if (Intl.DateTimeFormat().resolvedOptions().timeZone === "Europe/Bucharest") return "ro";
    } catch {
      // ignore
    }

    return "en";
  },

  getNested(obj, path) {
    return path.split(".").reduce((acc, key) => (acc && acc[key] !== undefined ? acc[key] : null), obj);
  },

  format(str, vars) {
    if (!str) return "";
    return str.replace(/\{(\w+)\}/g, (_, key) => (vars[key] !== undefined ? vars[key] : `{${key}}`));
  },

  apply(lang, version) {
    const dict = this[lang];
    if (!dict) return;

    const vars = { version: version || "" };

    document.documentElement.lang = lang;
    document.documentElement.dataset.lang = lang;

    const legalPage = document.body.dataset.legalPage;
    if (legalPage) {
      const legalMeta = this.getNested(dict, `legal.${legalPage}.metaTitle`);
      if (legalMeta) document.title = legalMeta;
    } else if (dict.meta?.title) {
      document.title = dict.meta.title;
      const desc = document.querySelector('meta[name="description"]');
      if (desc && dict.meta.description) desc.setAttribute("content", dict.meta.description);
      const keywords = document.querySelector('meta[name="keywords"]');
      if (keywords && dict.meta.keywords) keywords.setAttribute("content", dict.meta.keywords);
      const ogTitle = document.querySelector('meta[property="og:title"]');
      if (ogTitle && dict.meta.ogTitle) ogTitle.setAttribute("content", dict.meta.ogTitle);
      const ogDesc = document.querySelector('meta[property="og:description"]');
      if (ogDesc && dict.meta.ogDescription) ogDesc.setAttribute("content", dict.meta.ogDescription);
    }

    document.querySelectorAll("[data-i18n]").forEach(el => {
      if (el.classList.contains("download-count")) return;
      const value = this.getNested(dict, el.dataset.i18n);
      if (value == null) return;
      let localVars = vars;
      if (el.dataset.i18nVars) {
        try {
          localVars = { ...vars, ...JSON.parse(el.dataset.i18nVars) };
        } catch {
          // ignore invalid JSON
        }
      }
      el.textContent = this.format(value, localVars);
    });

    document.querySelectorAll("[data-i18n-html]").forEach(el => {
      const value = this.getNested(dict, el.dataset.i18nHtml);
      if (value == null) return;
      let localVars = vars;
      if (el.dataset.i18nVars) {
        try {
          localVars = { ...vars, ...JSON.parse(el.dataset.i18nVars) };
        } catch {
          // ignore invalid JSON
        }
      }
      el.innerHTML = this.format(value, localVars);
    });

    document.querySelectorAll(".lang-btn").forEach(btn => {
      const active = btn.dataset.lang === lang;
      btn.setAttribute("aria-pressed", active ? "true" : "false");
    });

    const langSwitch = document.querySelector(".lang-switch");
    if (langSwitch) {
      const label = this.getNested(dict, "nav.langLabel");
      if (label) langSwitch.setAttribute("aria-label", label);
    }

    window.dispatchEvent(new CustomEvent("sf-lang-change", { detail: { lang, dict, vars } }));
  },

  setLang(lang, version) {
    if (lang !== "ro" && lang !== "en") return;
    localStorage.setItem(this.storageKey, lang);
    this.apply(lang, version);
  },

  init(version) {
    const lang = this.detectDefaultLang();
    this.apply(lang, version);

    document.querySelectorAll(".lang-btn").forEach(btn => {
      btn.addEventListener("click", () => {
        this.setLang(btn.dataset.lang, version);
      });
    });
  }
};
