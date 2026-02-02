using HtmlAgilityPack;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using vkinegrab.Services.Imdb.Models;

namespace vkinegrab.Services.Imdb;

public class ResilientHttpClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly CookieContainer _cookieContainer = new();
    private readonly Random _random = new();

    private readonly string[] _userAgents = 
    {
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36"
    };

    public ResilientHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
            // Mimic browser connection behavior
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
            }
        };

        _client = new HttpClient(handler);
    }

    public async Task<string> GetImdbSearchAsync(string query)
    {
        // 1. Randomize Fingerprint
        string ua = _userAgents[_random.Next(_userAgents.Length)];
        
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.imdb.com/find?q={Uri.EscapeDataString(query)}");

        // 2. Set Mandatory Browser Headers
        request.Headers.Add("User-Agent", ua);
        request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        request.Headers.Add("Referer", "https://www.imdb.com/");
        
        // 3. Add Client Hints (Crucial for modern Chromium-based detection)
        request.Headers.Add("sec-ch-ua", "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"121\", \"Google Chrome\";v=\"121\"");
        request.Headers.Add("sec-ch-ua-mobile", "?0");
        request.Headers.Add("sec-ch-ua-platform", "\"Windows\"");
        request.Headers.Add("Sec-Fetch-Dest", "document");
        request.Headers.Add("Sec-Fetch-Mode", "navigate");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");

        // 4. Random Delay to simulate human thinking time
        await Task.Delay(_random.Next(1000, 3000));

        var response = await _client.SendAsync(request);

        // Handle the 202 "Accepted" but empty body scenario
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            // If we get a 202, IMDb is likely "queuing" or soft-blocking.
            // A common fix is to wait and retry once with the cookies we just received.
            await Task.Delay(2000);
            return await GetImdbSearchAsync(query); 
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public void Dispose() => _client.Dispose();
}

internal sealed class ImdbSearchService
{
    private readonly ResilientHttpClient _resilientClient;

    public ImdbSearchService(HttpClient client)
    {
        // Ignore the injected client, use our resilient one
        _resilientClient = new ResilientHttpClient();
    }

    public async Task<List<ImdbSearchResult>> Search(string query, string? titleType = null)
    {
        var titleTypeParam = titleType ?? "ft";
        var searchUrl = $"https://www.imdb.com/find?q={Uri.EscapeDataString(query)}";

        string searchHtml;
        try
        {
            Console.WriteLine($"    Search URL: {searchUrl}");
            searchHtml = await _resilientClient.GetImdbSearchAsync(query);
            Console.WriteLine($"    HTML length: {searchHtml.Length}, contains findList: {searchHtml.Contains("findList")}, contains ipc-metadata: {searchHtml.Contains("ipc-metadata-list-summary-item")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Search failed: {ex.Message}");
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
}
