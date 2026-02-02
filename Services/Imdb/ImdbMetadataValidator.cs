using HtmlAgilityPack;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using vkinegrab.Models;
using vkinegrab.Services.Imdb.Models;

namespace vkinegrab.Services.Imdb;

internal sealed class ImdbMetadataValidator
{
    private readonly HttpClient client;
    private readonly ImdbTitleMatcher matcher;

    public ImdbMetadataValidator(HttpClient client, ImdbTitleMatcher matcher)
    {
        this.client = client;
        this.matcher = matcher;
    }

    public async Task<bool> Validate(string imdbId, CsfdMovie movie)
    {
        var hasYear = !string.IsNullOrWhiteSpace(movie.Year);
        var hasDirectors = movie.Directors != null && movie.Directors.Count > 0;

        if (!hasYear && !hasDirectors)
        {
            return true;
        }

        var metadata = await FetchTitleMetadata(imdbId);
        if (metadata == null)
        {
            Console.WriteLine($"      Validation: No metadata found for {imdbId}, accepting by default");
            return true;
        }

        var yearValid = IsYearValid(movie.Year, metadata.Year);
        var directorsValid = AreDirectorsValid(movie.Directors, metadata.Directors);

        Console.WriteLine($"      Validation for {imdbId}: Year={yearValid} (CSFD:{movie.Year} vs IMDb:{metadata.Year}), Directors={directorsValid}");

        if (hasYear && hasDirectors)
        {
            // Accept if either year or directors match (to handle cases where metadata has incorrect year but directors are correct)
            return yearValid || directorsValid;
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
            return Math.Abs(movieYearValue - imdbYearValue) <= 2;
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

        var normalizedImdb = new HashSet<string>(imdbDirectors.Select(matcher.NormalizePersonName).Where(n => !string.IsNullOrEmpty(n)), StringComparer.Ordinal);
        if (normalizedImdb.Count == 0)
        {
            return false;
        }

        foreach (var director in movieDirectors)
        {
            var normalizedDirector = matcher.NormalizePersonName(director);
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

    private async Task<ImdbTitleMetadata?> FetchTitleMetadata(string imdbId)
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
        Console.WriteLine($"  DEBUG: JSON-LD didn't provide year, trying HTML fallback...");
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (titleNode != null)
        {
            var titleText = WebUtility.HtmlDecode(titleNode.InnerText);
            Console.WriteLine($"  DEBUG: Page title: {titleText}");
            var yearMatch = Regex.Match(titleText, @"\((\d{4})\)");
            if (yearMatch.Success)
            {
                var year = yearMatch.Groups[1].Value;
                Console.WriteLine($"  DEBUG: Extracted year from title: {year}");
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
}
