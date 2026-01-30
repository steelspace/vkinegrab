using HtmlAgilityPack;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using vkinegrab.Models;

namespace vkinegrab.Services;

internal sealed class ImdbResolver
{
    private readonly HttpClient client;

    public ImdbResolver(HttpClient client)
    {
        this.client = client;
    }

    public async Task<string?> ResolveImdbIdAsync(HtmlDocument csfdDoc, CsfdMovie movie)
    {
        var directLink = csfdDoc.DocumentNode.SelectSingleNode("//a[contains(@href, 'imdb.com/title/tt')]");
        if (directLink != null)
        {
            var href = directLink.GetAttributeValue("href", string.Empty);
            var match = Regex.Match(href, @"tt\d+");
            if (match.Success)
            {
                var imdbId = match.Value;
                if (await ValidateImdbTitleAsync(imdbId, movie))
                {
                    return imdbId;
                }
            }
        }

        foreach (var candidateTitle in GetImdbSearchTitles(movie))
        {
            var imdbId = await SearchImdbForTitleAsync(candidateTitle, movie);
            if (!string.IsNullOrEmpty(imdbId))
            {
                return imdbId;
            }
        }

        return null;
    }

    private async Task<string?> SearchImdbForTitleAsync(string title, CsfdMovie movie)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }



        // Try with year first
        var terms = new List<string> { title };
        if (!string.IsNullOrWhiteSpace(movie.Year))
        {
            terms.Add(movie.Year!);
        }

        var query = string.Join(" ", terms.Where(t => !string.IsNullOrWhiteSpace(t)));
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        // Try feature film search first
        var result = await TryImdbSearch(query, movie, "ft");
        if (result != null)
        {
            return result;
        }

        // Fallback: search all titles (includes TV movies, documentaries, etc.)
        result = await TryImdbSearch(query, movie, null);
        if (result != null)
        {
            return result;
        }

        // Last resort: try without year
        if (!string.IsNullOrWhiteSpace(movie.Year))
        {
            return await TryImdbSearch(title, movie, null);
        }

        return null;
    }

    private async Task<string?> TryImdbSearch(string query, CsfdMovie movie, string? titleType)
    {
        var searchUrl = titleType != null
            ? $"https://www.imdb.com/find/?q={Uri.EscapeDataString(query)}&s=tt&ttype={titleType}"
            : $"https://www.imdb.com/find/?q={Uri.EscapeDataString(query)}";

        string searchHtml;
        try
        {
            searchHtml = await client.GetStringAsync(searchUrl);
        }
        catch
        {
            return null;
        }

        var searchDoc = new HtmlDocument();
        searchDoc.LoadHtml(searchHtml);

        var results = ExtractImdbResults(searchDoc).ToList();
        Console.WriteLine($"    Found {results.Count} results");
        foreach (var r in results.Take(3))
        {
            Console.WriteLine($"      - {r.Id}: '{r.Title}' ({r.Year})");
        }
        
        if (results.Count == 0)
        {
            return null;
        }

        // Extract title from query (remove year if present)
        var queryTitle = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                              .Where(part => !Regex.IsMatch(part, @"^\d{4}$"))
                              .Aggregate("", (acc, part) => acc + " " + part).Trim();

        var normalizedTargets = BuildNormalizedTitleSet(movie, queryTitle);
        var prioritized = new List<ImdbSearchResult>();
        var secondary = new List<ImdbSearchResult>();

        foreach (var result in results)
        {
            var normalizedTitle = NormalizeTitle(result.Title);
            var titleMatches = normalizedTargets.Count > 0 && normalizedTargets.Contains(normalizedTitle);
            var yearMatches = TitlesShareYear(movie.Year, result);

            if (titleMatches)
            {
                prioritized.Add(result);
                continue;
            }

            if (yearMatches)
            {
                secondary.Add(result);
            }
        }

        foreach (var candidate in prioritized)
        {
            if (await ValidateImdbTitleAsync(candidate.Id, movie))
            {
                return candidate.Id;
            }
        }

        foreach (var candidate in secondary)
        {
            if (await ValidateImdbTitleAsync(candidate.Id, movie))
            {
                return candidate.Id;
            }
        }

        return null;
    }

    private IEnumerable<string> GetImdbSearchTitles(CsfdMovie movie)
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

    private string? GetLocalizedTitle(CsfdMovie movie, params string[] countryAliases)
    {
        foreach (var alias in countryAliases)
        {
            foreach (var kvp in movie.LocalizedTitles)
            {
                if (kvp.Key.Equals(alias, StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Contains(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }
        }

        return null;
    }

    private HashSet<string> BuildNormalizedTitleSet(CsfdMovie movie, string? primaryTitle)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        void TryAdd(string? value)
        {
            var normalized = NormalizeTitle(value);
            if (!string.IsNullOrEmpty(normalized))
            {
                set.Add(normalized);
            }
        }

        TryAdd(primaryTitle);
        TryAdd(movie.Title);

        foreach (var localized in movie.LocalizedTitles.Values)
        {
            TryAdd(localized);
        }

        return set;
    }

    private string NormalizeTitle(string? value)
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

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private bool TitlesShareYear(string? movieYear, ImdbSearchResult result)
    {
        if (string.IsNullOrWhiteSpace(movieYear))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(result.Year) && result.Year == movieYear)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(result.RawText))
        {
            var match = Regex.Match(result.RawText, @"\b(\d{4})\b");
            if (match.Success && match.Groups[1].Value == movieYear)
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<ImdbSearchResult> ExtractImdbResults(HtmlDocument doc)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in ExtractLegacyResults(doc))
        {
            if (seen.Add(result.Id))
            {
                yield return result;
            }
        }

        foreach (var result in ExtractModernResults(doc))
        {
            if (seen.Add(result.Id))
            {
                yield return result;
            }
        }
    }

    private IEnumerable<ImdbSearchResult> ExtractLegacyResults(HtmlDocument doc)
    {
        var rows = doc.DocumentNode.SelectNodes("//table[contains(@class, 'findList')]//tr");
        if (rows == null)
        {
            yield break;
        }

        foreach (var row in rows)
        {
            var textCell = row.SelectSingleNode(".//td[@class='result_text']");
            var linkNode = textCell?.SelectSingleNode(".//a");
            if (textCell == null || linkNode == null)
            {
                continue;
            }

            var href = linkNode.GetAttributeValue("href", string.Empty);
            var match = Regex.Match(href, @"tt\d+");
            if (!match.Success)
            {
                continue;
            }

            var rawText = WebUtility.HtmlDecode(textCell.InnerText) ?? string.Empty;
            var titleText = WebUtility.HtmlDecode(linkNode.InnerText)?.Trim() ?? string.Empty;
            var yearMatch = Regex.Match(rawText, @"\((\d{4})\)");
            var year = yearMatch.Success ? yearMatch.Groups[1].Value : null;

            yield return new ImdbSearchResult(match.Value, titleText, year, rawText);
        }
    }

    private IEnumerable<ImdbSearchResult> ExtractModernResults(HtmlDocument doc)
    {
        // Try "Movies" first (when filtered by type), then "Titles" (unfiltered results)
        var section = doc.DocumentNode.SelectSingleNode("//section[@data-testid='find-results-section-title'][.//h3[text()='Movies']]")
            ?? doc.DocumentNode.SelectSingleNode("//section[@data-testid='find-results-section-title'][.//h3[text()='Titles']]");
        if (section == null)
        {
            yield break;
        }

        var items = section.SelectNodes(".//li[contains(@class, 'ipc-metadata-list-summary-item')]");
        if (items == null)
        {
            yield break;
        }

        foreach (var item in items)
        {
            var linkNode = item.SelectSingleNode(".//a[contains(@href, '/title/tt')][1]");
            if (linkNode == null)
            {
                continue;
            }

            var href = linkNode.GetAttributeValue("href", string.Empty);
            var match = Regex.Match(href, @"tt\d+");
            if (!match.Success)
            {
                continue;
            }

            // Extract title from aria-label (format: "View title page for <Title>")
            var ariaLabel = linkNode.GetAttributeValue("aria-label", string.Empty);
            var titleText = string.Empty;
            if (!string.IsNullOrWhiteSpace(ariaLabel))
            {
                var prefixMatch = Regex.Match(ariaLabel, @"(?:View title page for |)(.+)$");
                if (prefixMatch.Success)
                {
                    titleText = WebUtility.HtmlDecode(prefixMatch.Groups[1].Value)?.Trim() ?? string.Empty;
                }
            }
            
            // Fallback to InnerText if aria-label parsing failed
            if (string.IsNullOrWhiteSpace(titleText))
            {
                titleText = WebUtility.HtmlDecode(linkNode.InnerText)?.Trim() ?? string.Empty;
            }
            
            string? year = null;
            var rawBuilder = new StringBuilder();

            var metaSpans = item.SelectNodes(".//span[contains(@class, 'cli-title-metadata-item')]");
            if (metaSpans != null)
            {
                foreach (var span in metaSpans)
                {
                    var text = WebUtility.HtmlDecode(span.InnerText) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        rawBuilder.Append(' ').Append(text);
                        var yearMatch = Regex.Match(text, @"\b(\d{4})\b");
                        if (yearMatch.Success && string.IsNullOrWhiteSpace(year))
                        {
                            year = yearMatch.Groups[1].Value;
                        }
                    }
                }
            }

            yield return new ImdbSearchResult(match.Value, titleText, year, rawBuilder.ToString());
        }
    }

    private async Task<bool> ValidateImdbTitleAsync(string imdbId, CsfdMovie movie)
    {
        var hasYear = !string.IsNullOrWhiteSpace(movie.Year);
        var hasDirectors = movie.Directors != null && movie.Directors.Count > 0;

        if (!hasYear && !hasDirectors)
        {
            return true;
        }

        var metadata = await FetchTitleMetadataAsync(imdbId);
        if (metadata == null)
        {
            Console.WriteLine($"      Validation: No metadata found for {imdbId}, accepting by default");
            return true;
        }

        var yearValid = IsYearValid(movie.Year, metadata.Year);
        var directorsValid = AreDirectorsValid(movie.Directors, metadata.Directors);



        if (hasYear && hasDirectors)
        {
            // If directors don't match but year does, accept it (IMDb may not have director info from HTML fallback)
            if (yearValid && !directorsValid && metadata.Directors.Count == 0)
            {

                return true;
            }
            return yearValid && directorsValid;
        }

        if (hasYear)
        {
            return yearValid;
        }

        return directorsValid;
    }

    private bool IsYearValid(string? movieYear, string? imdbYear)
    {
        if (string.IsNullOrWhiteSpace(movieYear))
        {
            return true;
        }

        var normalizedMovieYear = ExtractYearDigits(movieYear);
        if (string.IsNullOrEmpty(normalizedMovieYear))
        {
            return true;
        }

        if (string.IsNullOrEmpty(imdbYear))
        {
            return false;
        }

        if (string.Equals(normalizedMovieYear, imdbYear, StringComparison.Ordinal))
        {
            return true;
        }

        var normalizedImdbYear = ExtractYearDigits(imdbYear);
        if (int.TryParse(normalizedMovieYear, out var movieYearValue) && 
            int.TryParse(normalizedImdbYear, out var imdbYearValue))
        {
            return Math.Abs(movieYearValue - imdbYearValue) <= 1;
        }

        return false;
    }

    private string? ExtractYearDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(value, "\\b(\\d{4})\\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    private bool AreDirectorsValid(IReadOnlyCollection<string>? movieDirectors, IReadOnlyList<string> imdbDirectors)
    {
        if (movieDirectors == null || movieDirectors.Count == 0)
        {
            return true;
        }

        if (imdbDirectors.Count == 0)
        {
            return false;
        }

        var normalizedImdb = new HashSet<string>(imdbDirectors.Select(NormalizePersonName).Where(n => !string.IsNullOrEmpty(n)), StringComparer.Ordinal);
        if (normalizedImdb.Count == 0)
        {
            return false;
        }

        foreach (var director in movieDirectors)
        {
            var normalizedDirector = NormalizePersonName(director);
            if (string.IsNullOrEmpty(normalizedDirector))
            {
                continue;
            }

            if (!normalizedImdb.Contains(normalizedDirector))
            {
                return false;
            }
        }

        return true;
    }

    private string NormalizePersonName(string? value)
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

            if (char.IsLetter(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                sb.Append(' ');
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', System.StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task<ImdbTitleMetadata?> FetchTitleMetadataAsync(string imdbId)
    {
        var titleUrl = $"https://www.imdb.com/title/{imdbId}/";

        string html;
        try
        {
            html = await client.GetStringAsync(titleUrl);
        }
        catch
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var scriptNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scriptNodes == null)
        {
            return null;
        }

        foreach (var scriptNode in scriptNodes)
        {
            var jsonText = WebUtility.HtmlDecode(scriptNode.InnerText);
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                continue;
            }

            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonText);
                if (TryCreateMetadata(jsonDoc.RootElement, out var metadata))
                {
                    return metadata;
                }
            }
            catch (JsonException)
            {
                continue;
            }
        }

        // Fallback: Try to extract year from HTML title tag (e.g., "Title (1944) - IMDb")
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (titleNode != null)
        {
            var titleText = WebUtility.HtmlDecode(titleNode.InnerText);
            var yearMatch = System.Text.RegularExpressions.Regex.Match(titleText, @"\((\d{4})\)");
            if (yearMatch.Success)
            {
                var year = yearMatch.Groups[1].Value;
                // Note: Directors list will be empty, but year validation will work
                return new ImdbTitleMetadata(year, new List<string>());
            }
        }

        return null;
    }

    private bool TryCreateMetadata(JsonElement element, out ImdbTitleMetadata metadata)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("@graph", out var graphElement) && graphElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in graphElement.EnumerateArray())
                {
                    if (TryCreateMetadata(node, out metadata))
                    {
                        return true;
                    }
                }
            }

            if (element.TryGetProperty("@type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
            {
                var type = typeElement.GetString();
                if (string.Equals(type, "Movie", StringComparison.OrdinalIgnoreCase))
                {
                    var year = ExtractYearFromElement(element);
                    var directors = ExtractDirectorsFromElement(element);

                    // If JSON-LD doesn't have year, return false so HTML fallback can be used
                    if (year == null)
                    {
                        metadata = default!;
                        return false;
                    }
                    
                    metadata = new ImdbTitleMetadata(year, directors);
                    return true;
                }
            }
        }

        metadata = default!;
        return false;
    }

    private string? ExtractYearFromElement(JsonElement element)
    {
        if (element.TryGetProperty("datePublished", out var datePublished) && datePublished.ValueKind == JsonValueKind.String)
        {
            var dateStr = datePublished.GetString();
            var year = ExtractYearDigits(dateStr);
            if (!string.IsNullOrEmpty(year))
            {
                return year;
            }
        }

        if (element.TryGetProperty("releaseDate", out var releaseDate) && releaseDate.ValueKind == JsonValueKind.String)
        {
            var dateStr = releaseDate.GetString();
            var year = ExtractYearDigits(dateStr);
            if (!string.IsNullOrEmpty(year))
            {
                return year;
            }
        }

        if (element.TryGetProperty("releasedEvent", out var releasedEvent) && releasedEvent.ValueKind == JsonValueKind.Array)
        {
            foreach (var ev in releasedEvent.EnumerateArray())
            {
                if (ev.ValueKind == JsonValueKind.Object && ev.TryGetProperty("startDate", out var startDate) && startDate.ValueKind == JsonValueKind.String)
                {
                    var dateStr = startDate.GetString();
                    var year = ExtractYearDigits(dateStr);
                    if (!string.IsNullOrEmpty(year))
                    {
                        return year;
                    }
                }
            }
        }

        return null;
    }

    private List<string> ExtractDirectorsFromElement(JsonElement element)
    {
        var directors = new List<string>();

        if (element.TryGetProperty("director", out var directorElement))
        {
            if (directorElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in directorElement.EnumerateArray())
                {
                    var name = ExtractDirectorName(item);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        directors.Add(name);
                    }
                }
            }
            else
            {
                var name = ExtractDirectorName(directorElement);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    directors.Add(name);
                }
            }
        }

        return directors;
    }

    private string? ExtractDirectorName(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
        {
            return nameElement.GetString();
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return null;
    }

    private sealed class ImdbSearchResult
    {
        public ImdbSearchResult(string id, string title, string? year, string rawText)
        {
            Id = id;
            Title = title;
            Year = year;
            RawText = rawText;
        }

        public string Id { get; }

        public string Title { get; }

        public string? Year { get; }

        public string RawText { get; }
    }

    private sealed class ImdbTitleMetadata
    {
        public ImdbTitleMetadata(string? year, IReadOnlyList<string> directors)
        {
            Year = year;
            Directors = directors;
        }

        public string? Year { get; }

        public IReadOnlyList<string> Directors { get; }
    }
}
