using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services;

public class MovieCollectorService
{
    private readonly IMovieMetadataOrchestrator metadataOrchestrator;
    private readonly IDatabaseService databaseService;

    public MovieCollectorService(IMovieMetadataOrchestrator metadataOrchestrator, IDatabaseService databaseService)
    {
        this.metadataOrchestrator = metadataOrchestrator;
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
        return await CollectMoviesAsync(uniqueMovieIds, cancellationToken);
    }

    /// <summary>
    /// Fetches and stores movie metadata for each provided CSFD ID.
    /// Uses smart refresh logic to avoid unnecessary re-fetches.
    /// </summary>
    public async Task<(int Fetched, int Skipped, int Failed)> CollectMoviesAsync(IReadOnlyList<int> movieIds, CancellationToken cancellationToken = default)
    {

        int fetched = 0;
        int skipped = 0;
        int failed = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4, // Conservative default, could be 8 or more
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(movieIds, parallelOptions, async (movieId, ct) =>
        {
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
                    Interlocked.Increment(ref skipped);
                    return;
                }

                var merged = await metadataOrchestrator.ResolveMovieMetadataAsync(movieId, existing, ct);

                await databaseService.StoreMovie(merged);
                Interlocked.Increment(ref fetched);
            }
            catch (Exception ex)
            {
                // Log and continue
                Console.WriteLine($"Failed to fetch/store movie {movieId}: {ex.Message}");
                Interlocked.Increment(ref failed);
            }
        });

        return (fetched, skipped, failed);
    }
}