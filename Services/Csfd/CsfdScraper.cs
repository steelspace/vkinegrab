using HtmlAgilityPack;
using System.Net;
using System.Text.RegularExpressions;
using vkinegrab.Models;
using vkinegrab.Services;
using vkinegrab.Services.Imdb;
using vkinegrab.Services.Tmdb;

namespace vkinegrab.Services.Csfd;

public class CsfdScraper : ICsfdScraper
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ImdbResolver imdbResolver;
    private readonly TmdbResolver tmdbResolver;
    private static readonly Regex OriginSplitRegex = new("[/·•–—|&]", RegexOptions.Compiled);

    public CsfdScraper(IHttpClientFactory httpClientFactory, string tmdbBearerToken)
    {
        this.httpClientFactory = httpClientFactory;
        
        var csfdClient = httpClientFactory.CreateClient("Csfd");
        imdbResolver = new ImdbResolver(csfdClient);
        
        var tmdbClient = httpClientFactory.CreateClient("Tmdb");
        tmdbResolver = new TmdbResolver(tmdbClient, tmdbBearerToken);
    }

    public async Task<CsfdMovie> ScrapeMovie(int movieId, bool resolveImdb = true)
    {
        // CSFD handles numeric IDs by redirecting to the full URL (usually), or just serving the content.
        // We can just query /film/ID
        return await ScrapeMovie($"https://www.csfd.cz/film/{movieId}", resolveImdb);
    }

    public async Task<CsfdMovie> ScrapeMovie(string url, bool resolveImdb = true)
    {
        Console.WriteLine($"Downloading: {url}");
        var client = httpClientFactory.CreateClient("Csfd");
        var html = await client.GetStringAsync(url);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var movie = new CsfdMovie();
        movie.Id = ExtractIdFromUrl(url);
        var mainNode = doc.DocumentNode;

        var typeNodes = mainNode.SelectNodes("//span[contains(@class, 'type')]");
        movie.IsStudentFilm = typeNodes != null && typeNodes.Any(IsStudentFilmNode);

        // 2. Title - Text inside the H1 usually contains the title (sometimes followed by (year))
        var h1Node = mainNode.SelectSingleNode("//h1");
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

                var countryCodes = CountryCodeMapper.MapToIsoAlpha2(new[] { country });
                var countryKey = countryCodes.Count > 0 ? countryCodes[0] : country;
                movie.LocalizedTitles.TryAdd(countryKey, localizedTitle);
            }
        }

        // 3. Rating
        // The class is often 'film-rating-average' or just 'rating-average' depending on A/B tests or page type
        var ratingNode = mainNode.SelectSingleNode("//div[contains(@class, 'film-rating-average')]");
        var ratingText = Clean(ratingNode?.InnerText);
        // CSFD shows "? %" for unrated movies — treat as no rating
        movie.Rating = !string.IsNullOrWhiteSpace(ratingText) && ratingText != "? %" ? ratingText : null;

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
                var parts = rawText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 0)
                {
                    movie.Origin = parts[0];
                    movie.Origins = ParseOrigins(parts[0]);
                }

                if (parts.Length > 1)
                    movie.Year = parts[1];

                if (parts.Length > 2)
                    movie.Duration = parts[^1]; // Duration is usually last
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

        // Resolve IMDb ID and rating in a single pass (no duplicate HTTP requests)
        if (resolveImdb && !movie.IsStudentFilm)
        {
            var (imdbId, imdbRating, imdbRatingCount) = await imdbResolver.ResolveImdb(doc, movie);
            movie.ImdbId = imdbId;
            movie.ImdbRating = imdbRating;
            movie.ImdbRatingCount = imdbRatingCount;
        }

        return movie;
    }

    /// <summary>
    /// Scrapes basic information about a cinema/venue page on CSFD.
    /// Best effort parsing since CSFD markup can vary.
    /// </summary>
    public async Task<Venue> ScrapeVenue(int venueId)
    {
        return await ScrapeVenue($"https://www.csfd.cz/kino/{venueId}");
    }

    public async Task<Venue> ScrapeVenue(string url)
    {
        Console.WriteLine($"Downloading venue: {url}");

        var client = httpClientFactory.CreateClient("Csfd");
        // Use SendAsync to capture the final request URI after redirects so we store the canonical URL
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await client.SendAsync(request);
        var finalUri = response.RequestMessage?.RequestUri?.ToString();
        var html = await response.Content.ReadAsStringAsync();

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var venue = new Venue();
        venue.DetailUrl = !string.IsNullOrWhiteSpace(finalUri) ? finalUri : url;

        // Name - prefer an H1 or H2
        var titleNode = doc.DocumentNode.SelectSingleNode("//h1") ?? doc.DocumentNode.SelectSingleNode("//h2") ?? doc.DocumentNode.SelectSingleNode("//title");
        var titleText = titleNode != null ? Clean(titleNode.InnerText) : null;
        // Normalize venue name: remove leading "Praha -" prefix when present
        if (!string.IsNullOrWhiteSpace(titleText))
        {
            titleText = Regex.Replace(titleText, "^(Praha)\\s*-\\s*", string.Empty, RegexOptions.IgnoreCase);
        }
        venue.Name = titleText;

        // Map URL - look for links to known map providers
        var mapAnchor = doc.DocumentNode.SelectSingleNode("//a[contains(@href,'google.com') or contains(@href,'mapy.cz') or contains(@href,'maps')]");
        if (mapAnchor != null)
        {
            var href = mapAnchor.GetAttributeValue("href", string.Empty);
            if (!string.IsNullOrWhiteSpace(href))
            {
                venue.MapUrl = href.StartsWith("//") ? "https:" + href : href;
            }
        }

        // Address - try a few heuristics
        // 1) explicit address tag
        var addressNode = doc.DocumentNode.SelectSingleNode("//address") ?? doc.DocumentNode.SelectSingleNode("//*[contains(@class,'address')]");
        if (addressNode != null)
        {
            venue.Address = Clean(addressNode.InnerText);
        }
        else
        {
            // 2) look for text nodes containing 'Adresa' (Czech for Address) or 'Address'
            var possible = doc.DocumentNode.SelectNodes("//*[text()]")?.Select(n => n.InnerText.Trim()).FirstOrDefault(t => t.IndexOf("Adresa", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("Address", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrWhiteSpace(possible))
            {
                // Try to extract after label
                var idx = possible.IndexOf(':');
                venue.Address = idx >= 0 && idx + 1 < possible.Length ? Clean(possible.Substring(idx + 1)) : Clean(possible);
            }
            else if (mapAnchor != null)
            {
                // Fallback: use parent surrounding text of the map link as address
                var parentText = mapAnchor.ParentNode?.InnerText;
                if (!string.IsNullOrWhiteSpace(parentText)) venue.Address = Clean(parentText);
            }
        }

        // Try to extract numeric ID from URL
        var idMatch = System.Text.RegularExpressions.Regex.Match(url, "(\\d+)");
        if (idMatch.Success && int.TryParse(idMatch.Groups[1].Value, out var id))
        {
            venue.Id = id;
        }

        return venue;
    }

    public async Task<TmdbMovie?> ResolveTmdb(CsfdMovie movie)
    {
        return await tmdbResolver.ResolveTmdbMovie(movie);
    }

    public async Task<TmdbMovie?> FetchTmdbById(int tmdbId)
    {
        return await tmdbResolver.GetMovieById(tmdbId);
    }

    public async Task<(double? Rating, int? RatingCount)> FetchImdbRating(string imdbId)
    {
        return await imdbResolver.FetchRating(imdbId);
    }

    public async Task<string?> FetchTrailerUrl(int tmdbId)
    {
        return await tmdbResolver.FetchTrailerUrl(tmdbId);
    }

    public async Task<List<CrewMember>> FetchCredits(int tmdbId)
    {
        return await tmdbResolver.FetchCredits(tmdbId);
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

    private static string? Clean(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var cleaned = HtmlEntity.DeEntitize(input).Trim();
        
        // Remove trailing text in parentheses (e.g., "(oficiální text distributora)")
        cleaned = Regex.Replace(cleaned, @"\s*\([^)]*\)\s*$", string.Empty).Trim();
        
        return cleaned;
    }

    private static bool IsStudentFilmNode(HtmlNode node)
    {
        var rawText = HtmlEntity.DeEntitize(node.InnerText ?? string.Empty).Trim();
        return ContainsStudentFilmText(rawText);
    }

    private static bool ContainsStudentFilmText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed.Contains("studentský", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains("studentsky", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ParseOrigins(string? originText)
    {
        if (string.IsNullOrWhiteSpace(originText))
            return new List<string>();

        return OriginSplitRegex
            .Split(originText)
            .Select(part => Regex.Replace(part, @"\s+", " ").Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
