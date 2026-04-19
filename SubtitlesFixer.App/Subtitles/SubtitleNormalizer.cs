using System.Text;
using System.Text.RegularExpressions;

namespace SubtitlesFixer.App.Subtitles;

/// <summary>
/// Port C# al functiilor Decode-Best si Normalize-RO din fixsubs.ps1.
/// Repara encoding + mojibake + caractere stricate (?, \uFFFD, `) folosind
/// context local (bigrame romanesti), NU un dictionar de cuvinte.
/// </summary>
internal static partial class SubtitleNormalizer
{
    private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf8NoBom  = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // ── Candidatii de inlocuire ──────────────────────────────────────────
    private static readonly char[] LowerCandidates = ['ș', 'ț', 'ă', 'â', 'î'];
    private static readonly char[] UpperCandidates = ['Ș', 'Ț', 'Ă', 'Â', 'Î'];

    // ── Mojibake (dublu-encoding UTF-8 citit ca Windows-1252 etc.) ───────
    private static readonly (string Broken, string Fixed)[] MojibakePairs =
    [
        ("È™", "ș"), ("È˜", "Ș"),
        ("È›", "ț"), ("Èš", "Ț"),
        ("ÅŸ", "ș"), ("Åž", "Ș"),
        ("Å£", "ț"), ("Å¢", "Ț"),
        ("Äƒ", "ă"), ("Ä‚", "Ă"),
        ("Ã¢", "â"), ("Ã‚", "Â"),
        ("Ã®", "î"), ("ÃŽ", "Î"),
    ];

    // ── Bigrame romanesti: scorul contextual ─────────────────────────────
    // Cheia = secventa de 2 caractere. Valoarea = cat de comuna e in romana.
    // Cu cat valoarea e mai mare, cu atat combinatia e mai naturala.
    private static readonly Dictionary<string, int> BigramScores = BuildBigramScores();

    private static Dictionary<string, int> BuildBigramScores()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // ── ș + urmatorul caracter ──
        d["șt"] = 100;  // ești, cunoaște, povestește, aștept, deștept
        d["și"] = 95;   // și (conjuncție), totuși
        d["șe"] = 55;   // greșeală, reușește
        d["șa"] = 50;   // așa, mașină
        d["șu"] = 35;   // roșu, ușurat
        d["șo"] = 30;   // ușor
        d["șc"] = 25;   // școală
        d["șn"] = 10;   // nișnai (rar)

        // ── caracterul precedent + ș ──
        d["eș"] = 70;   // greșit, ieșit, leșinat, deschișe
        d["aș"] = 65;   // așa, aștept, lași
        d["uș"] = 60;   // ușor, ușă, reușit, rușine
        d["oș"] = 35;   // roșu
        d["iș"] = 25;   // niște, înșine

        // ── ț + urmatorul caracter ──
        d["ți"] = 100;  // puteți, atenție, poliție, simți, îți
        d["ță"] = 85;   // față, viață, dimineață, siguranță
        d["ța"] = 80;   // dimineața, poliția, forța
        d["țe"] = 55;   // brațe, forțe, prețuri
        d["țu"] = 65;   // mulțumesc, puțin, cuțit
        d["țo"] = 10;   // funcționa (rar direct ț+o)
        d["țî"] = 5;    // rar

        // ── caracterul precedent + ț ──
        d["nț"] = 70;   // atenție, siguranță, violență, funcționa
        d["lț"] = 45;   // mulțumesc
        d["aț"] = 55;   // brațe, spațiu, ață
        d["eț"] = 45;   // prețuri, rețea
        d["uț"] = 35;   // cuțit, puțin
        d["iț"] = 30;   // situație, condiții
        d["oț"] = 20;   // hoțul
        d["rț"] = 15;   // (rar: cârți)

        // ── ă contexte ──
        d["ăr"] = 40; d["ră"] = 40; d["ăt"] = 30; d["tă"] = 35;
        d["ăl"] = 30; d["lă"] = 30; d["ăm"] = 25; d["mă"] = 40;
        d["ăi"] = 35; d["că"] = 45; d["să"] = 45; d["gă"] = 35;
        d["fă"] = 35; d["dă"] = 25; d["pă"] = 30; d["bă"] = 25;
        d["ză"] = 35; d["jă"] = 25;

        // ── â contexte ──
        d["ân"] = 50; d["nâ"] = 15; d["âm"] = 30; d["ât"] = 70;
        d["câ"] = 40; d["tâ"] = 15; d["lâ"] = 10; d["râ"] = 25;

        // ── î contexte ──
        d["în"] = 60; d["îm"] = 30; d["îl"] = 25; d["îi"] = 30;
        d["îș"] = 15; d["ît"] = 20; d["nî"] = 5;
        // extra: ă+ț, ț+â
        d["ăț"] = 40;  // bățos, cățel, încăpățânat
        d["țâ"] = 25;  // încăpățână

        // bigrame lipsă pentru ă
        d["ău"] = 35;  // rău, tău, său (nu prea mare, altfel divorțul → divorăul)
        d["vă"] = 40;  // vă rog, vă mulțumesc
        d["nă"] = 30;  // mănânc, mănăstire

