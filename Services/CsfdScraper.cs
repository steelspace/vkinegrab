using HtmlAgilityPack;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using vkinegrab.Models;

namespace vkinegrab.Services;

public class CsfdScraper
{
    private static readonly HttpClient _client;

    static CsfdScraper()
    {
        // 1. Setup HttpClient with realistic headers (Crucial for CSFD)
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        _client = new HttpClient(handler);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
    }

    public async Task<CsfdMovie> ScrapeMovieAsync(int movieId)
    {
        // CSFD handles numeric IDs by redirecting to the full URL (usually), or just serving the content.
        // We can just query /film/ID
        return await ScrapeMovieAsync($"https://www.csfd.cz/film/{movieId}");
    }

    public async Task<CsfdMovie> ScrapeMovieAsync(string url)
    {
        Console.WriteLine($"Downloading: {url}");
        var html = await _client.GetStringAsync(url);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var movie = new CsfdMovie();
        movie.Id = ExtractIdFromUrl(url);
        var mainNode = doc.DocumentNode;

        // 2. Title - Text inside the H1 usually contains the title (sometimes followed by (year))
        var h1Node = mainNode.SelectSingleNode("//header[@class='film-header']//h1");
        movie.Title = Clean(h1Node?.InnerText);
        if (!string.IsNullOrEmpty(movie.Title))
        {
            movie.LocalizedTitles.TryAdd("Original", movie.Title);
        }

        var localizedTitleNodes = mainNode.SelectNodes("//ul[contains(@class, 'film-names')]/li");
        if (localizedTitleNodes != null)
        {
            foreach (var node in localizedTitleNodes)
            {
                var textNode = node.ChildNodes.FirstOrDefault(n => n.NodeType == HtmlNodeType.Text && !string.IsNullOrWhiteSpace(n.InnerText));
                var localizedTitle = Clean(textNode?.InnerText);
                if (string.IsNullOrEmpty(localizedTitle))
                {
                    continue;
                }

                var flagNode = node.SelectSingleNode(".//img");
                var country = Clean(flagNode?.GetAttributeValue("title", string.Empty))
                              ?? Clean(flagNode?.GetAttributeValue("alt", string.Empty))
                              ?? "Unspecified";

                movie.LocalizedTitles.TryAdd(country, localizedTitle);
            }
        }

        // 3. Rating
        // The class is often 'film-rating-average' or just 'rating-average' depending on A/B tests or page type
        var ratingNode = mainNode.SelectSingleNode("//div[contains(@class, 'film-rating-average')]");
        movie.Rating = Clean(ratingNode?.InnerText);

        // 4. Genres - Located in the film header info area
        var genreNodes = mainNode.SelectNodes("//div[contains(@class, 'genres')]//a");
        if (genreNodes != null)
            movie.Genres = genreNodes.Select(n => Clean(n.InnerText)).Where(x => !string.IsNullOrEmpty(x)).ToList()!;

        // 5. Origin, Year, Duration
        // Usually plain text in a div like: "USA, 2024, 124 min"
        var originNode = mainNode.SelectSingleNode("//div[contains(@class, 'origin')]");
        if (originNode != null)
        {
            var rawText = Clean(originNode.InnerText);
            if (!string.IsNullOrEmpty(rawText))
            {
                var parts = rawText.Split(',').Select(s => s.Trim()).ToList();
                
                if (parts.Count > 0) movie.Origin = parts[0];
                if (parts.Count > 1) movie.Year = parts[1];
                if (parts.Count > 2) movie.Duration = parts.Last(); // Duration is usually last
            }
        }

        // 6. Creators (Directors)
        // Look for the header "Režie:" and get the links following it in the same span/div container
        movie.Directors = GetCreators(mainNode, "Režie");

        // 7. Cast (Actors)
        // Look for "Hrají:"
        movie.Cast = GetCreators(mainNode, "Hrají");

        // 8. Description / Plot
        // Priority: 'plot-full' > 'plot-preview' > standard plots list
        var plotNode = mainNode.SelectSingleNode("//div[contains(@class, 'plot-full')]/p")
                    ?? mainNode.SelectSingleNode("//div[contains(@class, 'plot-preview')]/p")
                    ?? mainNode.SelectSingleNode("//div[contains(@class, 'plots')]//div[@class='plot-item']/p");
        
        movie.Description = Clean(plotNode?.InnerText);

        // 9. Poster
        // Look for the image inside 'film-posters'. Handle relative URLs.
        var posterImg = mainNode.SelectSingleNode("//div[contains(@class, 'film-posters')]//img");
        if (posterImg != null)
        {
            // CSFD sometimes uses srcset; try grabbing that or src
            var src = posterImg.GetAttributeValue("src", "");
            
            // Fix protocol-relative URLs (e.g. //img.csfd...)
            if (src.StartsWith("//")) src = "https:" + src;
            
            // Ignore base64 placeholders if possible, but for simple scraping:
            if (!src.Contains("data:image")) 
            {
                movie.PosterUrl = src;
            }
        }

        movie.ImdbId = await TryResolveImdbIdAsync(doc, movie);

        return movie;
    }

