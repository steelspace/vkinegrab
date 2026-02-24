using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using vkinegrab.Models;

namespace vkinegrab.Services.Tmdb;

internal sealed class TmdbResolver
{
    private readonly HttpClient client;
    private const string ApiBaseUrl = "https://api.themoviedb.org/3";
    private readonly string bearerToken;

    public TmdbResolver(HttpClient client, string bearerToken)
    {
        this.client = client;
        this.bearerToken = bearerToken;
        this.client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        this.client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<TmdbMovie?> ResolveTmdbMovie(CsfdMovie movie)
    {
        // Try IMDb ID first for precise result
        if (!string.IsNullOrWhiteSpace(movie.ImdbId))
        {
            var tmdbMovie = await FindByImdbId(movie.ImdbId);
            if (tmdbMovie != null)
            {
                tmdbMovie.TrailerUrl = await FetchTrailerUrl(tmdbMovie.Id);
                return tmdbMovie;
            }
        }

        // Fall back to title search
        var searchTitles = GetSearchTitles(movie);
        var normalizedTitles = BuildNormalizedTitleSet(movie);

        foreach (var title in searchTitles)
        {
            var tmdbMovie = await SearchTmdb(title, movie.Year, normalizedTitles);
            if (tmdbMovie != null)
            {
                tmdbMovie.TrailerUrl = await FetchTrailerUrl(tmdbMovie.Id);
                return tmdbMovie;
            }
        }

        return null;
    }

    private async Task<TmdbMovie?> FindByImdbId(string imdbId)
    {
        var findUrl = $"{ApiBaseUrl}/find/{imdbId}?external_source=imdb_id";

        string responseJson;
        try
        {
            responseJson = await client.GetStringAsync(findUrl);
        }
        catch
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("movie_results", out var movieResults) || 
                movieResults.ValueKind != JsonValueKind.Array || 
                movieResults.GetArrayLength() == 0)
            {
                return null;
            }

            var result = movieResults[0];
            return ParseTmdbMovie(result);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IEnumerable<string> GetSearchTitles(CsfdMovie movie)
    {
        var candidates = new List<string?>();

        if (!string.IsNullOrWhiteSpace(movie.OriginalTitle))
        {
            candidates.Add(movie.OriginalTitle);
        }

        var englishTitle = GetLocalizedTitle(movie, "USA", "United States", "Spojené státy", "Velká Británie", "United Kingdom", "UK", "Spojené království");
        if (!string.IsNullOrWhiteSpace(englishTitle))
        {
            candidates.Add(englishTitle);
        }

        if (!string.IsNullOrWhiteSpace(movie.Title))
        {
            candidates.Add(movie.Title);
        }

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

    private async Task<TmdbMovie?> SearchTmdb(string title, string? year, HashSet<string> normalizedTitles)
    {
        var yearDigits = ExtractYearDigits(year);
        var tmdbMovie = await ExecuteSearch(title, yearDigits, normalizedTitles);

        // If not found and we had a year, try +/- 1 year (common discrepancies)
        if (tmdbMovie == null && !string.IsNullOrEmpty(yearDigits) && int.TryParse(yearDigits, out var y))
        {
            tmdbMovie = await ExecuteSearch(title, (y + 1).ToString(), normalizedTitles);
            if (tmdbMovie == null)
            {
                tmdbMovie = await ExecuteSearch(title, (y - 1).ToString(), normalizedTitles);
            }
        }

        // Final fallback: search without year — only when CSFD has no year data.
        // If CSFD has a year, we already tried exact/±1; dropping the year entirely
        // would match any film sharing the title regardless of decade.
        if (tmdbMovie == null && string.IsNullOrEmpty(yearDigits))
        {
            tmdbMovie = await ExecuteSearch(title, null, normalizedTitles);
        }

        return tmdbMovie;
    }

    private async Task<TmdbMovie?> ExecuteSearch(string title, string? yearDigits, HashSet<string> normalizedTitles)
    {
        var queryParams = new List<string>
        {
            $"query={Uri.EscapeDataString(title)}",
            "include_adult=false",
            "language=en-US",
            "page=1"
        };

        if (!string.IsNullOrWhiteSpace(yearDigits))
        {
            queryParams.Add($"year={yearDigits}");
        }

        var searchUrl = $"{ApiBaseUrl}/search/movie?{string.Join("&", queryParams)}";

        string responseJson;
        try
        {
            responseJson = await client.GetStringAsync(searchUrl);
        }
        catch
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var result in results.EnumerateArray())
            {
                if (!result.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var resultTitle = result.TryGetProperty("title", out var t) ? t.GetString() : null;
                var resultOriginalTitle = result.TryGetProperty("original_title", out var ot) ? ot.GetString() : null;

                if (IsTitleMatch(normalizedTitles, resultTitle, resultOriginalTitle))
                {
                    return ParseTmdbMovie(result);
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private TmdbMovie? ParseTmdbMovie(JsonElement result)
    {
        if (!result.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var tmdbMovie = new TmdbMovie
        {
            Id = idElement.GetInt32()
        };

        if (result.TryGetProperty("title", out var titleElement))
        {
            tmdbMovie.Title = titleElement.GetString();
        }

        if (result.TryGetProperty("original_title", out var originalTitleElement))
        {
            tmdbMovie.OriginalTitle = originalTitleElement.GetString();
        }

        if (result.TryGetProperty("release_date", out var releaseDateElement))
        {
            tmdbMovie.ReleaseDate = releaseDateElement.GetString();
        }

        if (result.TryGetProperty("overview", out var overviewElement))
        {
            tmdbMovie.Overview = overviewElement.GetString();
        }

        if (result.TryGetProperty("poster_path", out var posterPathElement))
        {
            tmdbMovie.PosterPath = posterPathElement.GetString();
        }

        if (result.TryGetProperty("backdrop_path", out var backdropPathElement))
        {
            tmdbMovie.BackdropPath = backdropPathElement.GetString();
        }

        if (result.TryGetProperty("vote_average", out var voteAverageElement) && voteAverageElement.ValueKind == JsonValueKind.Number)
        {
            tmdbMovie.VoteAverage = voteAverageElement.GetDouble();
        }

        if (result.TryGetProperty("vote_count", out var voteCountElement) && voteCountElement.ValueKind == JsonValueKind.Number)
        {
            tmdbMovie.VoteCount = voteCountElement.GetInt32();
        }

        if (result.TryGetProperty("popularity", out var popularityElement) && popularityElement.ValueKind == JsonValueKind.Number)
        {
            tmdbMovie.Popularity = popularityElement.GetDouble();
        }

        if (result.TryGetProperty("genre_ids", out var genreIdsElement) && genreIdsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var genreId in genreIdsElement.EnumerateArray())
            {
                if (genreId.ValueKind == JsonValueKind.Number)
                {
                    tmdbMovie.GenreIds.Add(genreId.GetInt32());
                }
            }
        }

        if (result.TryGetProperty("original_language", out var originalLanguageElement))
        {
            tmdbMovie.OriginalLanguage = originalLanguageElement.GetString();
        }

        if (result.TryGetProperty("adult", out var adultElement) && (adultElement.ValueKind == JsonValueKind.True || adultElement.ValueKind == JsonValueKind.False))
        {
            tmdbMovie.Adult = adultElement.GetBoolean();
        }

        if (result.TryGetProperty("homepage", out var homepageElement))
        {
            tmdbMovie.Homepage = homepageElement.GetString();
        }

        return tmdbMovie;
    }

    private HashSet<string> BuildNormalizedTitleSet(CsfdMovie movie)
    {
        var candidates = new List<string?> { movie.Title, movie.OriginalTitle };
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

    private static bool IsTitleMatch(HashSet<string> normalizedTitles, string? resultTitle, string? resultOriginalTitle)
    {
        var normalizedResult = NormalizeTitle(resultTitle);
        var normalizedOriginal = NormalizeTitle(resultOriginalTitle);

        return (!string.IsNullOrEmpty(normalizedResult) && normalizedTitles.Contains(normalizedResult))
            || (!string.IsNullOrEmpty(normalizedOriginal) && normalizedTitles.Contains(normalizedOriginal));
    }

    internal static string NormalizeTitle(string? title)
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

    private string? ExtractYearDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(value, @"\b(\d{4})\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    public async Task<TmdbMovie?> GetMovieById(int id)
    {
        var url = $"{ApiBaseUrl}/movie/{id}?language=en-US";
        string responseJson;
        try
        {
            responseJson = await client.GetStringAsync(url);
        }
        catch
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            var movie = ParseTmdbMovie(root);
            if (movie != null)
            {
                movie.TrailerUrl = await FetchTrailerUrl(movie.Id);
            }
            return movie;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<string?> FetchTrailerUrl(int tmdbId)
    {
        var url = $"{ApiBaseUrl}/movie/{tmdbId}/videos?language=en-US";
        string responseJson;
        try
        {
            responseJson = await client.GetStringAsync(url);
        }
        catch
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            // Prefer official YouTube trailers, then teasers, then any YouTube video
            string? trailerKey = null;
            string? teaserKey = null;
            string? anyKey = null;

            foreach (var video in results.EnumerateArray())
            {
                var site = video.TryGetProperty("site", out var siteEl) ? siteEl.GetString() : null;
                if (!string.Equals(site, "YouTube", StringComparison.OrdinalIgnoreCase))
                    continue;

                var key = video.TryGetProperty("key", out var keyEl) ? keyEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var type = video.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                var official = video.TryGetProperty("official", out var officialEl) && officialEl.ValueKind == JsonValueKind.True;

                if (string.Equals(type, "Trailer", StringComparison.OrdinalIgnoreCase))
                {
                    if (official || trailerKey == null)
                        trailerKey = key;
                }
                else if (string.Equals(type, "Teaser", StringComparison.OrdinalIgnoreCase))
                {
                    teaserKey ??= key;
                }
                else
                {
                    anyKey ??= key;
                }
            }

            var bestKey = trailerKey ?? teaserKey ?? anyKey;
            return bestKey != null ? $"https://www.youtube.com/watch?v={bestKey}" : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
