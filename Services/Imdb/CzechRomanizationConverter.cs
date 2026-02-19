using System.Text;

namespace vkinegrab.Services.Imdb;

/// <summary>
/// Converts Czech romanization (Polivka transcription for Japanese, Czech phonetic for Korean)
/// to English romanization (modified Hepburn for Japanese, Revised Romanization for Korean).
/// Rules are applied longest-match-first to avoid partial substitution errors.
/// </summary>
internal static class CzechRomanizationConverter
{
    /// <summary>
    /// Czech Polivka transcription → modified Hepburn romanization.
    /// Ordered longest-match-first to prevent partial substitutions.
    /// </summary>
    private static readonly (string Czech, string English)[] JapaneseRules =
    [
        // Multi-char clusters (must come before single-char rules)
        ("šú", "shū"),
        ("šó", "shō"),
        ("čó", "chō"),
        ("čú", "chū"),
        ("džú", "jū"),
        ("džó", "jō"),
        ("cú", "tsū"),
        ("rjú", "ryū"),
        ("rjó", "ryō"),
        ("kjú", "kyū"),
        ("kjó", "kyō"),
        ("gjú", "gyū"),
        ("gjó", "gyō"),
        ("njú", "nyū"),
        ("njó", "nyō"),
        ("mjú", "myū"),
        ("mjó", "myō"),
        ("hjú", "hyū"),
        ("hjó", "hyō"),
        ("bjú", "byū"),
        ("bjó", "byō"),
        ("pjú", "pyū"),
        ("pjó", "pyō"),
        ("dži", "ji"),
        ("džu", "ju"),
        ("dže", "je"),
        ("džo", "jo"),
        ("dža", "ja"),
        ("ša", "sha"),
        ("ši", "shi"),
        ("šu", "shu"),
        ("še", "she"),
        ("šo", "sho"),
        ("ča", "cha"),
        ("či", "chi"),
        ("ču", "chu"),
        ("če", "che"),
        ("čo", "cho"),
        ("cu", "tsu"),
        ("ca", "tsa"),
        ("ce", "tse"),
        ("co", "tso"),
        ("ci", "tsi"),
        ("rja", "rya"),
        ("rji", "ryi"),
        ("rju", "ryu"),
        ("rje", "rye"),
        ("rjo", "ryo"),
        ("kja", "kya"),
        ("kji", "kyi"),
        ("kju", "kyu"),
        ("kje", "kye"),
        ("kjo", "kyo"),
        ("gja", "gya"),
        ("gji", "gyi"),
        ("gju", "gyu"),
        ("gje", "gye"),
        ("gjo", "gyo"),
        ("nja", "nya"),
        ("nji", "nyi"),
        ("nju", "nyu"),
        ("nje", "nye"),
        ("njo", "nyo"),
        ("mja", "mya"),
        ("mji", "myi"),
        ("mju", "myu"),
        ("mje", "mye"),
        ("mjo", "myo"),
        ("hja", "hya"),
        ("hji", "hyi"),
        ("hju", "hyu"),
        ("hje", "hye"),
        ("hjo", "hyo"),
        ("bja", "bya"),
        ("bji", "byi"),
        ("bju", "byu"),
        ("bje", "bye"),
        ("bjo", "byo"),
        ("pja", "pya"),
        ("pji", "pyi"),
        ("pju", "pyu"),
        ("pje", "pye"),
        ("pjo", "pyo"),
        ("jú", "yū"),
        ("jó", "yō"),
        ("ja", "ya"),
        ("ji", "yi"),
        ("ju", "yu"),
        ("je", "ye"),
        ("jo", "yo"),
        // Long vowels
        ("ó", "ō"),
        ("ú", "ū"),
    ];

    /// <summary>
    /// Czech phonetic transcription → Revised Romanization of Korean.
    /// </summary>
    private static readonly (string Czech, string English)[] KoreanRules =
    [
        ("šin", "sin"),
        ("šim", "sim"),
        ("ča", "ja"),
        ("čo", "jo"),
        ("ču", "ju"),
        ("če", "je"),
        ("či", "ji"),
        ("š", "s"),
        ("č", "j"),
        ("ů", "u"),
    ];

    /// <summary>
    /// Applies Czech→English romanization rules for Japanese names.
    /// Input should be lowercased. Output preserves lowercase.
    /// </summary>
    public static string JapaneseToHepburn(string czechName)
    {
        return ApplyRules(czechName, JapaneseRules);
    }

    /// <summary>
    /// Applies Czech→English romanization rules for Korean names.
    /// Input should be lowercased. Output preserves lowercase.
    /// </summary>
    public static string KoreanToRevised(string czechName)
    {
        return ApplyRules(czechName, KoreanRules);
    }

    /// <summary>
    /// Applies both Japanese and Korean transliteration and returns
    /// the variant that differs most from the input (i.e., had the most rules applied).
    /// If neither changes the input, returns it unchanged.
    /// </summary>
    public static string TransliterateToEnglish(string czechName)
    {
        if (string.IsNullOrWhiteSpace(czechName))
        {
            return czechName;
        }

        // Only apply transliteration if input contains non-ASCII characters (Czech diacritics).
        // This prevents false positives on Western/English names (e.g., "co" in "scorsese" → "tso").
        if (!czechName.Any(c => c > 127))
        {
            return czechName;
        }

        var japanese = JapaneseToHepburn(czechName);
        var korean = KoreanToRevised(czechName);

        // Return whichever one changed more (had more rules applied)
        var japaneseDiff = LevenshteinDistance(czechName, japanese);
        var koreanDiff = LevenshteinDistance(czechName, korean);

        if (japaneseDiff == 0 && koreanDiff == 0)
        {
            return czechName; // No rules applied
        }

        return japaneseDiff >= koreanDiff ? japanese : korean;
    }

    private static string ApplyRules(string input, (string Czech, string English)[] rules)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var result = new StringBuilder(input.Length + 8);
        var i = 0;

        while (i < input.Length)
        {
            var matched = false;
            foreach (var (czech, english) in rules)
            {
                if (i + czech.Length <= input.Length &&
                    input.AsSpan(i, czech.Length).Equals(czech.AsSpan(), StringComparison.Ordinal))
                {
                    result.Append(english);
                    i += czech.Length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                result.Append(input[i]);
                i++;
            }
        }

        return result.ToString();
    }

    private static int LevenshteinDistance(string s, string t)
    {
        if (string.Equals(s, t, StringComparison.Ordinal))
        {
            return 0;
        }

        var n = s.Length;
        var m = t.Length;

        if (n == 0) return m;
        if (m == 0) return n;

        var previous = new int[m + 1];
        var current = new int[m + 1];

        for (var j = 0; j <= m; j++)
        {
            previous[j] = j;
        }

        for (var x = 1; x <= n; x++)
        {
            current[0] = x;
            for (var y = 1; y <= m; y++)
            {
                var cost = s[x - 1] == t[y - 1] ? 0 : 1;
                current[y] = Math.Min(
                    Math.Min(current[y - 1] + 1, previous[y] + 1),
                    previous[y - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[m];
    }
}
