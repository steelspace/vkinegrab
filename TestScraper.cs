using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

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
        Console.WriteLine("Fetching movie IDs from CSFD TV schedule (days 1-10)...");
        var movieIds = new HashSet<int>();
        
        // Fetch from 10 consecutive days
        for (int day = 1; day <= 10; day++)
        {
            var url = $"https://www.csfd.cz/televize/?day={day}";
            Console.WriteLine($"  Fetching day {day}: {url}");
            
            try
            {
                var html = await client.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error fetching day {day}: {ex.Message}");
            }
        }

        Console.WriteLine($"Found {movieIds.Count} unique movie IDs across all days");
        return movieIds.OrderBy(x => x).ToList();
    }

    public async Task<TestResult> TestMovie(int movieId)
    {
        var result = new TestResult { MovieId = movieId };
        
        try
        {
            Console.WriteLine($"\nTesting Movie ID: {movieId}");
            Console.WriteLine("----------------------------------------");
            
            var movie = await scraper.ScrapeMovie(movieId);
            var tmdbMovie = await scraper.ResolveTmdb(movie);

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

    public async Task RunTests(int maxMovies = 100)
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

    public async Task RunCinemaShowtimes(string period = "today", string? pageUrl = null, int maxMovies = 5)
    {
        Uri? requestUri = null;
        if (!string.IsNullOrWhiteSpace(pageUrl))
        {
            if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var absolute))
            {
                requestUri = absolute;
            }
            else
            {
                try
                {
                    requestUri = new Uri(new Uri("https://www.csfd.cz/"), pageUrl);
                }
                catch (UriFormatException)
                {
                    Console.WriteLine($"Ignoring invalid page URL '{pageUrl}'.");
                }
            }
        }

        Console.WriteLine($"Fetching cinema schedule for period '{period}'{(requestUri != null ? $" ({requestUri})" : string.Empty)}...");

        IReadOnlyList<CsfdCinema> cinemas;
        try
        {
            var service = new PerformancesService();
            cinemas = await service.GetPerformancesAsync(requestUri, period);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to download cinema schedule: {ex.Message}");
            return;
        }

        if (cinemas.Count == 0)
        {
            Console.WriteLine("No cinemas returned.");
            return;
        }

        var entries = cinemas
            .SelectMany(cinema => cinema.Performances.Select(performance => new { cinema, performance }))
            .Where(x => x.performance.MovieId > 0 && x.performance.Showtimes.Count > 0)
            .ToList();

        if (entries.Count == 0)
        {
            Console.WriteLine("No showtimes detected.");
            return;
        }

        var movieGroups = entries
            .GroupBy(x => x.performance.MovieId)
            .OrderByDescending(group => group.Sum(item => item.performance.Showtimes.Count))
            .Take(Math.Max(1, maxMovies))
            .ToList();

        var culture = CultureInfo.InvariantCulture;

        foreach (var group in movieGroups)
        {
            CsfdMovie? movie = null;
            try
            {
                movie = await scraper.ScrapeMovie(group.Key);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Movie {group.Key}: failed to download metadata ({ex.Message}).");
            }

            var movieTitle = movie?.Title ?? $"Film #{group.Key}";
            var movieYear = string.IsNullOrWhiteSpace(movie?.Year) ? string.Empty : $" ({movie!.Year})";

            var cinemaGroups = group
                .GroupBy(x => x.cinema.Id)
                .Select(g => new
                {
                    Cinema = g.First().cinema,
                    Performances = g.Select(item => item.performance).ToList()
                })
                .OrderBy(g => g.Cinema.City ?? string.Empty)
                .ThenBy(g => g.Cinema.Name ?? string.Empty);

            var theaterSummaries = new List<string>();
            foreach (var cinemaGroup in cinemaGroups)
            {
                var segments = new List<string>();
                foreach (var performance in cinemaGroup.Performances)
                {
                    var hallLabels = performance.Badges
                        .Where(b => b.Kind == CsfdBadgeKind.Hall)
                        .Select(b => string.IsNullOrWhiteSpace(b.Description) ? b.Code : b.Description)
                        .Where(label => !string.IsNullOrWhiteSpace(label))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var showtimes = performance.Showtimes
                        .OrderBy(s => s.StartAt)
                        .Select(s => s.StartAt.ToString("HH:mm", culture) + (s.TicketsAvailable ? "*" : string.Empty))
                        .Distinct()
                        .ToList();

                    if (showtimes.Count == 0)
                    {
                        continue;
                    }

                    var prefix = hallLabels.Count > 0 ? string.Join("/", hallLabels) + ": " : string.Empty;
                    segments.Add(prefix + string.Join(", ", showtimes));
                }

                if (segments.Count == 0)
                {
                    continue;
                }

                var theaterLabel = $"{cinemaGroup.Cinema.City ?? "?"} - {cinemaGroup.Cinema.Name ?? "Unknown"}";
                theaterSummaries.Add($"{theaterLabel}: {string.Join(" | ", segments)}");
            }

            if (theaterSummaries.Count == 0)
            {
                continue;
            }

            Console.WriteLine($"Movie: {movieTitle}{movieYear}");
            Console.WriteLine($"  Theaters: {string.Join("; ", theaterSummaries)}");
            Console.WriteLine();
        }

        Console.WriteLine("(*) indicates an active ticket link.");
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

        var tester = new TestScraper(tmdbBearerToken);
        if (args.Length > 0 && args[0].Equals("showtimes", StringComparison.OrdinalIgnoreCase))
        {
            var period = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : "today";
            var pageUrl = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]) ? args[2] : null;
            var limit = args.Length > 3 && int.TryParse(args[3], out var parsedLimit) && parsedLimit > 0 ? parsedLimit : 5;
            await tester.RunCinemaShowtimes(period, pageUrl, limit);
            return;
        }

        var maxMovies = 100;
        if (args.Length > 0 && int.TryParse(args[0], out var argMax) && argMax > 0)
        {
            maxMovies = argMax;
        }

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
