using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using vkinegrab.Models;
using vkinegrab.Services.Imdb.Models;

namespace vkinegrab.Services.Imdb;

internal sealed class ImdbTitleMatcher
{
    public IEnumerable<string> GetSearchTitles(CsfdMovie movie)
    {
        var candidates = new List<string?>
        {
            GetLocalizedTitle(movie, "USA", "United States", "Spojené státy"),
            GetLocalizedTitle(movie, "Velká Británie", "United Kingdom", "UK", "Spojené království"),
            movie.Title
        };

        // Add origin country title as high priority
        if (!string.IsNullOrWhiteSpace(movie.Origin))
        {
            var originCountries = movie.Origin.Split(new[] { '/', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(c => c.Trim())
                                              .ToList();
            
            foreach (var country in originCountries)
            {
                var originTitle = GetLocalizedTitle(movie, country);
                if (!string.IsNullOrWhiteSpace(originTitle))
                {
                    candidates.Insert(0, originTitle); // Add at beginning for priority
                }
            }
        }

        // Prioritize English titles
        var englishTitle = GetLocalizedTitle(movie, "angličtina", "English", "USA", "United States", "UK", "United Kingdom");
        if (!string.IsNullOrWhiteSpace(englishTitle))
        {
            candidates.Insert(0, englishTitle);
        }

        candidates.AddRange(movie.LocalizedTitles.Values);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var trimmed = candidate.Trim();
            if (seen.Add(trimmed))
            {
                yield return trimmed;
            }
        }
    }

    public HashSet<string> BuildNormalizedTitleSet(CsfdMovie movie, string queryTitle)
    {
        var candidates = new List<string?> { movie.Title, queryTitle };
        candidates.AddRange(movie.LocalizedTitles.Values);

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var norm = NormalizeTitle(candidate);
            if (!string.IsNullOrEmpty(norm))
            {
                normalized.Add(norm);
            }
        }

        return normalized;
    }

    public string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var normalized = title.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    public bool TitlesShareYear(string? movieYear, ImdbSearchResult result)
    {
        // Allow ±2 year tolerance for TV episodes, different release regions, etc.
        if (string.IsNullOrWhiteSpace(movieYear))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(result.Year) && YearsMatch(movieYear, result.Year))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(result.RawText))
        {
            // Check all 4-digit years in RawText (important for TV episodes where series year != episode year)
            var matches = Regex.Matches(result.RawText, @"\b(\d{4})\b");
            foreach (Match match in matches)
            {
                var yearStr = match.Groups[1].Value;
                if (YearsMatch(movieYear, yearStr))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool YearsMatch(string year1, string year2, int tolerance = 2)
    {
        if (year1 == year2)
        {
            return true;
        }

        if (int.TryParse(year1, out var y1) && int.TryParse(year2, out var y2))
        {
            return Math.Abs(y1 - y2) <= tolerance;
        }

        return false;
    }

    public string NormalizePersonName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetter(ch) || char.IsWhiteSpace(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private string? GetLocalizedTitle(CsfdMovie movie, params string[] countryNames)
    {
        foreach (var country in countryNames)
        {
            if (movie.LocalizedTitles.TryGetValue(country, out var title) && !string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }

        return null;
    }
}
