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

    public async Task<string?> ResolveImdbId(HtmlDocument csfdDoc, CsfdMovie movie)
    {
        var directLink = csfdDoc.DocumentNode.SelectSingleNode("//a[contains(@href, 'imdb.com/title/tt')]");
        if (directLink != null)
        {
            var href = directLink.GetAttributeValue("href", string.Empty);
            var match = Regex.Match(href, @"tt\d+");
            if (match.Success)
            {
                var imdbId = match.Value;
                if (await validator.Validate(imdbId, movie))
                {
                    return imdbId;
                }
            }
        }

        foreach (var candidateTitle in titleMatcher.GetSearchTitles(movie))
        {
            var imdbId = await SearchImdbForTitle(candidateTitle, movie);
            if (!string.IsNullOrEmpty(imdbId))
            {
                return imdbId;
            }
        }

        return null;
    }

    private async Task<string?> SearchImdbForTitle(string title, CsfdMovie movie)
    {
        Console.WriteLine($"  Searching IMDb for: '{title}'");
        return await TryImdbSearch(title, movie, null);
    }

    private async Task<string?> TryImdbSearch(string query, CsfdMovie movie, string? titleType)
    {
        var results = await searchService.Search(query, titleType);
        Console.WriteLine($"    Found {results.Count} results");
        foreach (var r in results.Take(3))
        {
            Console.WriteLine($"      - {r.Id}: '{r.Title}' ({r.Year}) RawText: '{r.RawText}'");
        }
        
        if (results.Count == 0)
        {
            return null;
        }

        // Extract title from query (remove year if present)
        var queryTitle = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                              .Where(part => !Regex.IsMatch(part, @"^\d{4}$"))
                              .Aggregate("", (acc, part) => acc + " " + part).Trim();

        var normalizedTargets = titleMatcher.BuildNormalizedTitleSet(movie, queryTitle);
        var prioritized = new List<ImdbSearchResult>();
        var secondary = new List<ImdbSearchResult>();

        foreach (var result in results)
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
            if (await validator.Validate(candidate.Id, movie))
            {
                return candidate.Id;
            }
        }

        foreach (var candidate in secondary)
        {
            if (await validator.Validate(candidate.Id, movie))
            {
                return candidate.Id;
            }
        }

        return null;
    }
}

