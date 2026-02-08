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
                // Always fetch and store the movie without checking existing data
                // Scrape CSFD
                var csfdMovie = await csfdScraper.ScrapeMovie(movieId);

                // Attempt to resolve TMDB metadata
                var tmdbMovie = await csfdScraper.ResolveTmdb(csfdMovie);

                var merged = csfdMovie.Merge(tmdbMovie);

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