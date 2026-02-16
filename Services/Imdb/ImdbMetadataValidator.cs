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
        var (valid, _) = await ValidateAndGetMetadata(imdbId, movie);
        return valid;
    }

    /// <summary>
    /// IMDb JSON-LD @type values that are compatible with cinema movies.
    /// </summary>
    private static readonly HashSet<string> AcceptableTitleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Movie",
        "TVMovie",
        "TVSpecial",
        "TVMiniSeries",
        "Short",
    };

    /// <summary>
    /// IMDb JSON-LD @type values that are definitely NOT movies shown in cinemas.
    /// </summary>
    private static readonly HashSet<string> RejectedTitleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PodcastSeries",
        "PodcastEpisode",
        "TVSeries",
        "TVEpisode",
        "VideoGame",
        "MusicVideoObject",
    };

    public async Task<(bool IsValid, ImdbTitleMetadata? Metadata)> ValidateAndGetMetadata(string imdbId, CsfdMovie movie)
    {
        var hasYear = !string.IsNullOrWhiteSpace(movie.Year);
        var hasDirectors = movie.Directors != null && movie.Directors.Count > 0;

        // Always fetch metadata so we get the rating, even if there's nothing to validate
        var metadata = await FetchTitleMetadata(imdbId);

        // Validate title type — reject podcasts, TV series, video games, etc.
        if (metadata != null && !IsTitleTypeAcceptable(metadata.TitleType))
        {
            Console.WriteLine($"      Validation: Rejecting {imdbId} — incompatible title type '{metadata.TitleType}'");
            return (false, null);
        }

        if (!hasYear && !hasDirectors)
        {
            return (true, metadata);
        }

        if (metadata == null)
        {
            Console.WriteLine($"      Validation: No metadata found for {imdbId}, accepting by default");
            return (true, null);
        }

        var yearValid = IsYearValid(movie.Year, metadata.Year);
        var directorsValid = AreDirectorsValid(movie.Directors, metadata.Directors);

        Console.WriteLine($"      Validation for {imdbId}: TitleType='{metadata.TitleType}', Year={yearValid} (CSFD:{movie.Year} vs IMDb:{metadata.Year}), Directors={directorsValid}");

        if (hasYear && hasDirectors)
        {
            if (yearValid && !directorsValid && metadata.Directors.Count == 0)
            {
                Console.WriteLine($"      Accepting match: Year valid but no IMDb director data (likely HTML fallback)");
                return (true, metadata);
            }
            return (yearValid && directorsValid, yearValid && directorsValid ? metadata : null);
        }

        if (hasYear)
        {
            return (yearValid, yearValid ? metadata : null);
        }

        return (directorsValid, directorsValid ? metadata : null);
    }

    /// <summary>
    /// Checks whether the given IMDb title type is compatible with a cinema movie.
    /// Returns true if the type is null/unknown (permissive for missing data),
    /// true if it's in the acceptable set, and false if it's in the rejected set.
    /// For unknown types not in either set, returns true (permissive).
    /// </summary>
    internal static bool IsTitleTypeAcceptable(string? titleType)
    {
        if (string.IsNullOrWhiteSpace(titleType))
        {
            return true;
        }

        if (RejectedTitleTypes.Contains(titleType))
        {
            return false;
        }

        // Acceptable or unknown type — allow it
        return true;
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

        // Track rating from JSON-LD even if year extraction fails
        double? ratingFromJsonLd = null;
        int? ratingCountFromJsonLd = null;

        foreach (var scriptNode in scriptNodes)
        {
            // Parse raw JSON first (HtmlDecode can corrupt JSON when text fields contain special chars)
            var rawJson = scriptNode.InnerText;
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                continue;
            }

            try
            {
                using var jsonDoc = JsonDocument.Parse(rawJson);
                if (TryCreateMetadata(jsonDoc.RootElement, out var metadata))
                {
                    return metadata;
                }

                // Even if year is missing (TryCreateMetadata returned false),
                // we may have extracted rating from the JSON-LD. Capture it for
                // the HTML title fallback below.
                if (TryExtractRatingOnly(jsonDoc.RootElement, out var fallbackRating, out var fallbackCount))
                {
                    ratingFromJsonLd = fallbackRating;
                    ratingCountFromJsonLd = fallbackCount;
                }
            }
            catch (JsonException)
            {
                continue;
            }
        }

        // Fallback: Try to extract year and type from HTML title tag (e.g., "Title (1944) - IMDb")
        Console.WriteLine($"  DEBUG: JSON-LD didn't provide year, trying HTML fallback...");
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        string? htmlFallbackType = null;
        if (titleNode != null)
        {
            var titleText = WebUtility.HtmlDecode(titleNode.InnerText);
            Console.WriteLine($"  DEBUG: Page title: {titleText}");

            // Try to detect content type from the page title.
            // IMDB titles often include type indicators like "(TV Series 2020–)", "(Podcast Series)", etc.
            htmlFallbackType = ExtractTypeFromPageTitle(titleText);

            var yearMatch = Regex.Match(titleText, @"\((\d{4})\)");
            if (yearMatch.Success)
            {
                var year = yearMatch.Groups[1].Value;
                Console.WriteLine($"  DEBUG: Extracted year from title: {year}, type hint: {htmlFallbackType ?? "(none)"}");
                // Preserve any rating we found in JSON-LD even though year had to come from HTML
                return new ImdbTitleMetadata(year, new List<string>(), ratingFromJsonLd, ratingCountFromJsonLd, htmlFallbackType);
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
                // Extract metadata for any recognized content type so we can validate the type later.
                // Previously we only accepted "Movie" here, which caused non-movie types (podcasts, etc.)
                // to fall through to the HTML fallback path that has no type checking.
                if (!string.IsNullOrWhiteSpace(type))
                {
                    var year = ExtractYearFromElement(element);
                    var directors = ExtractDirectorsFromElement(element);
                    var (rating, ratingCount) = ExtractRatingFromElement(element);

                    // If JSON-LD doesn't have year, return false so HTML fallback can be used
                    if (year == null)
                    {
                        metadata = default!;
                        return false;
                    }
                    
                    metadata = new ImdbTitleMetadata(year, directors, rating, ratingCount, type);
                    return true;
                }
            }
        }

        metadata = default!;
        return false;
    }

    private static (double? Rating, int? RatingCount) ExtractRatingFromElement(JsonElement element)
    {
        if (!element.TryGetProperty("aggregateRating", out var aggregateRating) || aggregateRating.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        double? ratingValue = null;
        int? ratingCount = null;

        if (aggregateRating.TryGetProperty("ratingValue", out var ratingElement))
        {
            if (ratingElement.ValueKind == JsonValueKind.Number)
            {
                ratingValue = ratingElement.GetDouble();
            }
            else if (ratingElement.ValueKind == JsonValueKind.String && double.TryParse(ratingElement.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                ratingValue = parsed;
            }
        }

        if (aggregateRating.TryGetProperty("ratingCount", out var countElement))
        {
            if (countElement.ValueKind == JsonValueKind.Number)
            {
                ratingCount = countElement.GetInt32();
            }
            else if (countElement.ValueKind == JsonValueKind.String && int.TryParse(countElement.GetString(), out var parsedCount))
            {
                ratingCount = parsedCount;
            }
        }

        return (ratingValue, ratingCount);
    }

    private static bool TryExtractRatingOnly(JsonElement element, out double? rating, out int? ratingCount)
    {
        rating = null;
        ratingCount = null;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Handle @graph arrays
        if (element.TryGetProperty("@graph", out var graphElement) && graphElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in graphElement.EnumerateArray())
            {
                if (TryExtractRatingOnly(node, out rating, out ratingCount))
                {
                    return true;
                }
            }
        }

        var (r, c) = ExtractRatingFromElement(element);
        if (r.HasValue)
        {
            rating = r;
            ratingCount = c;
            return true;
        }

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

    /// <summary>
    /// Extracts a title type hint from the IMDB page &lt;title&gt; tag.
    /// E.g. "Kremlin Wizard (Podcast Series 2024– ) - IMDb" → "PodcastSeries"
    /// </summary>
    private static string? ExtractTypeFromPageTitle(string? titleText)
    {
        if (string.IsNullOrWhiteSpace(titleText))
        {
            return null;
        }

        // IMDB page titles for non-movie content include type in parentheses, e.g.:
        // "Title (TV Series 2020– )" or "Title (Podcast Series 2024– )"
        var typePatterns = new (string Pattern, string TypeName)[]
        {
            (@"\(Podcast Series\b", "PodcastSeries"),
            (@"\(Podcast Episode\b", "PodcastEpisode"),
            (@"\(TV Series\b", "TVSeries"),
            (@"\(TV Episode\b", "TVEpisode"),
            (@"\(TV Mini Series\b", "TVMiniSeries"),
            (@"\(TV Movie\b", "TVMovie"),
            (@"\(TV Special\b", "TVSpecial"),
            (@"\(TV Short\b", "TVShort"),
            (@"\(Video Game\b", "VideoGame"),
            (@"\(Video\b", "Video"),
            (@"\(Short\b", "Short"),
            (@"\(Music Video\b", "MusicVideoObject"),
        };

        foreach (var (pattern, typeName) in typePatterns)
        {
            if (Regex.IsMatch(titleText, pattern, RegexOptions.IgnoreCase))
            {
                return typeName;
            }
        }

        return null;
    }
}
