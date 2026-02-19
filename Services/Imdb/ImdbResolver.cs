using HtmlAgilityPack;
using System.Text.RegularExpressions;
using vkinegrab.Models;
using vkinegrab.Services.Imdb.Models;

namespace vkinegrab.Services.Imdb;

internal sealed class ImdbResolver
{
    private readonly ImdbSearchService searchService;
    private readonly ImdbTitleMatcher titleMatcher;
    private readonly ImdbMetadataValidator validator;

    public ImdbResolver(HttpClient client)
    {
        searchService = new ImdbSearchService(client);
        titleMatcher = new ImdbTitleMatcher();
        validator = new ImdbMetadataValidator(client, titleMatcher);
    }

    /// <summary>
    /// Resolves the IMDb ID and rating for a movie.
    /// Returns (imdbId, rating, ratingCount).
    /// </summary>
    public async Task<(string? ImdbId, double? Rating, int? RatingCount)> ResolveImdb(HtmlDocument csfdDoc, CsfdMovie movie)
    {
        var directLink = csfdDoc.DocumentNode.SelectSingleNode("//a[contains(@href, 'imdb.com/title/tt')]");
        if (directLink != null)
        {
            var href = directLink.GetAttributeValue("href", string.Empty);
            var match = Regex.Match(href, @"tt\d+");
            if (match.Success)
            {
                var imdbId = match.Value;
                var (valid, metadata) = await validator.ValidateAndGetMetadata(imdbId, movie);
                if (valid)
                {
                    return (imdbId, metadata?.Rating, metadata?.RatingCount);
                }
            }
        }

        foreach (var candidateTitle in titleMatcher.GetSearchTitles(movie))
        {
            var (imdbId, rating, ratingCount) = await SearchImdbForTitle(candidateTitle, movie);
            if (!string.IsNullOrEmpty(imdbId))
            {
                return (imdbId, rating, ratingCount);
            }
        }

        return (null, null, null);
    }

    /// <summary>
    /// Fetches fresh rating data for a known IMDb ID without performing any search.
    /// </summary>
    public async Task<(double? Rating, int? RatingCount)> FetchRating(string imdbId)
    {
        var (_, metadata) = await validator.ValidateAndGetMetadata(imdbId, new CsfdMovie());
        return (metadata?.Rating, metadata?.RatingCount);
    }

    /// <summary>
    /// Legacy method for backward compatibility.
    /// </summary>
    public async Task<string?> ResolveImdbId(HtmlDocument csfdDoc, CsfdMovie movie)
    {
        var (imdbId, _, _) = await ResolveImdb(csfdDoc, movie);
        return imdbId;
    }

    private async Task<(string? ImdbId, double? Rating, int? RatingCount)> SearchImdbForTitle(string title, CsfdMovie movie)
    {
        Console.WriteLine($"  Searching IMDb for: '{title}'");
        return await TryImdbSearch(title, movie, null);
    }

    private async Task<(string? ImdbId, double? Rating, int? RatingCount)> TryImdbSearch(string query, CsfdMovie movie, string? titleType)
    {
        var results = await searchService.Search(query, titleType);
        Console.WriteLine($"    Found {results.Count} results");
        foreach (var r in results.Take(3))
        {
            Console.WriteLine($"      - {r.Id}: '{r.Title}' ({r.Year}) Type: '{r.TitleType}' RawText: '{r.RawText}'");
        }
        
        if (results.Count == 0)
        {
            return (null, null, null);
        }

        // Pre-filter: skip results that are clearly not movies (podcasts, TV series, video games, etc.)
        var filteredResults = results.Where(r => ImdbMetadataValidator.IsTitleTypeAcceptable(r.TitleType)).ToList();
        if (filteredResults.Count < results.Count)
        {
            Console.WriteLine($"    Filtered out {results.Count - filteredResults.Count} non-movie results (podcasts, TV series, etc.)");
        }

        // Extract title from query (remove year if present)
        var queryTitle = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                              .Where(part => !Regex.IsMatch(part, @"^\d{4}$"))
                              .Aggregate("", (acc, part) => acc + " " + part).Trim();

        var normalizedTargets = titleMatcher.BuildNormalizedTitleSet(movie, queryTitle);
        var prioritized = new List<ImdbSearchResult>();
        var secondary = new List<ImdbSearchResult>();

        foreach (var result in filteredResults)
        {
            var normalizedTitle = titleMatcher.NormalizeTitle(result.Title);
            var titleMatches = normalizedTargets.Count > 0 && normalizedTargets.Contains(normalizedTitle);
            var yearMatches = titleMatcher.TitlesShareYear(movie.Year, result);

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
            var (valid, metadata) = await validator.ValidateAndGetMetadata(candidate.Id, movie, candidate.Year);
            if (valid)
            {
                return (candidate.Id, metadata?.Rating, metadata?.RatingCount);
            }
        }

        foreach (var candidate in secondary)
        {
            var (valid, metadata) = await validator.ValidateAndGetMetadata(candidate.Id, movie, candidate.Year);
            if (valid)
            {
                return (candidate.Id, metadata?.Rating, metadata?.RatingCount);
            }
        }

        return (null, null, null);
    }
}

