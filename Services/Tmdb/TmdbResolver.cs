using System.Net.Http.Headers;
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
                return tmdbMovie;
            }
        }

        // Fall back to title search
        var searchTitles = GetSearchTitles(movie);

        foreach (var title in searchTitles)
        {
            var tmdbMovie = await SearchTmdb(title, movie.Year);
            if (tmdbMovie != null)
            {
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

    private async Task<TmdbMovie?> SearchTmdb(string title, string? year)
    {
        var queryParams = new List<string>
        {
            $"query={Uri.EscapeDataString(title)}",
            "include_adult=false",
            "language=en-US",
            "page=1"
        };

        if (!string.IsNullOrWhiteSpace(year))
        {
            var yearDigits = ExtractYearDigits(year);
            if (!string.IsNullOrEmpty(yearDigits))
            {
                queryParams.Add($"year={yearDigits}");
            }
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
                if (result.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number)
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

        return tmdbMovie;
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
}
