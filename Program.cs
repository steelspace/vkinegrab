using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

// Set up Dependency Injection
var services = new ServiceCollection();
services.AddSingleton<IDatabaseService>(new DatabaseService(mongoConnectionString));
services.AddCsfdServices(tmdbBearerToken);
var serviceProvider = services.BuildServiceProvider();

var databaseService = serviceProvider.GetRequiredService<IDatabaseService>();

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
    var localScraper = serviceProvider.GetRequiredService<ICsfdScraper>();
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var tester = new vkinegrab.TestScraper(localScraper, httpClientFactory);
    
    if (testArgs.Length > 0 && testArgs[0].Equals("showtimes", StringComparison.OrdinalIgnoreCase))
    {
        var period = testArgs.Length > 1 && !string.IsNullOrWhiteSpace(testArgs[1]) ? testArgs[1] : "today";
        var pageUrl = testArgs.Length > 2 && !string.IsNullOrWhiteSpace(testArgs[2]) ? testArgs[2] : null;
        var limit = testArgs.Length > 3 && int.TryParse(testArgs[3], out var parsedLimit) && parsedLimit > 0 ? parsedLimit : 5;
        await tester.RunCinemaShowtimes(period, pageUrl, limit);
        return;
    }

    var maxMovies = 100;
    if (testArgs.Length > 0 && int.TryParse(testArgs[0], out var argMax) && argMax > 0)
    {
        maxMovies = argMax;
    }

    await tester.RunTests(maxMovies);
    return;
}

if (args.Length > 0 && args[0].Equals("cinemas", StringComparison.OrdinalIgnoreCase))
{
    await PrintCinemaSchedule(serviceProvider, args);
    return;
}

if (args.Length > 0 && args[0].Equals("showtimes", StringComparison.OrdinalIgnoreCase))
{
    var localScraper = serviceProvider.GetRequiredService<ICsfdScraper>();
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var tester = new vkinegrab.TestScraper(localScraper, httpClientFactory);
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

    var storeService = serviceProvider.GetRequiredService<SchedulesStoreService>();
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

    var storeService = serviceProvider.GetRequiredService<SchedulesStoreService>();

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

    var collector = serviceProvider.GetRequiredService<MovieCollectorService>();

    Console.WriteLine("Collecting movies from newly grabbed schedules...");
    var (fetched, skipped, failedMovies) = await collector.CollectMoviesFromSchedulesAsync(schedules);

    Console.WriteLine($"Done. Fetched: {fetched}. Skipped: {skipped}. Failed: {failedMovies}.");

    // After storing schedules and movies, optionally fetch venues
    Console.WriteLine("To fetch venue details for stored performances run: grab-venues");
    return;
}

if (args.Length > 0 && args[0].Equals("verify-discrepancies", StringComparison.OrdinalIgnoreCase))
{
    var perfService = serviceProvider.GetRequiredService<IPerformancesService>();
    var dbSchedules = await databaseService.GetSchedulesAsync();
    var liveSchedules = await perfService.GetSchedules(new Uri("https://www.csfd.cz/kino/1-praha/?period=today"), "today");
    
    var today = DateOnly.FromDateTime(DateTime.Today);
    var dbToday = dbSchedules.Where(s => s.Date == today).ToList();

    Console.WriteLine($"--- Discrepancy Report ({today}) ---");
    Console.WriteLine($"{"Movie Title",-40} | {"Live",-5} | {"DB",-5} | {"Status"}");
    Console.WriteLine(new string('-', 70));

    var allIds = liveSchedules.Select(s => s.MovieId).Union(dbToday.Select(s => s.MovieId)).Distinct();

    int matches = 0;
    int mismatches = 0;

    foreach (var id in allIds)
    {
        var live = liveSchedules.FirstOrDefault(s => s.MovieId == id);
        var db = dbToday.FirstOrDefault(s => s.MovieId == id);

        var liveCount = live?.Performances.Sum(p => p.Showtimes.Count) ?? 0;
        var dbCount = db?.Performances.Sum(p => p.Showtimes.Count) ?? 0;

        if (liveCount != dbCount || liveCount == 0 || dbCount == 0)
        {
            mismatches++;
            var title = live?.MovieTitle ?? db?.MovieTitle ?? $"Film #{id}";
            var status = liveCount == dbCount ? "✓ Match" : (dbCount == 0 ? "❌ Missing" : (liveCount > dbCount ? "⚠️ DB Under" : "⚠️ DB Over"));
            Console.WriteLine($"{title.PadRight(40).Substring(0, 40)} | {liveCount,-5} | {dbCount,-5} | {status}");
        }
        else
        {
            matches++;
        }
    }
    Console.WriteLine(new string('-', 70));
    Console.WriteLine($"Summary: {matches} matches, {mismatches} discrepancies.");
    return;
}

