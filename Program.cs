using System.Globalization;
using Microsoft.Extensions.Configuration;
using vkinegrab.Models;
using vkinegrab.Services;
using vkinegrab.Services.Csfd;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets("vkinegrab-tmdb-secrets")
    .Build();

var tmdbBearerToken = configuration["Tmdb:BearerToken"];

if (string.IsNullOrWhiteSpace(tmdbBearerToken))
{
    Console.WriteLine("ERROR: TMDB Bearer Token not found!");
    Console.WriteLine("Please set it using:");
    Console.WriteLine("  dotnet user-secrets set \"Tmdb:BearerToken\" \"your-bearer-token-here\"");
    return;
}

// Initialize MongoDB connection string
var mongoConnectionString = configuration["MongoDB:ConnectionString"];

if (string.IsNullOrWhiteSpace(mongoConnectionString))
{
    Console.WriteLine("ERROR: MongoDB Connection String not found!");
    Console.WriteLine("Please set it using:");
    Console.WriteLine("  dotnet user-secrets set \"MongoDB:ConnectionString\" \"your-connection-string\"");
    return;
}

var databaseService = new DatabaseService(mongoConnectionString);

// Test MongoDB connection
try
{
    var isConnected = await databaseService.TestConnection();
    if (isConnected)
    {
        Console.WriteLine("✓ MongoDB connection successful");
    }
    else
    {
        Console.WriteLine("✗ MongoDB connection failed");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✗ MongoDB connection error: {ex.Message}");
}

// Check if running tests
if (args.Length > 0 && args[0].Equals("test", StringComparison.OrdinalIgnoreCase))
{
    var testArgs = args.Skip(1).ToArray();
    await vkinegrab.TestScraper.Run(testArgs);
    return;
}

if (args.Length > 0 && args[0].Equals("cinemas", StringComparison.OrdinalIgnoreCase))
{
    await PrintCinemaSchedule(args);
    return;
}

if (args.Length > 0 && args[0].Equals("showtimes", StringComparison.OrdinalIgnoreCase))
{
    var tester = new vkinegrab.TestScraper(tmdbBearerToken);
    var period = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : "today";
    var pageUrl = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]) ? args[2] : null;
    var maxMovies = args.Length > 3 && int.TryParse(args[3], out var parsedMax) && parsedMax > 0 ? parsedMax : 5;
    await tester.RunCinemaShowtimes(period, pageUrl, maxMovies);
    return;
}

