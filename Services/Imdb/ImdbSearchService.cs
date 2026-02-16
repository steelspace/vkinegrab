using HtmlAgilityPack;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using vkinegrab.Services.Imdb.Models;

namespace vkinegrab.Services.Imdb;

internal sealed class ImdbSearchService
{
    private readonly HttpClient client;

    public ImdbSearchService(HttpClient client)
    {
        this.client = client;
    }

    public async Task<List<ImdbSearchResult>> Search(string query, string? titleType = null)
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
            return new List<ImdbSearchResult>();
        }

        var searchDoc = new HtmlDocument();
        searchDoc.LoadHtml(searchHtml);

        return ExtractImdbResults(searchDoc).ToList();
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
            var titleType = ExtractTitleTypeFromRawText(rawText);

            yield return new ImdbSearchResult(match.Value, titleText, year, rawText, titleType);
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
            string? titleType = null;
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

            // Extract title type label (e.g., "TV Series", "Podcast Series", "TV Episode", "Video Game")
            // These appear in a separate label element within the search result item
            var typeLabel = item.SelectSingleNode(".//span[contains(@class, 'ipc-metadata-list-summary-item__tl')]")
                ?? item.SelectSingleNode(".//label[contains(@class, 'ipc-metadata-list-summary-item__tl')]");
            if (typeLabel != null)
            {
                titleType = WebUtility.HtmlDecode(typeLabel.InnerText)?.Trim();
            }

            // Also check the raw text for type indicators
            if (string.IsNullOrWhiteSpace(titleType))
            {
                var rawText = rawBuilder.ToString();
                titleType = ExtractTitleTypeFromRawText(rawText);
            }

            yield return new ImdbSearchResult(match.Value, titleText, year, rawBuilder.ToString(), titleType);
        }
    }

    /// <summary>
    /// Extracts a title type indicator from raw search result text.
    /// IMDB search results often contain labels like "(TV Series)", "(Podcast Series)", "(Video Game)", etc.
    /// </summary>
    private static string? ExtractTitleTypeFromRawText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        var typeMatch = Regex.Match(rawText, @"\b(TV Series|TV Mini Series|TV Movie|TV Episode|TV Special|TV Short|Podcast Series|Podcast Episode|Video Game|Video|Short|Music Video)\b", RegexOptions.IgnoreCase);
        return typeMatch.Success ? typeMatch.Groups[1].Value : null;
    }
}