    private async Task<string?> TryResolveImdbIdAsync(HtmlDocument csfdDoc, CsfdMovie movie)
    {
        var directLink = csfdDoc.DocumentNode.SelectSingleNode("//a[contains(@href, 'imdb.com/title/tt')]");
        if (directLink != null)
        {
            var href = directLink.GetAttributeValue("href", string.Empty);
            var match = Regex.Match(href, @"tt\d+");
            if (match.Success)
            {
                return match.Value;
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

        var searchUrl = $"https://www.imdb.com/find/?s=tt&ttype=ft&q={Uri.EscapeDataString(query)}";

        string searchHtml;
        try
        {
            searchHtml = await _client.GetStringAsync(searchUrl);
        }
        catch
        {
            return null;
        }

        var searchDoc = new HtmlDocument();
        searchDoc.LoadHtml(searchHtml);

        var results = ExtractImdbResults(searchDoc).ToList();
        if (results.Count == 0)
        {
            return null;
        }

        var normalizedTargets = BuildNormalizedTitleSet(movie, title);
        string? fallbackMatch = null;

        foreach (var result in results)
        {
            var normalizedTitle = NormalizeTitle(result.Title);
            var yearMatches = TitlesShareYear(movie.Year, result);

            if (normalizedTargets.Count > 0 && normalizedTargets.Contains(normalizedTitle))
            {
                if (yearMatches || string.IsNullOrWhiteSpace(movie.Year))
                {
                    return result.Id;
                }

                fallbackMatch ??= result.Id;
                continue;
            }

            if (fallbackMatch == null && yearMatches)
            {
                fallbackMatch = result.Id;
            }
        }

        return fallbackMatch;
    }

    private IEnumerable<string> GetImdbSearchTitles(CsfdMovie movie)
    {
        var candidates = new List<string?>
        {
            GetLocalizedTitle(movie, "USA", "United States", "Spojené státy"),
            GetLocalizedTitle(movie, "Velká Británie", "United Kingdom", "UK", "Spojené království"),
            movie.Title
        };

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
        var section = doc.DocumentNode.SelectSingleNode("//section[@data-testid='find-results-section-title'][.//h3[text()='Movies']]");
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

            var titleText = WebUtility.HtmlDecode(linkNode.InnerText)?.Trim() ?? string.Empty;
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

    // Helper to extract list of people (actors, directors) based on the label (e.g. "Režie:", "Hrají:")
    private List<string> GetCreators(HtmlNode root, string labelSnippet)
    {
        var creators = new List<string>();
        // Try to find the H4 containing the label
        var h4Node = root.SelectSingleNode($"//div[contains(@class, 'creators')]//h4[contains(text(), '{labelSnippet}')]");
        
        if (h4Node != null)
        {
            // The links are usually in a sibling span or directly in the parent div
            var parent = h4Node.ParentNode;
            var links = parent.SelectNodes(".//a"); // Get all links in the same block
            
            if (links != null)
            {
                foreach (var link in links)
                {
                    var name = Clean(link.InnerText);
                    // Filter out "více" and empty strings
                    if (!string.IsNullOrEmpty(name) && !name.Equals("více", StringComparison.OrdinalIgnoreCase))
                    {
                        creators.Add(name);
                    }
                }
            }
        }
        return creators;
    }

    private string? Clean(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        return HtmlEntity.DeEntitize(input).Trim();
    }

    private int ExtractIdFromUrl(string url)
    {
        var match = Regex.Match(url, @"film/(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
        {
            return id;
        }
        return 0;
    }
}