if (args.Length > 0 && args[0].Equals("store-schedules", StringComparison.OrdinalIgnoreCase))
{
    var remainingArgs = args.Skip(1).ToArray();
    var period = remainingArgs.Length > 0 && !string.IsNullOrWhiteSpace(remainingArgs[0]) ? remainingArgs[0] : "all";

    Uri? pageUri = null;
    if (remainingArgs.Length > 1 && !string.IsNullOrWhiteSpace(remainingArgs[1]))
    {
        // Only accept absolute http(s) URIs. On Unix a leading slash can produce a file:// URI
        // which HttpClient doesn't support, so treat other inputs as relative CSFD paths.
        if (Uri.TryCreate(remainingArgs[1], UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            pageUri = absolute;
        }
        else if (Uri.TryCreate(remainingArgs[1], UriKind.Relative, out var relative))
        {
            pageUri = new Uri(new Uri("https://www.csfd.cz/"), relative);
        }
        else
        {
            // Fallback: construct relative URI against the CSFD base
            pageUri = new Uri(new Uri("https://www.csfd.cz/"), remainingArgs[1]);
        }
    }

    var service = new PerformancesService();
    IReadOnlyList<Schedule> schedules;
    try
    {
        schedules = await service.GetSchedules(pageUri, period);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to download schedules: {ex.Message}");
        return;
    }

    if (schedules.Count == 0)
    {
        Console.WriteLine("No schedules to store.");
        return;
    }

    Console.WriteLine($"Storing {schedules.Count} schedules to MongoDB (collection 'schedule')...");
    var success = 0;
    var failed = 0;

    foreach (var s in schedules)
    {
        try
        {
            await databaseService.StoreSchedule(s);
            success++;
        }
        catch (Exception ex)
        {
            failed++;
                // Print full exception (including inner exceptions and stack trace) to aid debugging
                Console.WriteLine($"  Failed to store schedule for movie {s.MovieId} on {s.Date:yyyy-MM-dd}: {ex}");
            }
        }

        Console.WriteLine($"Done. Stored: {success}. Failed: {failed}.");
        return;
    }

    var scraper = new CsfdScraper(tmdbBearerToken);
    var movieId = 1580037;

    if (args.Length > 0)
    {
        if (!int.TryParse(args[0], out movieId) || movieId <= 0)
        {
            Console.WriteLine("Please provide a valid numeric CSFD movie ID as the first argument.");
            return;
        }
    }

    try
    {
        var movie = await scraper.ScrapeMovie(movieId);
        var tmdbMovie = await scraper.ResolveTmdb(movie);

        // Merge the movies
        var mergedMovie = movie.Merge(tmdbMovie);
        
        // Store in database
        try
        {
            await databaseService.StoreMovie(mergedMovie);
            Console.WriteLine("✓ Movie stored in MongoDB");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Failed to store movie in database: {ex.Message}");
        }

    Console.WriteLine("--------------------------------------------------");
    Console.WriteLine($"CSFD SOURCE ID: {movieId}");
    Console.WriteLine($"ID: {movie.Id}");
    Console.WriteLine($"TITLE: {movie.Title}");
    Console.WriteLine($"YEAR: {movie.Year}");
    Console.WriteLine($"ORIGIN: {movie.Origin}");
    Console.WriteLine($"DURATION: {movie.Duration}");
    Console.WriteLine($"RATING: {movie.Rating}");
    Console.WriteLine($"GENRES: {string.Join(", ", movie.Genres)}");
    Console.WriteLine($"DIRECTORS: {string.Join(", ", movie.Directors)}");
    Console.WriteLine($"CAST (First 10): {string.Join(", ", movie.Cast.Take(10))}");
    Console.WriteLine($"POSTER: {movie.PosterUrl}");
    Console.WriteLine($"IMDB: {movie.ImdbUrl ?? "Not found"}");

    if (tmdbMovie != null)
    {
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine("TMDB INFO:");
        Console.WriteLine($"  ID: {tmdbMovie.Id}");
        Console.WriteLine($"  URL: {tmdbMovie.Url}");
        Console.WriteLine($"  TITLE: {tmdbMovie.Title}");
        Console.WriteLine($"  ORIGINAL TITLE: {tmdbMovie.OriginalTitle}");
        Console.WriteLine($"  RELEASE DATE: {tmdbMovie.ReleaseDate}");
        Console.WriteLine($"  RATING: {tmdbMovie.VoteAverage:F1}/10 ({tmdbMovie.VoteCount} votes)");
        Console.WriteLine($"  POPULARITY: {tmdbMovie.Popularity:F1}");
        Console.WriteLine($"  LANGUAGE: {tmdbMovie.OriginalLanguage}");
        Console.WriteLine($"  ADULT: {tmdbMovie.Adult}");
        Console.WriteLine($"  GENRE IDS: {string.Join(", ", tmdbMovie.GenreIds)}");
        Console.WriteLine($"  POSTER: {tmdbMovie.FullPosterUrl ?? "N/A"}");
        Console.WriteLine($"  BACKDROP: {tmdbMovie.FullBackdropUrl ?? "N/A"}");
        if (!string.IsNullOrWhiteSpace(tmdbMovie.Overview))
        {
            Console.WriteLine($"  OVERVIEW: {tmdbMovie.Overview}");
        }
    }

    Console.WriteLine("--------------------------------------------------");
    Console.WriteLine("TITLES BY COUNTRY:");
    foreach (var kvp in movie.LocalizedTitles)
    {
        Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
    }
    Console.WriteLine("--------------------------------------------------");
    Console.WriteLine($"DESC: {movie.Description}");
    Console.WriteLine("--------------------------------------------------");
}
catch (Exception ex)
{
    Console.WriteLine($"Error occurred: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

static async Task PrintCinemaSchedule(string[] args)
{
    var remainingArgs = args.Skip(1).ToArray();
    var period = remainingArgs.Length > 0 && !string.IsNullOrWhiteSpace(remainingArgs[0])
        ? remainingArgs[0]
        : "today";

    Uri? pageUri = null;
    if (remainingArgs.Length > 1 && !string.IsNullOrWhiteSpace(remainingArgs[1]))
    {
        // Only accept absolute http(s) URIs. On Unix a leading slash can produce a file:// URI
        // which HttpClient doesn't support, so treat other inputs as relative CSFD paths.
        if (Uri.TryCreate(remainingArgs[1], UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            pageUri = absolute;
        }
        else if (Uri.TryCreate(remainingArgs[1], UriKind.Relative, out var relative))
        {
            pageUri = new Uri(new Uri("https://www.csfd.cz/"), relative);
        }
        else
        {
            // Fallback: construct relative URI against the CSFD base
            pageUri = new Uri(new Uri("https://www.csfd.cz/"), remainingArgs[1]);
        }
    }

    var performancesService = new PerformancesService();
    var schedules = await performancesService.GetSchedules(pageUri, period);

    Console.WriteLine($"Fetched {schedules.Count} schedules for period '{period}'.");
    Console.WriteLine("----------------------------------------");

    var culture = CultureInfo.InvariantCulture;

    // Group by movie and print a compact summary (venues are referenced by ID)
    var movieGroups = schedules
        .GroupBy(s => s.MovieId)
        .OrderByDescending(g => g.Sum(s => s.Performances.Sum(p => p.Showtimes.Count)))
        .ToList();

    foreach (var group in movieGroups.Take(20))
    {
        var movieTitle = group.First().MovieTitle ?? $"Film #{group.Key}";
        Console.WriteLine($"Movie: {movieTitle} ({group.Key})");

        var venueSummaries = new List<string>();
        foreach (var schedule in group)
        {
            foreach (var perf in schedule.Performances)
            {
                var showtimes = perf.Showtimes
                    .OrderBy(s => s.StartAt)
                    .Select(s => s.StartAt.ToString("HH:mm", culture) + (s.TicketsAvailable ? "*" : string.Empty))
                    .Distinct()
                    .ToList();

                if (showtimes.Count == 0)
                    continue;

                venueSummaries.Add($"Venue {perf.VenueId}: {string.Join(", ", showtimes)}");
            }
        }

        if (venueSummaries.Count == 0)
            continue;

        Console.WriteLine("  Theaters: " + string.Join("; ", venueSummaries));
        Console.WriteLine();
    }

    Console.WriteLine("(*) indicates an active ticket link.");
}