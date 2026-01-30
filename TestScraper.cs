using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using vkinegrab.Services;

namespace vkinegrab;

public class TestScraper
{
    private readonly CsfdScraper scraper;
    private readonly HttpClient client;

    public TestScraper(string tmdbBearerToken)
    {
        scraper = new CsfdScraper(tmdbBearerToken);
        client = new HttpClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public async Task<List<int>> GetMovieIdsFromTvSchedule()
    {
        Console.WriteLine("Fetching movie IDs from CSFD TV schedule...");
        var url = "https://www.csfd.cz/televize/";
        var html = await client.GetStringAsync(url);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var movieIds = new HashSet<int>();
        
        // Look for links to /film/{id}
        var filmLinks = doc.DocumentNode.SelectNodes("//a[contains(@href, '/film/')]");
        if (filmLinks != null)
        {
            foreach (var link in filmLinks)
            {
                var href = link.GetAttributeValue("href", "");
                var match = Regex.Match(href, @"/film/(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
                {
                    movieIds.Add(id);
                }
            }
        }

        Console.WriteLine($"Found {movieIds.Count} unique movie IDs");
        return movieIds.OrderBy(x => x).ToList();
    }

    public async Task<TestResult> TestMovie(int movieId)
    {
        var result = new TestResult { MovieId = movieId };
        
        try
        {
            Console.WriteLine($"\nTesting Movie ID: {movieId}");
            Console.WriteLine("----------------------------------------");
            
            var movie = await scraper.ScrapeMovieAsync(movieId);
            var tmdbMovie = await scraper.ResolveTmdbAsync(movie);

            // Validate CSFD properties
            result.HasTitle = !string.IsNullOrWhiteSpace(movie.Title);
            result.HasYear = !string.IsNullOrWhiteSpace(movie.Year);
            result.HasOrigin = !string.IsNullOrWhiteSpace(movie.Origin);
            result.HasGenres = movie.Genres != null && movie.Genres.Any();
            result.HasDirectors = movie.Directors != null && movie.Directors.Any();
            result.HasCast = movie.Cast != null && movie.Cast.Any();
            result.HasDescription = !string.IsNullOrWhiteSpace(movie.Description);
            result.HasPoster = !string.IsNullOrWhiteSpace(movie.PosterUrl);
            result.HasLocalizedTitles = movie.LocalizedTitles != null && movie.LocalizedTitles.Any();

            // Validate IMDb
            result.HasImdbId = !string.IsNullOrWhiteSpace(movie.ImdbId);

            // Validate TMDB
            result.HasTmdbId = tmdbMovie != null;
            if (tmdbMovie != null)
            {
                result.HasTmdbTitle = !string.IsNullOrWhiteSpace(tmdbMovie.Title);
                result.HasTmdbReleaseDate = !string.IsNullOrWhiteSpace(tmdbMovie.ReleaseDate);
                result.HasTmdbOverview = !string.IsNullOrWhiteSpace(tmdbMovie.Overview);
            }

            result.Success = true;
            result.CsfdTitle = movie.Title;
            result.ImdbId = movie.ImdbId;
            result.TmdbId = tmdbMovie?.Id;

            // Print summary
            Console.WriteLine($"CSFD Title: {result.CsfdTitle ?? "MISSING"}");
            Console.WriteLine($"IMDb ID: {result.ImdbId ?? "MISSING"}");
            Console.WriteLine($"TMDB ID: {result.TmdbId?.ToString() ?? "MISSING"}");
            Console.WriteLine($"Status: {(result.IsComplete ? "✓ COMPLETE" : "⚠ INCOMPLETE")}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Console.WriteLine($"ERROR: {ex.Message}");
        }

        return result;
    }

    public async Task RunTests(int maxMovies = 10)
    {
        var movieIds = await GetMovieIdsFromTvSchedule();
        var testMovies = movieIds.Take(maxMovies).ToList();

        Console.WriteLine($"\n\nRunning tests on {testMovies.Count} movies...\n");
        Console.WriteLine("========================================");

        var results = new List<TestResult>();
        foreach (var id in testMovies)
        {
            var result = await TestMovie(id);
            results.Add(result);
            await Task.Delay(500); // Be nice to the server
        }

        Console.WriteLine("\n\n========================================");
        Console.WriteLine("TEST SUMMARY");
        Console.WriteLine("========================================");
        Console.WriteLine($"Total movies tested: {results.Count}");
        Console.WriteLine($"Successful: {results.Count(r => r.Success)}");
        Console.WriteLine($"Failed: {results.Count(r => !r.Success)}");
        Console.WriteLine($"Complete (all data): {results.Count(r => r.IsComplete)}");
        Console.WriteLine($"Incomplete: {results.Count(r => !r.IsComplete)}");
        Console.WriteLine();
        Console.WriteLine($"CSFD Title: {results.Count(r => r.HasTitle)}/{results.Count}");
        Console.WriteLine($"CSFD Year: {results.Count(r => r.HasYear)}/{results.Count}");
        Console.WriteLine($"CSFD Genres: {results.Count(r => r.HasGenres)}/{results.Count}");
        Console.WriteLine($"CSFD Directors: {results.Count(r => r.HasDirectors)}/{results.Count}");
        Console.WriteLine($"CSFD Cast: {results.Count(r => r.HasCast)}/{results.Count}");
        Console.WriteLine($"CSFD Description: {results.Count(r => r.HasDescription)}/{results.Count}");
        Console.WriteLine($"IMDb ID: {results.Count(r => r.HasImdbId)}/{results.Count}");
        Console.WriteLine($"TMDB ID: {results.Count(r => r.HasTmdbId)}/{results.Count}");
        Console.WriteLine($"TMDB Title: {results.Count(r => r.HasTmdbTitle)}/{results.Count}");

        if (results.Any(r => !r.Success))
        {
            Console.WriteLine("\n\nFAILED MOVIES:");
            foreach (var failed in results.Where(r => !r.Success))
            {
                Console.WriteLine($"  Movie ID {failed.MovieId}: {failed.ErrorMessage}");
            }
        }

        if (results.Any(r => !r.IsComplete && r.Success))
        {
            Console.WriteLine("\n\nINCOMPLETE MOVIES:");
            foreach (var incomplete in results.Where(r => !r.IsComplete && r.Success))
            {
                Console.WriteLine($"  Movie ID {incomplete.MovieId} ({incomplete.CsfdTitle ?? "No title"}):");
                if (!incomplete.HasTitle) Console.WriteLine("    - Missing CSFD Title");
                if (!incomplete.HasYear) Console.WriteLine("    - Missing Year");
                if (!incomplete.HasGenres) Console.WriteLine("    - Missing Genres");
                if (!incomplete.HasDirectors) Console.WriteLine("    - Missing Directors");
                if (!incomplete.HasCast) Console.WriteLine("    - Missing Cast");
                if (!incomplete.HasDescription) Console.WriteLine("    - Missing Description");
                if (!incomplete.HasImdbId) Console.WriteLine("    - Missing IMDb ID");
                if (!incomplete.HasTmdbId) Console.WriteLine("    - Missing TMDB ID");
                if (!incomplete.HasTmdbTitle) Console.WriteLine("    - Missing TMDB Title");
            }
        }
    }

    public static async Task Run(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();

        var tmdbBearerToken = configuration["Tmdb:BearerToken"];
        
        if (string.IsNullOrWhiteSpace(tmdbBearerToken))
        {
            Console.WriteLine("ERROR: TMDB Bearer Token not found!");
            Console.WriteLine("Please set it using:");
            Console.WriteLine("  dotnet user-secrets set \"Tmdb:BearerToken\" \"your-bearer-token-here\"");
            return;
        }

        var maxMovies = 10;
        if (args.Length > 0 && int.TryParse(args[0], out var argMax) && argMax > 0)
        {
            maxMovies = argMax;
        }

        var tester = new TestScraper(tmdbBearerToken);
        await tester.RunTests(maxMovies);
    }
}

public class TestResult
{
    public int MovieId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    
    // CSFD properties
    public bool HasTitle { get; set; }
    public bool HasYear { get; set; }
    public bool HasOrigin { get; set; }
    public bool HasGenres { get; set; }
    public bool HasDirectors { get; set; }
    public bool HasCast { get; set; }
    public bool HasDescription { get; set; }
    public bool HasPoster { get; set; }
    public bool HasLocalizedTitles { get; set; }
    
    // IMDb properties
    public bool HasImdbId { get; set; }
    
    // TMDB properties
    public bool HasTmdbId { get; set; }
    public bool HasTmdbTitle { get; set; }
    public bool HasTmdbReleaseDate { get; set; }
    public bool HasTmdbOverview { get; set; }
    
    // Actual values for reporting
    public string? CsfdTitle { get; set; }
    public string? ImdbId { get; set; }
    public int? TmdbId { get; set; }
    
    public bool IsComplete => 
        Success && 
        HasTitle && 
        HasYear && 
        HasGenres && 
        HasDirectors && 
        HasCast && 
        HasDescription && 
        HasImdbId && 
        HasTmdbId && 
        HasTmdbTitle;
}
