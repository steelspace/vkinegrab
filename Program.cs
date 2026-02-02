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

    var performancesService = new PerformancesService(new CsfdRowParser(new BadgeExtractor(), new ShowtimeExtractor()));
    var storeService = new SchedulesStoreService(performancesService, databaseService);
    var (schedules, storedSchedules, failedSchedules, storedVenues, failedVenues) = await storeService.StoreSchedulesAndVenuesAsync(pageUri, period);

    if ((schedules?.Count ?? 0) == 0 && storedSchedules + failedSchedules == 0)
    {
        Console.WriteLine("No schedules to store.");
        return;
    }

    Console.WriteLine($"Done. Stored schedules: {storedSchedules}. Failed: {failedSchedules}.");

    if (storedVenues > 0)
    {
        Console.WriteLine($"Stored discovered venues: {storedVenues}. Failed: {failedVenues}.");
    }

    return;
    }

if (args.Length > 0 && args[0].Equals("grab-all", StringComparison.OrdinalIgnoreCase))
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

    var performancesService = new PerformancesService(new CsfdRowParser(new BadgeExtractor(), new ShowtimeExtractor()));
    var storeService = new SchedulesStoreService(performancesService, databaseService);

    // Fetch + store in one step and obtain schedules for the movie collector (avoids double-fetch)
    IReadOnlyList<Schedule> schedules;
    try
    {
        var (fetchedSchedules, storedSchedules, failedSchedules, storedVenues, failedVenues) = await storeService.StoreSchedulesAndVenuesAsync(pageUri, period);
        schedules = fetchedSchedules;

        Console.WriteLine($"Done storing schedules. Stored: {storedSchedules}. Failed: {failedSchedules}.");
        if (storedVenues > 0)
        {
            Console.WriteLine($"Stored discovered venues: {storedVenues}. Failed: {failedVenues}.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to download/store schedules: {ex.Message}");
        return;
    }

    if (schedules == null || schedules.Count == 0)
    {
        Console.WriteLine("No schedules to grab.");
        return;
    }

    var csfdScraper = new CsfdScraper(tmdbBearerToken);
    var collector = new MovieCollectorService(csfdScraper, databaseService);

    Console.WriteLine("Collecting movies from newly grabbed schedules...");
    var (fetched, skipped, failedMovies) = await collector.CollectMoviesFromSchedulesAsync(schedules);

    Console.WriteLine($"Done. Fetched: {fetched}. Skipped: {skipped}. Failed: {failedMovies}.");

    // After storing schedules and movies, optionally fetch venues
    Console.WriteLine("To fetch venue details for stored performances run: grab-venues");
    return;
}

if (args.Length > 0 && args[0].Equals("grab-venues", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Grabbing venues referenced by stored performances...");
    var csfdScraper = new CsfdScraper(tmdbBearerToken);

    var schedules = await databaseService.GetSchedulesAsync();
    var venueIds = schedules
        .SelectMany(s => s.Performances)
        .Select(p => p.VenueId)
        .Where(id => id > 0)
        .Distinct()
        .OrderBy(id => id)
        .ToList();

    if (venueIds.Count == 0)
    {
        Console.WriteLine("No venue IDs found in stored schedules.");
        return;
    }

    Console.WriteLine($"Found {venueIds.Count} distinct venue IDs. Querying database to skip already stored venues...");

    var stored = await databaseService.GetVenuesAsync();
    var storedIds = new HashSet<int>(stored.Select(v => v.Id));

    var toFetch = venueIds.Where(id => !storedIds.Contains(id)).ToList();

    Console.WriteLine($"Need to fetch {toFetch.Count} venues.");

    var fetched = 0;
    var failed = 0;

    foreach (var venueId in toFetch)
    {
        try
        {
            var venue = await csfdScraper.ScrapeVenue(venueId);
            await databaseService.StoreVenue(venue);



            Console.WriteLine($"Stored venue {venueId}: {venue.Name}");
            fetched++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to fetch/store venue {venueId}: {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine($"Done. Fetched: {fetched}. Failed: {failed}.");
    return;
}

if (args.Length > 0 && args[0].Equals("collect-movies", StringComparison.OrdinalIgnoreCase))
{
    var remainingArgs = args.Skip(1).ToArray();
    var period = remainingArgs.Length > 0 && !string.IsNullOrWhiteSpace(remainingArgs[0]) ? remainingArgs[0] : "today";

    Uri? pageUri = null;
    if (remainingArgs.Length > 1 && !string.IsNullOrWhiteSpace(remainingArgs[1]))
    {
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
            pageUri = new Uri(new Uri("https://www.csfd.cz/"), remainingArgs[1]);
        }
    }

    var csfdScraper = new CsfdScraper(tmdbBearerToken);
    var performancesService = new PerformancesService(new CsfdRowParser(new BadgeExtractor(), new ShowtimeExtractor()));
    var collector = new MovieCollectorService(csfdScraper, databaseService);

    Console.WriteLine("Collecting movies from stored schedules...");
    var schedules = await databaseService.GetSchedulesAsync();
    var (fetched, skipped, failed) = await collector.CollectMoviesFromSchedulesAsync(schedules);

    Console.WriteLine($"Done. Fetched: {fetched}. Skipped: {skipped}. Failed: {failed}.");
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

    var performancesService = new PerformancesService(new CsfdRowParser(new BadgeExtractor(), new ShowtimeExtractor()));
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