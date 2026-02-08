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
                // Check existing movie and decide whether to fetch based on premiere (release) date
                var existing = await databaseService.GetMovie(movieId).ConfigureAwait(false);
                var now = DateTime.UtcNow;

                bool shouldFetchCsfd = false;

                if (existing == null)
                {
                    // Missing movie -> always fetch
                    shouldFetchCsfd = true;
                }
                else
                {
                    // Determine age since release
                    var releaseDate = existing.ReleaseDate;
                    var storedAt = existing.StoredAt ?? DateTime.MinValue;

                    if (releaseDate.HasValue)
                    {
                        var age = now - releaseDate.Value.Date;

                        // <= 3 months -> fetch every time
                        if (age <= TimeSpan.FromDays(90))
                        {
                            shouldFetchCsfd = true;
                        }
                        // 3-12 months -> update once a week
                        else if (age <= TimeSpan.FromDays(365))
                        {
                            shouldFetchCsfd = (now - storedAt) >= TimeSpan.FromDays(7);
                        }
                        // > 12 months -> update once in two weeks
                        else
                        {
                            shouldFetchCsfd = (now - storedAt) >= TimeSpan.FromDays(14);
                        }
                    }
                    else
                    {
                        // Unknown release date -> conservative 2 weeks interval
                        shouldFetchCsfd = (now - storedAt) >= TimeSpan.FromDays(14);
                    }
                }

                if (shouldFetchCsfd)
                {
                    // Scrape CSFD details (pass existing ImdbId to avoid heuristic lookup when present)
                    var csfdMovie = await csfdScraper.ScrapeMovie(movieId, existing?.ImdbId).ConfigureAwait(false);

                    // Always attempt to resolve TMDB when we fetched CSFD (no separate refresh logic)
                    var tmdbMovie = await csfdScraper.ResolveTmdb(csfdMovie).ConfigureAwait(false);

                    var merged = csfdMovie.Merge(tmdbMovie);

                    // Preserve existing identifiers and TMDB fields when Merge didn't provide them
                    if (existing != null)
                    {
                        if (!merged.TmdbId.HasValue && existing.TmdbId.HasValue) merged.TmdbId = existing.TmdbId;
                        if (string.IsNullOrEmpty(merged.ImdbId) && !string.IsNullOrEmpty(existing.ImdbId)) merged.ImdbId = existing.ImdbId;
                        merged.PosterUrl = !string.IsNullOrEmpty(merged.PosterUrl) ? merged.PosterUrl : existing.PosterUrl;
                        merged.BackdropUrl = !string.IsNullOrEmpty(merged.BackdropUrl) ? merged.BackdropUrl : existing.BackdropUrl;
                        merged.VoteAverage = merged.VoteAverage ?? existing.VoteAverage;
                        merged.VoteCount = merged.VoteCount ?? existing.VoteCount;
                        merged.Popularity = merged.Popularity ?? existing.Popularity;
                        merged.ReleaseDate = merged.ReleaseDate ?? existing.ReleaseDate;
                    }

                    await databaseService.StoreMovie(merged).ConfigureAwait(false);
                    fetched++;
                }
                else
                {
                    skipped++;
                }
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