if (args.Length > 0 && args[0].Equals("grab-venues", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Grabbing venues referenced by stored performances...");
    var csfdScraper = serviceProvider.GetRequiredService<ICsfdScraper>();

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

    var collector = serviceProvider.GetRequiredService<MovieCollectorService>();

    Console.WriteLine("Collecting movies from stored schedules...");
    var schedules = await databaseService.GetSchedulesAsync();
    var (fetched, skipped, failed) = await collector.CollectMoviesFromSchedulesAsync(schedules);

    Console.WriteLine($"Done. Fetched: {fetched}. Skipped: {skipped}. Failed: {failed}.");
    return;
}

var scraper = serviceProvider.GetRequiredService<ICsfdScraper>();
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
    var metadataOrchestrator = serviceProvider.GetRequiredService<IMovieMetadataOrchestrator>();
    var existingMovie = await databaseService.GetMovie(movieId);
    var mergedMovie = await metadataOrchestrator.ResolveMovieMetadataAsync(movieId, existingMovie);
    
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
    var movie = mergedMovie; // Use the merged movie for display
    Console.WriteLine($"CSFD SOURCE ID: {movieId}");
    Console.WriteLine($"ID: {movie.CsfdId}"); // Note: Movie model uses CsfdId for the CSFD ID
    Console.WriteLine($"TITLE: {movie.Title}");
    Console.WriteLine($"YEAR: {movie.Year}");
    Console.WriteLine($"ORIGIN: {movie.Origin}");
    var originCountriesDisplay = movie.OriginCountries != null && movie.OriginCountries.Count > 0
        ? string.Join(", ", movie.OriginCountries)
        : "N/A";
    Console.WriteLine($"ORIGIN COUNTRIES: {originCountriesDisplay}");
    Console.WriteLine($"DURATION: {movie.Duration}");
    Console.WriteLine($"RATING: {movie.Rating}");
    Console.WriteLine($"GENRES: {string.Join(", ", movie.Genres)}");
    Console.WriteLine($"DIRECTORS: {string.Join(", ", movie.Directors)}");
    Console.WriteLine($"CAST (First 10): {string.Join(", ", movie.Cast.Take(10))}");
    Console.WriteLine($"POSTER: {movie.PosterUrl}");
    Console.WriteLine($"IMDB: {movie.ImdbId ?? "Not found"}");

    if (movie.TmdbId != null)
    {
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine("TMDB INFO:");
        Console.WriteLine($"  ID: {movie.TmdbId}");
        Console.WriteLine($"  TITLE: {movie.Title}");
        Console.WriteLine($"  ORIGINAL TITLE: {movie.OriginalTitle}");
        Console.WriteLine($"  RELEASE DATE: {movie.ReleaseDate?.ToString("yyyy-MM-dd")}");
        Console.WriteLine($"  RATING: {movie.VoteAverage:F1}/10 ({movie.VoteCount} votes)");
        Console.WriteLine($"  POPULARITY: {movie.Popularity:F1}");
        Console.WriteLine($"  LANGUAGE: {movie.OriginalLanguage}");
        Console.WriteLine($"  ADULT: {movie.Adult}");
        Console.WriteLine($"  POSTER: {movie.PosterUrl ?? "N/A"}");
        Console.WriteLine($"  BACKDROP: {movie.BackdropUrl ?? "N/A"}");
        if (!string.IsNullOrWhiteSpace(movie.Description))
        {
            Console.WriteLine($"  OVERVIEW: {movie.Description}");
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

static async Task PrintCinemaSchedule(IServiceProvider serviceProvider, string[] args)
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

    var performancesService = serviceProvider.GetRequiredService<IPerformancesService>();
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