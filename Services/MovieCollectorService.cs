using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services;

public class MovieCollectorService
{
    private readonly ICsfdScraper csfdScraper;
    private readonly IDatabaseService databaseService;

    public MovieCollectorService(ICsfdScraper csfdScraper, IDatabaseService databaseService)
    {
        this.csfdScraper = csfdScraper;
        this.databaseService = databaseService;
    }

    /// <summary>
    /// Iterates provided schedules (e.g., loaded from the database), and for each unique movie id
    /// fetches details from CSFD (and TMDB), then stores the merged movie in the database if not present.
    /// Returns a tuple of (fetched, skipped, failed) counts.
    /// </summary>
    public async Task<(int Fetched, int Skipped, int Failed)> CollectMoviesFromSchedulesAsync(IEnumerable<Schedule> schedules, CancellationToken cancellationToken = default)
    {
        var uniqueMovieIds = schedules?.Select(s => s.MovieId).Where(id => id > 0).Distinct().ToList() ?? new List<int>();

        var fetched = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var movieId in uniqueMovieIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var existing = await databaseService.GetMovie(movieId);

                // Decide whether we should fetch/update this movie based on its premiere and last stored time
                var shouldFetch = false;
                if (existing == null)
                {
                    shouldFetch = true; // never stored before
                }
                else
                {
                    var now = DateTime.UtcNow;
                    var releaseDate = existing.ReleaseDate;
                    var storedAt = existing.StoredAt;

                    // Determine frequency: default monthly (30 days) when release date unknown
                    var interval = TimeSpan.FromDays(30);

                    if (releaseDate.HasValue)
                    {
                        var age = now - releaseDate.Value;
                        if (age.TotalDays < 30)
                            interval = TimeSpan.FromDays(1);
                        else if (age.TotalDays < 365)
                            interval = TimeSpan.FromDays(7);
                        else
                            interval = TimeSpan.FromDays(30);
                    }

                    // If never stored or last stored older than interval, fetch
                    if (!storedAt.HasValue || (now - storedAt.Value) >= interval)
                    {
                        shouldFetch = true;
                    }
                }

                if (!shouldFetch)
                {
                    skipped++;
                    continue;
                }

                // Scrape CSFD
                var csfdMovie = await csfdScraper.ScrapeMovie(movieId);

                // Determine how to obtain TMDB metadata:
                // - If we have an existing TmdbId, fetch details by that ID
                // - Otherwise, attempt to resolve via ResolveTmdb (search/IMDb)
                TmdbMovie? tmdbMovie = null;
                if (existing != null && existing.TmdbId.HasValue)
                {
                    tmdbMovie = await csfdScraper.FetchTmdbById(existing.TmdbId.Value);
                }
                else
                {
                    tmdbMovie = await csfdScraper.ResolveTmdb(csfdMovie);
                }

                var merged = csfdMovie.Merge(tmdbMovie);

                // Preserve existing IDs if merge didn't produce them
                if (existing != null)
                {
                    if (!merged.TmdbId.HasValue && existing.TmdbId.HasValue)
                        merged.TmdbId = existing.TmdbId;

                    if (string.IsNullOrWhiteSpace(merged.ImdbId) && !string.IsNullOrWhiteSpace(existing.ImdbId))
                        merged.ImdbId = existing.ImdbId;
                }

                await databaseService.StoreMovie(merged);
                fetched++;
            }
            catch (Exception ex)
            {
                // Log and continue (including full exception for debugging)
                Console.WriteLine($"Failed to fetch/store movie {movieId}: {ex}");
                failed++;
            }
        }

        return (fetched, skipped, failed);
    }
}