        return d;
    }

    // ── Cuvinte scurte comune (bonus mare cand potriveste exact) ─────────
    private static readonly HashSet<string> CommonShortWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "și", "să", "vă", "mă", "în", "că", "îți", "ți", "ăla", "ăia", "ăsta", "ăstea",
        "niște", "încă", "până", "câți", "câte", "câtă", "câțiva",
        // ți-words: ați, toți sunt ambigue cu ași, toși (ambele in dictionar!)
        "ați", "toți",
        // ș-words (bigram scorer greșește spre ț)
        "totuși", "reușit", "reușită", "reușesc", "leșin", "leșinat", "leșinată",
        "ieșit", "ieșire", "greșit", "greșeală", "greșesc", "cunoaște", "cunoaștem",
        "cunoștință", "meșter",
        // ț-words (fără trigram ar merge spre ș)
        "puteți", "faceți", "mergeți", "aveți", "luați", "dați", "vreți",
        "spuneți", "vedeți", "credeți", "băieți", "trăiți", "iubiți", "doriți",
        // â-words (bigram scorer greșește spre ș/ț)
        "atât", "când", "câteva", "mâine", "pâine", "pământ", "sfânt", "câmp",
        // ă/ș-words ambigue
        "așa", "rău", "tău", "său", "grău",
        // ș-words: bigram e?i/r?i favorizează ț greșit
        "ieși", "ieșind", "sfârșit", "sfârșitul", "sfârșesc",
        "uciși", "ucis", "detașament", "detașamentele",
        "înfricoșător", "înfricoșătoare", "înfricoșători",
        "aceeași", "aceleași", "depășit", "depășesc",
        "așadar", "făptași", "însăși", "însuși",
        "obișnuit", "obișnuiau", "obișnuiesc", "obișnuiți",
        "lași", "greșiți",
        // ș-words: multi-? (orașul are 2 marcaje)
        "orașul", "orașului", "orășenesc",
        "înșine", "înșiși",
        "păpuși", "păpușă", "păpușar",
        "hârțogăraie", "hârțogărie",
        // ț la inceput de cuvant (ambigue cu ș: țara/șara, ține/șine)
        "țara", "țară", "țării", "țări", "țărilor",
        "ține", "ținea", "ținut", "ținută",
    };

    // ── Sufixe si prefixe romanesti (bonus mic) ─────────────────────────
    // Formulate ca trigramuri care oferă bonus de context la scoring.
    private static readonly HashSet<string> RomanianTrigrams = new(StringComparer.OrdinalIgnoreCase)
    {
        "ște", "ști", "ați", "eți", "iți", "oți", "uți",
        "ție", "ții", "ția", "țiu", "țio", "țel", "țil",
        "ănț", "anț", "enț", "inț", "unț", "onț",
        "ață", "eță", "oță",
        "ăsc", "esc", "isc", "șco",
    };

    // ────────────────────────────────────────────────────────────────────────
    // Decode
    // ────────────────────────────────────────────────────────────────────────

    public static string DecodeBytes(byte[] bytes)
    {
        // BOM detection
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // Try strict UTF-8 first
        try
        {
            Utf8Strict.GetString(bytes);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { }

        // Score-based fallback
        var candidates = new[] { 1250, 28592, 852, 1252 };
        string? best  = null;
        double bestScore = double.NegativeInfinity;

        foreach (var cp in candidates)
        {
            try
            {
                var enc  = Encoding.GetEncoding(cp);
                var text = enc.GetString(bytes);
                var sc   = ScoreRO(text);
                if (sc > bestScore) { bestScore = sc; best = text; }
            }
            catch { }
        }

        return best ?? Encoding.UTF8.GetString(bytes);
    }

    private static double ScoreRO(string t)
    {
        var diac = DiaCriticsRx().Matches(t).Count;
        var rep  = ReplacementCharRx().Matches(t).Count;
        var bad  = BadCharRx().Matches(t).Count;
        return (3 * diac) - (5 * rep) - (3 * bad);
    }

    [GeneratedRegex(@"[\u0103\u0102\u00E2\u00C2\u00EE\u00CE\u0219\u0218\u021B\u021A\u015F\u015E\u0163\u0162]")]
    private static partial Regex DiaCriticsRx();

    [GeneratedRegex(@"\uFFFD")]
    private static partial Regex ReplacementCharRx();

    [GeneratedRegex(@"[^\u0009\u000A\u000D\u0020-\uFFFD]")]
    private static partial Regex BadCharRx();

    // ────────────────────────────────────────────────────────────────────────
    // Normalize  – 3 pași: mojibake → cedilla → context-based repair
    // ────────────────────────────────────────────────────────────────────────

    public static string Normalize(string s)
    {
        // Pas 0: CRLF
        s = CrlfRx().Replace(s, "\r\n");

        // Pas 1: Mojibake – secvente multi-byte stricate (UTF-8 citit ca 1252)
        foreach (var (broken, fixedText) in MojibakePairs)
            s = s.Replace(broken, fixedText, StringComparison.Ordinal);

        // Pas 2: Cedilla → comma diacritice
        s = s
            .Replace('\u015F', '\u0219')  // ş → ș
            .Replace('\u015E', '\u0218')  // Ş → Ș
            .Replace('\u0163', '\u021B')  // ţ → ț
            .Replace('\u0162', '\u021A'); // Ţ → Ț

        // Pas 2.5: Nota muzicala ♪ pierduta la conversia in Win-1250 devine '?' izolat.
        // Restauram ♪ inainte de repararea markerilor ca sa nu fie tratat ca diacritica.
        s = FixMusicNotes(s);

        // Nu pornim pasii grei de dictionar pe subtitrari deja curate.
        // Daca dupa repararea notelor muzicale nu mai exista markeri stricati,
        // evitam incarcarea lazy a dictionarului de ~960K cuvinte.
        var hasBrokenMarkers = ContainsBrokenMarkers(s);

        // Pas 3: Repara fiecare ? / \uFFFD / ` ramas, folosind contextul local
        if (hasBrokenMarkers)
            s = RepairBrokenMarks(s);

        // Pas 4: â la inceput de cuvant → î (regula ortografica romana)
        s = FixWordStartCircumflex(s);

        // Pas 5: Corectare diacritice gresite cu dictionar.
        // Rulam doar daca au existat markeri stricati reali; altfel costul este mare
        // iar pe subtitrari deja bune aduce prea putin beneficiu.
        if (hasBrokenMarkers)
            s = FixWrongDiacriticsWithDictionary(s);

        // Pas 6: cosmetica de subtitle - fara trailing spaces si fara spatiu dupa
        // marcajul de dialog de la inceputul liniei ("-Salut", nu "- Salut").
        s = TrailingWhitespaceRx().Replace(s, string.Empty);
        s = LeadingDialogueDashRx().Replace(s, "-");

        return s;
    }

    [GeneratedRegex(@"\r?\n")]
    private static partial Regex CrlfRx();

    // Note muzicale pierdute: ? izolat la inceput/sfarsit de linie sau inconjurat de spatii.
    // Cazurile acoperite (m = multiline pentru ^/$):
    //   "? Text ..."        → "♪ Text ..."
    //   "... Text ?"        → "... Text ♪"
    //   "... ? ..."         doar daca pe aceeasi linie exista deja un ♪ (vezi FixMusicNotes).
    [GeneratedRegex(@"(?m)^(\s*)\?(?=\s)")]
    private static partial Regex MusicNoteLineStartRx();

    [GeneratedRegex(@"(?m)(?<=\s)\?(\s*)$")]
    private static partial Regex MusicNoteLineEndRx();

    [GeneratedRegex(@"(?<=\s)\?(?=\s)")]
    private static partial Regex MusicNoteMiddleRx();

    [GeneratedRegex(@"[ \t]+(?=\r?\n|\z)")]
    private static partial Regex TrailingWhitespaceRx();

    [GeneratedRegex(@"(?m)^-\s+")]
    private static partial Regex LeadingDialogueDashRx();

    private static string FixMusicNotes(string s)
    {
        // Heuristica prudenta: transformam '?' izolat doar la marginile de linie
        // (unde niciun cuvant romanesc nu are '?'). Apoi, daca linia contine deja
        // '♪', transformam si '?'-urile din mijloc inconjurate de spatii.
        s = MusicNoteLineStartRx().Replace(s, "$1♪");
        s = MusicNoteLineEndRx().Replace(s, "♪$1");

        if (!s.Contains('♪'))
            return s;

        // Pentru liniile care au deja ♪ (fie original, fie dupa pasul de mai sus),
        // inlocuim si '?' izolat inconjurat de spatii.
        var sb = new StringBuilder(s.Length);
        int i = 0;
        int len = s.Length;
        while (i < len)
        {
            int lineEnd = s.IndexOf('\n', i);
            if (lineEnd < 0) lineEnd = len;

            var line = s.Substring(i, lineEnd - i);
            if (line.Contains('♪') && line.Contains('?'))
                line = MusicNoteMiddleRx().Replace(line, "♪");

            sb.Append(line);
            if (lineEnd < len)
            {
                sb.Append('\n');
                i = lineEnd + 1;
            }
            else
            {
                break;
            }
        }
        return sb.ToString();
    }

    private static bool ContainsBrokenMarkers(string s) =>
        s.IndexOf('?') >= 0 || s.IndexOf('\uFFFD') >= 0 || s.IndexOf('`') >= 0;

    // ────────────────────────────────────────────────────────────────────────
    // Pas 4: â la inceput de cuvant → î (regula ortografica romana)
    // In romana, î se foloseste la inceput si sfarsit de cuvant, â doar la mijloc.
    // ────────────────────────────────────────────────────────────────────────

    private static string FixWordStartCircumflex(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is not ('â' or 'Â'))
                continue;

            // Verifica daca e la inceput de cuvant (fara litera inainte)
            if (i > 0 && char.IsLetter(chars[i - 1]))
                continue;

            chars[i] = chars[i] == 'â' ? 'î' : 'Î';
        }
        return new string(chars);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Pas 5: Corectare diacritice gresite cu dictionar
    //
    // Unele fisiere au diacritice valide dar incorecte (ex: ț in loc de ă).
    // Pentru fiecare cuvant care contine diacritice si NU exista in dictionar,
    // incearca sa inlocuiasca diacriticele cu alternative si verifica dictionarul.
    // Prioritate: restaurare diacritice, nu adaugare litere lipsa.
    // ────────────────────────────────────────────────────────────────────────

    private static readonly char[] AllDiacritics = ['ș', 'ț', 'ă', 'â', 'î', 'Ș', 'Ț', 'Ă', 'Â', 'Î'];

    private static bool IsKnownRomanianWord(string word) =>
        CommonShortWords.Contains(word) || RomanianDictionary.Contains(word);

    private static string FixWrongDiacriticsWithDictionary(string s)
    {
        var chars = s.ToCharArray();
        int len = chars.Length;
        int i = 0;

        while (i < len)
        {
            if (!char.IsLetter(chars[i]))
            {
                i++;
                continue;
            }

            // Gaseste limitele cuvantului
            int wordStart = i;
            while (i < len && char.IsLetter(chars[i]))
                i++;
            int wordEnd = i;

            // Verifica daca cuvantul contine diacritice
            var diacPositions = new List<int>();
            for (int j = wordStart; j < wordEnd; j++)
            {
                char lower = char.ToLowerInvariant(chars[j]);
                if (lower is 'ș' or 'ț' or 'ă' or 'â' or 'î')
                    diacPositions.Add(j);
            }

            if (diacPositions.Count == 0)
                continue;

            // Daca cuvantul e format DOAR din diacritice (ex: ăă, ă),
            // e probabil o interjectie/filler — nu incerca sa o repari
            if (diacPositions.Count == wordEnd - wordStart)
                continue;

            // Cuvantul e deja corect?
            var word = new string(chars, wordStart, wordEnd - wordStart);
            if (IsKnownRomanianWord(word))
                continue;

            // Prea multe diacritice → skip (combinatorial explosion)
            if (diacPositions.Count > 5)
                continue;

            // Incearca inlocuirea diacriticelor gresite
            TryFixDiacritics(chars, wordStart, wordEnd, diacPositions);
        }

        return new string(chars);
    }

    /// <summary>
    /// Incearca toate combinatiile de diacritice alternative pentru un cuvant
    /// care nu exista in dictionar. Aplica doar daca gaseste exact un match
    /// sau un match clar mai bun (scor bigram).
    /// </summary>
    private static void TryFixDiacritics(char[] chars, int wordStart, int wordEnd, List<int> diacPositions)
    {
        var saved = new char[diacPositions.Count];
        for (int k = 0; k < diacPositions.Count; k++)
            saved[k] = chars[diacPositions[k]];

        var matches = new List<(char[] values, int score)>();
        TryDiacCombinations(chars, wordStart, wordEnd, diacPositions, 0, matches);

        // Restaureaza originalele
        for (int k = 0; k < diacPositions.Count; k++)
            chars[diacPositions[k]] = saved[k];

        if (matches.Count == 0)
            return;

        // Daca avem un singur match sau un match clar mai bun
        var best = matches.OrderByDescending(m => m.score).First();

        // Verifica ca nu e identic cu originalul (ar insemna ca cuvantul e deja corect)
        bool different = false;
        for (int k = 0; k < diacPositions.Count; k++)
        {
            if (best.values[k] != saved[k])
            {
                different = true;
                break;
            }
        }

        if (!different)
            return;

        // Aplica corectia
        for (int k = 0; k < diacPositions.Count; k++)
            chars[diacPositions[k]] = best.values[k];
    }

    private static void TryDiacCombinations(
        char[] chars, int wordStart, int wordEnd,
        List<int> diacPositions, int idx,
        List<(char[] values, int score)> matches)
    {
        if (idx >= diacPositions.Count)
        {
            var word = new string(chars, wordStart, wordEnd - wordStart);
            if (!IsKnownRomanianWord(word))
                return;

            // Calculeaza scor bigram ca tiebreaker
            int score = 0;
            foreach (var pos in diacPositions)
            {
                char lower = char.ToLowerInvariant(chars[pos]);
                if (pos > wordStart)
                {
                    var bg = $"{char.ToLowerInvariant(chars[pos - 1])}{lower}";
                    if (BigramScores.TryGetValue(bg, out var ls)) score += ls;
                }
                if (pos < wordEnd - 1)
                {
                    var bg = $"{lower}{char.ToLowerInvariant(chars[pos + 1])}";
                    if (BigramScores.TryGetValue(bg, out var rs)) score += rs;
                }
            }

            matches.Add((diacPositions.Select(p => chars[p]).ToArray(), score));
            return;
        }

        int pos2 = diacPositions[idx];
        bool isUp = char.IsUpper(chars[pos2]);
        var candidates = isUp ? UpperCandidates : LowerCandidates;
        var original = chars[pos2];

        foreach (var c in candidates)
        {
            chars[pos2] = c;
            TryDiacCombinations(chars, wordStart, wordEnd, diacPositions, idx + 1, matches);
        }

        chars[pos2] = original;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Pas 3: Reparare bazata pe dictionar + fallback bigram
    //
    // Algoritm:
    // 1. Imparte textul in "cuvinte" (secvente de litere + markeri)
    // 2. Pentru fiecare cuvant cu markeri (? / \uFFFD / `):
    //    a. Daca ≤5 markeri: incearca toate combinatiile de candidati
    //       si verifica dictionarul (~960K cuvinte romanesti)
    //    b. Pentru ? si ` la sfarsit de cuvant: verifica si varianta
    //       in care markerul e pastrat ca punctuatie (? = intrebare, ` = apostrof)
    //    c. Daca dictionarul nu gaseste nimic: fallback la bigram scoring
    // ────────────────────────────────────────────────────────────────────────

    private static string RepairBrokenMarks(string s)
    {
        var chars = s.ToCharArray();
        var len = chars.Length;
        int i = 0;

        while (i < len)
        {
            // Cauta inceputul unui cuvant (litera sau marker)
            if (!char.IsLetter(chars[i]) && !IsBrokenMarker(chars[i]))
            {
                i++;
                continue;
            }

            // Gaseste limitele cuvantului
            int wordStart = i;
            while (i < len && (char.IsLetter(chars[i]) || IsBrokenMarker(chars[i])))
                i++;
            int wordEnd = i;

            // Colecteaza pozitiile markerilor
            var markers = new List<int>();
            for (int j = wordStart; j < wordEnd; j++)
                if (IsBrokenMarker(chars[j]))
                    markers.Add(j);

            if (markers.Count == 0)
                continue;

            // "asta???" si cazuri similare sunt punctuatie, nu litere lipsa.
            // Daca avem numai '?' consecutive la finalul token-ului, le lasam intacte.
            if (markers.Count >= 2 &&
                markers[0] == wordEnd - markers.Count &&
                markers.All(pos => chars[pos] == '?') &&
                markers.Zip(markers.Skip(1), (a, b) => b == a + 1).All(x => x))
            {
                continue;
            }

            // Regula context: ?i dupa cratima (-?i) → ți (pronume reflexiv/dativ)
            // Exemple: Pune-ți, să-ți, fă-ți, Lasă-ți, curăță-ți
            if (markers.Count == 1 && wordEnd - wordStart == 2
                && IsBrokenMarker(chars[wordStart]) && chars[wordStart + 1] == 'i'
                && wordStart > 0 && chars[wordStart - 1] == '-')
            {
                chars[wordStart] = ShouldBeUpperCase(chars, wordStart) ? 'Ț' : 'ț';
                continue;
            }

            // Regula context: ?i- + auxiliar scurt → ți- (Nu ți-a / Cum ți-am / De ce ți-au)
            if (markers.Count == 1 && wordEnd - wordStart == 2
                && IsBrokenMarker(chars[wordStart]) && chars[wordStart + 1] == 'i'
                && wordEnd < len && chars[wordEnd] == '-')
            {
                int suffixStart = wordEnd + 1;
                int suffixEnd = suffixStart;
                while (suffixEnd < len && char.IsLetter(chars[suffixEnd]))
                    suffixEnd++;

                var suffix = suffixEnd > suffixStart
                    ? new string(chars, suffixStart, suffixEnd - suffixStart).ToLowerInvariant()
                    : string.Empty;

                if (suffix is "a" or "ai" or "am" or "ar" or "au")
                {
                    chars[wordStart] = ShouldBeUpperCase(chars, wordStart) ? 'Ț' : 'ț';
                    continue;
                }
            }

            // Incearca reparare cu dictionar (≤5 markeri)
            if (markers.Count <= 5 && TryDictionaryRepair(chars, wordStart, wordEnd, markers))
                continue;

            // Fallback: reparare per-caracter cu bigram scoring
            BigramRepairFallback(chars, wordStart, wordEnd, markers);
        }

        return new string(chars);
    }

    /// <summary>
    /// Incearca sa repare toti markerii dintr-un cuvant folosind dictionarul.
    /// Genereaza toate combinatiile de candidati si verifica daca rezultatul
    /// e un cuvant valid in romana.
    /// </summary>
    private static bool TryDictionaryRepair(char[] chars, int wordStart, int wordEnd, List<int> markers)
    {
        // Salveaza caracterele originale ale markerilor
        var saved = new char[markers.Count];
        for (int k = 0; k < markers.Count; k++)
            saved[k] = chars[markers[k]];

        // Determina majuscula/minuscula pentru fiecare pozitie
        var isUpper = new bool[markers.Count];
        for (int k = 0; k < markers.Count; k++)
            isUpper[k] = ShouldBeUpperCase(chars, markers[k]);

        // ── Branch B: inlocuieste TOTI markerii ──
        char[]? bestValues = null;
        int bestScore = int.MinValue;

        TryAllCandidateCombinations(chars, wordStart, wordEnd, markers, isUpper, 0,
            ref bestValues, ref bestScore);

        // Restaureaza originalele
        for (int k = 0; k < markers.Count; k++)
            chars[markers[k]] = saved[k];

        // ── Branch A: trailing ? sau ` → pastreaza ca punctuatie ──
        char[]? trailingBestValues = null;
        int trailingBestScore = int.MinValue;
        int lastPos = markers[^1];
        bool lastIsEnd = lastPos == wordEnd - 1;
        char lastMarkerChar = saved[^1];

        if (lastIsEnd && lastMarkerChar is '?' or '`' or '\uFFFD')
        {
            if (markers.Count == 1)
            {
                // La un singur marker final vrem in continuare ca "pre?" / "ora?"
                // sa poata deveni "preț" / "oraș", deci bonusul ramane sub scorul
                // minim al variantei complete din dictionar.
                const int singleMarkerTrailingPunctuationBonus = 450;

                // Singurul marker e la sfarsit → verifica cuvantul fara el
                var wordWithout = new string(chars, wordStart, wordEnd - wordStart - 1);
                if (IsKnownRomanianWord(wordWithout))
                    trailingBestScore = singleMarkerTrailingPunctuationBonus;
            }
            else
            {
                // Pentru cuvinte cu 2+ markeri, daca tulpina reparata fara ultimul '?'
                // exista in dictionar, preferam mai ferm semnul de intrebare real.
                // Asta evita cazuri gen "le?inat?" -> "leșinată" in loc de "leșinat?".
                const int multiMarkerTrailingPunctuationBonus = 750;

                // Mai multi markeri → repara pe toti in afara de ultimul
                var subMarkers = markers.GetRange(0, markers.Count - 1);
                var subIsUpper = isUpper[..^1];

                TryAllCandidateCombinations(chars, wordStart, wordEnd - 1, subMarkers, subIsUpper, 0,
                    ref trailingBestValues, ref trailingBestScore);

                // Restaureaza originalele
                for (int k = 0; k < subMarkers.Count; k++)
                    chars[subMarkers[k]] = saved[k];

                // Ajusteaza fata de baseline Branch B (500 din dict).
                if (trailingBestScore > int.MinValue)
                    trailingBestScore += multiMarkerTrailingPunctuationBonus - 500;
            }
        }

        // Alege cel mai bun rezultat
        if (bestScore >= trailingBestScore && bestValues != null)
        {
            // Branch B: inlocuieste toti markerii
            for (int k = 0; k < markers.Count; k++)
                chars[markers[k]] = bestValues[k];
            return true;
        }

        if (trailingBestScore > int.MinValue)
        {
            // Branch A: pastreaza trailing ca punctuatie
            if (trailingBestValues != null)
            {
                var subMarkers = markers.GetRange(0, markers.Count - 1);
                for (int k = 0; k < subMarkers.Count; k++)
                    chars[subMarkers[k]] = trailingBestValues[k];
            }
            chars[lastPos] = lastMarkerChar is '`' or '\uFFFD' ? '\'' : lastMarkerChar;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Exploreaza recursiv toate combinatiile de candidati pentru pozitiile marker.
    /// La fiecare frunza (toti markerii inlocuiti), verifica dictionarul.
    /// </summary>
    private static void TryAllCandidateCombinations(
        char[] chars, int wordStart, int wordEnd,
        List<int> markers, bool[] isUpper, int idx,
        ref char[]? bestValues, ref int bestScore)
    {
        if (idx >= markers.Count)
        {
            // Toti markerii au fost inlocuiti - verifica dictionarul
            var word = new string(chars, wordStart, wordEnd - wordStart);

            if (!IsKnownRomanianWord(word))
                return;

            // Calculeaza scor bigram ca tiebreaker
            int score = 500; // bonus dictionar
            foreach (var pos in markers)
            {
                if (pos > 0 && char.IsLetter(chars[pos - 1]))
                {
                    var bg = $"{char.ToLowerInvariant(chars[pos - 1])}{char.ToLowerInvariant(chars[pos])}";
                    if (BigramScores.TryGetValue(bg, out var ls)) score += ls;
                }
                if (pos < chars.Length - 1 && char.IsLetter(chars[pos + 1]))
                {
                    var bg = $"{char.ToLowerInvariant(chars[pos])}{char.ToLowerInvariant(chars[pos + 1])}";
                    if (BigramScores.TryGetValue(bg, out var rs)) score += rs;
                }

                // Trigram bonus (acelasi ca in BigramRepairFallback)
                if (pos > wordStart && pos < wordEnd - 1 &&
                    char.IsLetter(chars[pos - 1]) && char.IsLetter(chars[pos + 1]))
                {
                    var trigram = $"{char.ToLowerInvariant(chars[pos - 1])}{char.ToLowerInvariant(chars[pos])}{char.ToLowerInvariant(chars[pos + 1])}";
                    if (RomanianTrigrams.Contains(trigram))
                        score += 30;
                }

                // Penalizare combinatii imposibile (fonotactica)
                score += PenalizeImpossible(chars, pos, char.ToLowerInvariant(chars[pos]));
            }

            // Bonus pentru cuvinte scurte comune (și, să, că, așa, etc.)
            if (CommonShortWords.Contains(word))
                score += 200;

            if (score > bestScore)
            {
                bestScore = score;
                bestValues = markers.Select(m => chars[m]).ToArray();
            }
            return;
        }

        int pos2 = markers[idx];
        var candidates = isUpper[idx] ? UpperCandidates : LowerCandidates;

        foreach (var c in candidates)
        {
            chars[pos2] = c;
            TryAllCandidateCombinations(chars, wordStart, wordEnd, markers, isUpper, idx + 1,
                ref bestValues, ref bestScore);
        }
    }

    /// <summary>
    /// Determina daca caracterul la pozitia data ar trebui sa fie majuscula.
    /// </summary>
    private static bool ShouldBeUpperCase(char[] chars, int pos)
    {
        bool hasLetterLeft = pos > 0 && char.IsLetter(chars[pos - 1]);
        bool hasLetterRight = pos < chars.Length - 1 && char.IsLetter(chars[pos + 1]);

        if (!hasLetterLeft)
        {
            // Daca vecinul stang e un marker stricat, suntem in mijlocul cuvantului
            // (ex: ?\uFFFDrii → ? e marker, FFFD e marker → nu e inceput de cuvant)
            if (pos > 0 && IsBrokenMarker(chars[pos - 1]))
                return false;

            // Inceput de cuvant: majuscula doar la inceput de propozitie
            return hasLetterRight && IsStartOfSentence(chars, pos);
        }

        // Mijloc de cuvant: majuscula doar daca ambii vecini sunt majuscule (ALL-CAPS)
        return hasLetterLeft && hasLetterRight
            && char.IsUpper(chars[pos - 1]) && char.IsUpper(chars[pos + 1]);
    }

    /// <summary>
    /// Fallback pe bigram scoring cand dictionarul nu gaseste nimic.
    /// Gestioneaza ? la sfarsit de cuvant (pastrat ca ?) si ` (convertit in apostrof).
    /// </summary>
    private static void BigramRepairFallback(char[] chars, int wordStart, int wordEnd, List<int> markers)
    {
        int lastPos = markers[^1];
        bool lastIsEnd = lastPos == wordEnd - 1;
        char lastChar = chars[lastPos];

        // ? la sfarsit de cuvant → semn de intrebare real (dupa litera)
        if (lastIsEnd && lastChar == '?' && lastPos > wordStart && char.IsLetter(chars[lastPos - 1]))
        {
            markers = markers.GetRange(0, markers.Count - 1);
        }
        // ` sau FFFD la sfarsit de cuvant → apostrof
        else if (lastIsEnd && lastChar is '`' or '\uFFFD' && lastPos > wordStart && char.IsLetter(chars[lastPos - 1]))
        {
            chars[lastPos] = '\'';
            markers = markers.GetRange(0, markers.Count - 1);
        }

        // Multi-pass bigram repair pentru markerii ramasi
        for (int pass = 0; pass < 3; pass++)
        {
            bool changed = false;

            foreach (var pos in markers)
            {
                if (!IsBrokenMarker(chars[pos]))
                    continue;

                bool hasLeft = pos > wordStart && char.IsLetter(chars[pos - 1]);
                bool hasRight = pos < wordEnd - 1 && char.IsLetter(chars[pos + 1]);

                if (!hasLeft && !hasRight)
                    continue;

                bool isUp = ShouldBeUpperCase(chars, pos);
                var candidates = isUp ? UpperCandidates : LowerCandidates;

                char bestChar = '\0';
                int bestScore = int.MinValue;

                foreach (var c in candidates)
                {
                    int score = ScoreCandidate(chars, pos, c);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestChar = c;
                    }
                }

                if (bestScore > 0)
                {
                    chars[pos] = bestChar;
                    changed = true;
                }
            }

            if (!changed) break;
        }
    }

    /// <summary>
    /// Scor contextual pentru un candidat la pozitia <paramref name="pos"/>.
    /// Suma bigramelor (stanga+candidat) si (candidat+dreapta),
    /// plus bonus trigram si cuvânt scurt cunoscut.
    /// </summary>
    private static int ScoreCandidate(char[] text, int pos, char candidate)
    {
        var score = 0;
        var lower = char.ToLowerInvariant(candidate);

        // ── Bigram stanga: chars[pos-1] + candidat ──
        if (pos > 0 && char.IsLetter(text[pos - 1]))
        {
            var left = char.ToLowerInvariant(text[pos - 1]);
            var bigram = new string([left, lower]);
            if (BigramScores.TryGetValue(bigram, out var leftScore))
                score += leftScore;
        }
        else if (pos > 0 && IsBrokenMarker(text[pos - 1]))
        {
            // Vecinul stang e si el stricat; estimeaza cel mai bun bigram posibil
            var bestLeft = 0;
            foreach (var lc in LowerCandidates)
            {
                var bigram = new string([lc, lower]);
                if (BigramScores.TryGetValue(bigram, out var ls) && ls > bestLeft)
                    bestLeft = ls;
            }
            score += bestLeft;
        }

        // ── Bigram dreapta: candidat + chars[pos+1] ──
        if (pos < text.Length - 1 && char.IsLetter(text[pos + 1]))
        {
            var right = char.ToLowerInvariant(text[pos + 1]);
            var bigram = new string([lower, right]);
            if (BigramScores.TryGetValue(bigram, out var rightScore))
                score += rightScore;
        }
        else if (pos < text.Length - 1 && IsBrokenMarker(text[pos + 1]))
        {
            // Vecinul drept e si el stricat; estimeaza cel mai bun bigram posibil
            var bestRight = 0;
            foreach (var rc in LowerCandidates)
            {
                var bigram = new string([lower, rc]);
                if (BigramScores.TryGetValue(bigram, out var rs) && rs > bestRight)
                    bestRight = rs;
            }
            score += bestRight;
        }

        // ── Trigram bonus: 3 caractere centrate pe candidat ──
        if (pos > 0 && pos < text.Length - 1 &&
            char.IsLetter(text[pos - 1]) && char.IsLetter(text[pos + 1]))
        {
            var trigram = new string([
                char.ToLowerInvariant(text[pos - 1]),
                lower,
                char.ToLowerInvariant(text[pos + 1])
            ]);
            if (RomanianTrigrams.Contains(trigram))
                score += 30;
        }

        // ── Penalizare combinatii imposibile ──
        score += PenalizeImpossible(text, pos, lower);

        return score;
    }

    /// <summary>
    /// Penalizeaza combinatii care nu apar niciodata in romana (fonotactica).
    /// </summary>
    private static int PenalizeImpossible(char[] text, int pos, char lower)
    {
        var penalty = 0;
        bool hasLetterLeft = pos > 0 && char.IsLetter(text[pos - 1]);
        bool hasLetterRight = pos < text.Length - 1 && char.IsLetter(text[pos + 1]);

        // ț urmat de consoana este extrem de rar (doar ță, ți, țe, țo, ța, țu, țî)
        if (lower == 'ț' && hasLetterRight)
        {
            var right = char.ToLowerInvariant(text[pos + 1]);
            if (right is 't' or 'ț' or 'ș')
                penalty -= 200;
            else if (!"aăâeîiou".Contains(right))
                penalty -= 100; // ț + consoana e foarte rar
        }

        // ș urmat de ș sau ț e imposibil
        if (lower == 'ș' && hasLetterRight)
        {
            var right = char.ToLowerInvariant(text[pos + 1]);
            if (right is 'ș' or 'ț')
                penalty -= 200;
        }

        // â nu apare la sfarsit de cuvant
        if (lower == 'â' && !hasLetterRight)
            penalty -= 50;

        // â nu apare niciodata la inceput de cuvant in romana (acolo e î)
        if (lower == 'â' && !hasLetterLeft)
            penalty -= 100;

        // î apare in principal la inceput de cuvant (în, îți, etc.)
        if (lower == 'î' && hasLetterLeft)
            penalty -= 40;

        // La inceput de cuvant: restrictii
        if (!hasLetterLeft)
        {
            // ț la inceput de cuvant + vocala e OK (țara, ține, țări)
            // ț la inceput de cuvant + consoana e imposibil (țcoală)
            if (lower == 'ț')
            {
                if (hasLetterRight && !"aăâeîiou".Contains(char.ToLowerInvariant(text[pos + 1])))
                    penalty -= 200; // țcoală, țnui etc.
                // Nu penalizam ț + vocala la inceput (țara, ține sunt corecte)
            }
            if (lower == 'ă') penalty -= 100; // ăcoală nu exista
        }

        return penalty;
    }

    /// <summary>
    /// Determina daca pozitia e la inceputul unei propozitii (dupa newline, dupa punct, etc.)
    /// </summary>
    private static bool IsStartOfSentence(char[] text, int pos)
    {
        for (var i = pos - 1; i >= 0; i--)
        {
            var ch = text[i];
            if (ch is ' ' or '\t' or '\r')
                continue;
            if (ch == '\n')
            {
                // Dupa newline: verifica ce e inainte de newline
                for (var j = i - 1; j >= 0; j--)
                {
                    var prev = text[j];
                    if (prev is ' ' or '\t' or '\r')
                        continue;
                    return prev == '\n' || prev == '>' || char.IsDigit(prev) || prev is '.' or '!' or '?';
                }
                return true;
            }
            return ch is '.' or '!' or '?';
        }
        return true;
    }

    private static bool IsBrokenMarker(char ch) => ch is '?' or '\uFFFD' or '`';

    private static bool IsRomanianDiacritic(char ch) =>
        ch is 'ă' or 'â' or 'î' or 'ș' or 'ț' or 'Ă' or 'Â' or 'Î' or 'Ș' or 'Ț';
}
