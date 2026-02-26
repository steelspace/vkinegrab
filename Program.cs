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

if (args.Length > 0 && args[0].Equals("delete-movies", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Deleting all movies from the database...");
    await databaseService.ClearMoviesAsync();
    Console.WriteLine("✓ All movies deleted.");
    return;
}

if (args.Length > 0 && args[0].Equals("backfill-credits", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Backfilling TMDB credits (cast & crew) for all movies...");
    var csfdScraper = serviceProvider.GetRequiredService<ICsfdScraper>();
    var movies = await databaseService.GetAllMoviesAsync();

    if (movies.Count == 0)
    {
        Console.WriteLine("No movies found.");
        return;
    }

    var updated = 0;
    var skipped = 0;
    var failed = 0;

    foreach (var movie in movies)
    {
        try
        {
            if (!movie.TmdbId.HasValue)
            {
                skipped++;
                continue;
            }

            if (movie.Credits.Count > 0)
            {
                skipped++;
                continue;
            }

            var credits = await csfdScraper.FetchCredits(movie.TmdbId.Value);
            if (credits.Count == 0)
            {
                skipped++;
                continue;
            }

            movie.Credits = credits;
            await databaseService.StoreMovie(movie);
            updated++;
            Console.WriteLine($"  ✓ {movie.CsfdId}: {movie.Title ?? "Untitled"} — {credits.Count} credits");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ {movie.CsfdId}: {movie.Title ?? "Untitled"} — {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine($"Done. Updated: {updated}. Skipped: {skipped}. Failed: {failed}.");
    return;
}

if (args.Length > 0 && args[0].Equals("backfill-origin-codes", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Backfilling origin country codes for all movies...");
    var movies = await databaseService.GetAllMoviesAsync();

    if (movies.Count == 0)
    {
        Console.WriteLine("No movies found.");
        return;
    }

    var updated = 0;
    var skipped = 0;
    var failed = 0;

    foreach (var movie in movies)
    {
        try
        {
            var sourceCountries = BuildOriginCountryCandidates(movie);
            var codes = CountryCodeMapper.MapToIsoAlpha2(sourceCountries);

            var current = movie.OriginCountryCodes ?? new List<string>();
            var changed = !current.SequenceEqual(codes, StringComparer.OrdinalIgnoreCase);

            if (!changed)
            {
                skipped++;
                continue;
            }

            movie.OriginCountryCodes = codes;
            await databaseService.StoreMovie(movie);
            updated++;
        }
        catch
        {
            failed++;
        }
    }

    Console.WriteLine($"Done. Updated: {updated}. Skipped: {skipped}. Failed: {failed}.");
    return;
}

if (args.Length > 0 && args[0].Equals("backfill-localized-titles", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Normalizing localized title keys to ISO country codes...");
    var movies = await databaseService.GetAllMoviesAsync();

    if (movies.Count == 0)
    {
        Console.WriteLine("No movies found.");
        return;
    }

    var updated = 0;
    var skipped = 0;
    var failed = 0;

    foreach (var movie in movies)
    {
        try
        {
            var normalizedTitles = NormalizeLocalizedTitles(movie.LocalizedTitles);
            if (LocalizedTitlesAreEquivalent(movie.LocalizedTitles, normalizedTitles))
            {
                skipped++;
                continue;
            }

            movie.LocalizedTitles = normalizedTitles;
            await databaseService.StoreMovie(movie);
            updated++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ {movie.CsfdId}: {movie.Title ?? "Untitled"} — {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine($"Done. Updated: {updated}. Skipped: {skipped}. Failed: {failed}.");
    return;
}

if (args.Length > 0 && args[0].Equals("report-origin-codes", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Generating origin country code coverage report...");
    var movies = await databaseService.GetAllMoviesAsync();

    if (movies.Count == 0)
    {
        Console.WriteLine("No movies found.");
        return;
    }

    var missing = movies
        .Where(m => !string.IsNullOrWhiteSpace(m.Origin))
        .Where(m => m.OriginCountryCodes == null || m.OriginCountryCodes.Count == 0)
        .OrderBy(m => m.Title)
        .ToList();

    var withOrigin = movies.Count(m => !string.IsNullOrWhiteSpace(m.Origin));
    var withCodes = movies.Count(m => m.OriginCountryCodes != null && m.OriginCountryCodes.Count > 0);

    Console.WriteLine($"Total movies: {movies.Count}");
    Console.WriteLine($"Movies with origin text: {withOrigin}");
    Console.WriteLine($"Movies with origin country codes: {withCodes}");
    Console.WriteLine($"Movies missing codes (while having origin text): {missing.Count}");

    if (missing.Count > 0)
    {
        Console.WriteLine("\nSample missing entries (up to 50):");
        foreach (var movie in missing.Take(50))
        {
            var origin = string.IsNullOrWhiteSpace(movie.Origin) ? "N/A" : movie.Origin;
            Console.WriteLine($"- {movie.CsfdId}: {movie.Title} | Origin: {origin}");
        }
    }

    return;
}

if (args.Length > 0 && args[0].Equals("cleanup-origin-countries", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Removing legacy origin_countries field from MongoDB movies collection...");
    var modified = await databaseService.RemoveLegacyOriginCountriesFieldAsync();
    Console.WriteLine($"Done. Updated documents: {modified}.");
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

if (args.Length > 0 && args[0].Equals("update-trailers", StringComparison.OrdinalIgnoreCase))
{
    var csfdScraperForTrailers = serviceProvider.GetRequiredService<ICsfdScraper>();
    var allMovies = await databaseService.GetAllMoviesAsync();
    var moviesWithTmdb = allMovies.Where(m => m.TmdbId.HasValue).ToList();

    if (moviesWithTmdb.Count == 0)
    {
        Console.WriteLine("No movies with TMDB data found.");
        return;
    }

    Console.WriteLine($"Found {moviesWithTmdb.Count} movies with TMDB IDs. Fetching trailer URLs...");

    var updated = 0;
    var skipped = 0;
    var failed = 0;

    foreach (var movie in moviesWithTmdb)
    {
        try
        {
            var trailerUrl = await csfdScraperForTrailers.FetchTrailerUrl(movie.TmdbId!.Value);

            if (!string.IsNullOrWhiteSpace(trailerUrl) && trailerUrl != movie.TrailerUrl)
            {
                movie.TrailerUrl = trailerUrl;
                await databaseService.StoreMovie(movie);
                Console.WriteLine($"  ✓ {movie.Title} (TMDB {movie.TmdbId}) → {trailerUrl}");
                updated++;
            }
            else if (!string.IsNullOrWhiteSpace(movie.TrailerUrl))
            {
                skipped++;
            }
            else
            {
                Console.WriteLine($"  - {movie.Title} (TMDB {movie.TmdbId}) — no trailer found");
                skipped++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ {movie.Title} (TMDB {movie.TmdbId}) — error: {ex.Message}");
            failed++;
        }

        await Task.Delay(250); // Rate limit
    }

    Console.WriteLine($"\nDone. Updated: {updated}. Skipped: {skipped}. Failed: {failed}.");
    return;
}

if (args.Length > 0 && args[0].Equals("fix-movie", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2 || !int.TryParse(args[1], out var fixCsfdId))
    {
        Console.WriteLine("Usage: fix-movie <csfdId>");
        Console.WriteLine("Clears the TMDB pairing for the given movie and re-resolves metadata from scratch.");
        return;
    }

    var existing = await databaseService.GetMovie(fixCsfdId);
    if (existing == null)
    {
        Console.WriteLine($"Movie with CSFD ID {fixCsfdId} not found in database.");
        return;
    }

    Console.WriteLine($"Found: {existing.Title} (CSFD {existing.CsfdId})");
    Console.WriteLine($"  Current TmdbId: {existing.TmdbId}");
    Console.WriteLine($"  Current ImdbId: {existing.ImdbId}");

    // Clear TMDB data so the orchestrator re-resolves from scratch
    existing.TmdbId = null;
    existing.TrailerUrl = null;
    existing.PosterUrl = null;
    existing.BackdropUrl = null;

    var metadataOrchestrator = serviceProvider.GetRequiredService<IMovieMetadataOrchestrator>();
    var updated = await metadataOrchestrator.ResolveMovieMetadataAsync(fixCsfdId, existing);

    Console.WriteLine($"  Resolved TmdbId: {updated.TmdbId}");
    Console.WriteLine($"  Resolved ImdbId: {updated.ImdbId}");

    await databaseService.StoreMovie(updated);
    Console.WriteLine("✓ Movie updated in database.");
    return;
}

if (args.Length > 0 && args[0].Equals("retry-imdb", StringComparison.OrdinalIgnoreCase))
{
    var metadataOrchestrator = serviceProvider.GetRequiredService<IMovieMetadataOrchestrator>();
    var moviesWithoutImdb = await databaseService.GetMoviesWithMissingImdbAsync();

    if (moviesWithoutImdb.Count == 0)
    {
        Console.WriteLine("All movies already have IMDB data.");
        return;
    }

    Console.WriteLine($"Found {moviesWithoutImdb.Count} movies without IMDB data. Retrying resolution...");

    var resolved = 0;
    var failed = 0;
    var unchanged = 0;

    foreach (var existing in moviesWithoutImdb)
    {
        try
        {
            var updated = await metadataOrchestrator.ResolveMovieMetadataAsync(existing.CsfdId, existing);

            if (!string.IsNullOrWhiteSpace(updated.ImdbId))
            {
                await databaseService.StoreMovie(updated);
                Console.WriteLine($"  ✓ {existing.Title} (CSFD {existing.CsfdId}) → {updated.ImdbId}");
                resolved++;
            }
            else
            {
                Console.WriteLine($"  - {existing.Title} (CSFD {existing.CsfdId}) — still no IMDB match");
                unchanged++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ {existing.Title} (CSFD {existing.CsfdId}) — error: {ex.Message}");
            failed++;
        }

        await Task.Delay(500); // Be nice to the servers
    }

    Console.WriteLine($"\nDone. Resolved: {resolved}. Unchanged: {unchanged}. Failed: {failed}.");
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

if (args.Length > 0 && args[0].Equals("debug-showtimes", StringComparison.OrdinalIgnoreCase))
{
    var targetMovieId = args.Length > 1 && int.TryParse(args[1], out var parsedId) ? parsedId : 1582463; // Christy
    var today = DateOnly.FromDateTime(DateTime.Today);
    
    var schedules = await databaseService.GetSchedulesAsync();
    var venues = await databaseService.GetVenuesAsync();
    var venueDict = venues.ToDictionary(v => v.Id, v => v);
    
    Console.WriteLine("Debugging Showtimes Issue");
    Console.WriteLine("=====================================\n");
    
    // Check Christy schedules from database
    var christySchedules = schedules
        .Where(s => s.Date == today && s.MovieId == targetMovieId)
        .ToList();
    
    if (!christySchedules.Any())
    {
        Console.WriteLine("No schedules found for specified movie.");
        return;
    }
    
    Console.WriteLine($"Found {christySchedules.Count} schedule record(s) for movie ID {targetMovieId} on {today:D}\n");
    
    foreach (var schedule in christySchedules)
    {
        Console.WriteLine($"Schedule: {schedule.MovieTitle} (ID: {schedule.MovieId})");
        Console.WriteLine($"  Date: {schedule.Date}");
        Console.WriteLine($"  StoredAt: {schedule.StoredAt} (UTC)");
        Console.WriteLine($"  Age: {(DateTime.UtcNow - schedule.StoredAt).TotalHours:F1} hours old");
        Console.WriteLine($"  Total performances: {schedule.Performances.Count}");
        Console.WriteLine($"  Venue IDs: {string.Join(", ", schedule.Performances.Select(p => p.VenueId).Distinct().OrderBy(id => id))}");
        Console.WriteLine();
        
        var performancesByVenue = schedule.Performances
            .GroupBy(p => p.VenueId)
            .OrderBy(g => g.Key);
        
        foreach (var venueGroup in performancesByVenue)
        {
            var venueId = venueGroup.Key;
            var venue = venueDict.TryGetValue(venueId, out var v) ? v : null;
            var venueName = venue != null 
                ? $"{venue.Name}{(string.IsNullOrEmpty(venue.City) ? "" : $" ({venue.City})")}"
                : $"UNKNOWN VENUE";
            
            var allShowtimes = venueGroup
                .SelectMany(p => p.Showtimes)
                .OrderBy(s => s.StartAt)
                .Select(s => s.StartAt.ToString("HH:mm"))
                .Distinct();
            
            Console.WriteLine($"  Venue {venueId}: {venueName}");
            Console.WriteLine($"    Times: {string.Join(", ", allShowtimes)}");
        }
    }
    
    // Check total schedules in DB
    Console.WriteLine($"\n\nTotal schedules in database: {schedules.Count}");
    Console.WriteLine($"  For today ({today:d}): {schedules.Count(s => s.Date == today)}");
    Console.WriteLine($"  For future dates: {schedules.Count(s => s.Date > today)}");
    Console.WriteLine($"  For past dates: {schedules.Count(s => s.Date < today)}");
    
    // Now check what the live scraper returns
    Console.WriteLine("\n\nComparing with LIVE CSFD data:");
    Console.WriteLine(new string('=', 60));
    
    try
    {
        var perfService = serviceProvider.GetRequiredService<IPerformancesService>();
        var liveSchedules = await perfService.GetSchedules(new Uri("https://www.csfd.cz/kino/1-praha/?period=today"), "today");
        var liveChristy = liveSchedules.Where(s => s.MovieId == targetMovieId).ToList();
        
        if (liveChristy.Any())
        {
            Console.WriteLine($"\nLive CSFD shows Christy at {liveChristy.Sum(s => s.Performances.Count)} venue(s):");
            
            foreach (var schedule in liveChristy)
            {
                var liveVenueIds = schedule.Performances.Select(p => p.VenueId).Distinct().OrderBy(id => id).ToList();
                Console.WriteLine($"  Venue IDs: {string.Join(", ", liveVenueIds)}");
                
                foreach (var perf in schedule.Performances.OrderBy(p => p.VenueId))
                {
                    var venue = venueDict.TryGetValue(perf.VenueId, out var v) ? v : null;
                    var venueName = venue != null ? venue.Name : $"Unknown #{perf.VenueId}";
                    var times = perf.Showtimes.OrderBy(s => s.StartAt).Select(s => s.StartAt.ToString("HH:mm"));
                    Console.WriteLine($"    Venue {perf.VenueId} ({venueName}): {string.Join(", ", times)}");
                }
            }
            
            // Show differences
            Console.WriteLine("\n\nVenue Discrepancies:");
            Console.WriteLine(new string('-', 60));
            var dbVenueIds = new HashSet<int>(christySchedules.SelectMany(s => s.Performances.Select(p => p.VenueId)));
            var liveVenueIdSet = new HashSet<int>(liveChristy.SelectMany(s => s.Performances.Select(p => p.VenueId)));
            
            var onlyInDb = dbVenueIds.Except(liveVenueIdSet).OrderBy(id => id).ToList();
            var onlyInLive = liveVenueIdSet.Except(dbVenueIds).OrderBy(id => id).ToList();
            
            if (onlyInDb.Any())
            {
                Console.WriteLine($"❌ In database but NOT on live CSFD ({onlyInDb.Count}):");
                foreach (var id in onlyInDb)
                {
                    var name = venueDict.TryGetValue(id, out var v) ? v.Name : $"Unknown #{id}";
                    Console.WriteLine($"   - Venue {id}: {name}");
                }
            }
            
            if (onlyInLive.Any())
            {
                Console.WriteLine($"⚠️  On live CSFD but NOT in database ({onlyInLive.Count}):");
                foreach (var id in onlyInLive)
                {
                    var name = venueDict.TryGetValue(id, out var v) ? v.Name : $"Unknown #{id}";
                    Console.WriteLine($"   - Venue {id}: {name}");
                }
            }
            
            if (!onlyInDb.Any() && !onlyInLive.Any())
            {
                Console.WriteLine("✓ Database and live CSFD match perfectly!");
            }
        }
        else
        {
            Console.WriteLine("\nLive CSFD shows NO Christy showtimes.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\nFailed to fetch live data: {ex.Message}");
    }
    
    return;
}

if (args.Length > 0 && args[0].Equals("movie-showtimes", StringComparison.OrdinalIgnoreCase))
{
    var movieName = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "Christy";
    var today = DateOnly.FromDateTime(DateTime.Today);
    
    var schedules = await databaseService.GetSchedulesAsync();
    var matchingSchedules = schedules
        .Where(s => s.Date == today && s.MovieTitle != null && 
                    s.MovieTitle.Contains(movieName, StringComparison.OrdinalIgnoreCase))
        .ToList();
    
    if (!matchingSchedules.Any())
    {
        Console.WriteLine($"No showtimes found for '{movieName}' on {today:D}.");
        return;
    }
    
    var venues = await databaseService.GetVenuesAsync();
    var venueDict = venues.ToDictionary(v => v.Id, v => v);
    
    Console.WriteLine($"Showtimes for '{movieName}' on {today:D}");
    Console.WriteLine("========================================================\n");
    
    foreach (var schedule in matchingSchedules)
    {
        Console.WriteLine($"Movie: {schedule.MovieTitle} (ID: {schedule.MovieId})");
        Console.WriteLine("--------------------------------------------------------\n");
        
        var performancesByVenue = schedule.Performances
            .GroupBy(p => p.VenueId)
            .OrderBy(g => g.Key);
        
        foreach (var venueGroup in performancesByVenue)
        {
            var venueId = venueGroup.Key;
            var venueName = venueDict.TryGetValue(venueId, out var venue) 
                ? $"{venue.Name}{(string.IsNullOrEmpty(venue.City) ? "" : $" ({venue.City})")}"
                : $"Venue #{venueId}";
            
            var allShowtimes = venueGroup
                .SelectMany(p => p.Showtimes)
                .OrderBy(s => s.StartAt)
                .Select(s => 
                {
                    var time = s.StartAt.ToString("HH:mm");
                    var ticketIndicator = s.TicketsAvailable ? " ✓" : "";
                    return time + ticketIndicator;
                })
                .Distinct();
            
            Console.WriteLine($"  {venueName}");
            Console.WriteLine($"    Times: {string.Join(", ", allShowtimes)}");
            Console.WriteLine();
        }
    }
    
    Console.WriteLine("✓ = Tickets available for online purchase");
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
    var originCountryCodesDisplay = movie.OriginCountryCodes != null && movie.OriginCountryCodes.Count > 0
        ? string.Join(", ", movie.OriginCountryCodes)
        : "N/A";
    Console.WriteLine($"ORIGIN COUNTRY CODES: {originCountryCodesDisplay}");
    Console.WriteLine($"DURATION: {movie.Duration}");
    Console.WriteLine($"RATING: {movie.Rating}");
    Console.WriteLine($"GENRES: {string.Join(", ", movie.Genres)}");
    Console.WriteLine($"DIRECTORS: {string.Join(", ", movie.Directors)}");
    Console.WriteLine($"CAST (First 10): {string.Join(", ", movie.Cast.Take(10))}");
    Console.WriteLine($"POSTER: {movie.PosterUrl}");
    Console.WriteLine($"IMDB: {movie.ImdbId ?? "Not found"}");
    if (movie.ImdbRating.HasValue)
    {
        Console.WriteLine($"IMDB RATING: {movie.ImdbRating:F1}/10 ({movie.ImdbRatingCount} votes)");
    }
    else
    {
        Console.WriteLine($"IMDB RATING: N/A");
    }

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
        Console.WriteLine($"  TRAILER: {movie.TrailerUrl ?? "N/A"}");
        if (!string.IsNullOrWhiteSpace(movie.DescriptionCs))
        {
            Console.WriteLine($"  OVERVIEW (CS): {movie.DescriptionCs}");
        }
        if (!string.IsNullOrWhiteSpace(movie.DescriptionEn))
        {
            Console.WriteLine($"  OVERVIEW (EN): {movie.DescriptionEn}");
        }
    }

    Console.WriteLine("--------------------------------------------------");
    Console.WriteLine("TITLES BY COUNTRY:");
    foreach (var kvp in movie.LocalizedTitles)
    {
        Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
    }
    Console.WriteLine("--------------------------------------------------");
    Console.WriteLine($"DESC (CS): {movie.DescriptionCs}");
    Console.WriteLine($"DESC (EN): {movie.DescriptionEn}");
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

static IEnumerable<string> BuildOriginCountryCandidates(Movie movie)
{
    var candidates = new List<string>();

    if (!string.IsNullOrWhiteSpace(movie.Origin))
    {
        var fromOrigin = System.Text.RegularExpressions.Regex
            .Split(movie.Origin, "[/·•–—|&,]")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x));

        candidates.AddRange(fromOrigin);
    }

    return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
}

static Dictionary<string, string> NormalizeLocalizedTitles(Dictionary<string, string>? localizedTitles)
{
    var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    if (localizedTitles == null)
    {
        return normalized;
    }

    foreach (var entry in localizedTitles)
    {
        if (string.IsNullOrWhiteSpace(entry.Value))
        {
            continue;
        }

        var codes = CountryCodeMapper.MapToIsoAlpha2(new[] { entry.Key });
        var key = codes.Count > 0 ? codes[0] : entry.Key;

        if (!normalized.ContainsKey(key))
        {
            normalized[key] = entry.Value;
        }
    }

    return normalized;
}

static bool LocalizedTitlesAreEquivalent(Dictionary<string, string>? current, Dictionary<string, string> normalized)
{
    if (current == null)
    {
        return normalized.Count == 0;
    }

    var lookup = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase);

    if (lookup.Count != normalized.Count)
    {
        return false;
    }

    foreach (var entry in normalized)
    {
        if (!lookup.TryGetValue(entry.Key, out var existingValue) || existingValue != entry.Value)
        {
            return false;
        }
    }

    return true;
}
