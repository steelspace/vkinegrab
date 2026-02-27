using vkinegrab.Services.Csfd;

namespace vkinegrab.Services;

public class PremiereStoreService
{
    private readonly IPremiereScraper premiereScraper;
    private readonly IDatabaseService databaseService;
    private readonly MovieCollectorService movieCollectorService;

    public PremiereStoreService(IPremiereScraper premiereScraper, IDatabaseService databaseService, MovieCollectorService movieCollectorService)
    {
        this.premiereScraper = premiereScraper;
        this.databaseService = databaseService;
        this.movieCollectorService = movieCollectorService;
    }

    public async Task<(int Stored, int Failed)> GrabAndStorePremieresAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[PremiereStoreService] Clearing existing premieres...");
        await databaseService.ClearPremieresAsync().ConfigureAwait(false);
        Console.WriteLine("[PremiereStoreService] ✓ Premieres cleared");

        var currentYear = DateTime.UtcNow.Year;
        var allPremieres = new List<Models.Premiere>();

        for (var year = currentYear; year <= currentYear + 1; year++)
        {
            var premieres = await premiereScraper.ScrapePremieresAsync(year, cancellationToken).ConfigureAwait(false);
            allPremieres.AddRange(premieres);
        }

        Console.WriteLine($"[PremiereStoreService] Storing {allPremieres.Count} premieres...");

        int stored = 0;
        int failed = 0;

        foreach (var premiere in allPremieres)
        {
            try
            {
                await databaseService.StorePremiereAsync(premiere).ConfigureAwait(false);
                stored++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Failed to store premiere {premiere.CsfdId}: {ex.Message}");
                failed++;
            }
        }

        Console.WriteLine($"[PremiereStoreService] Done. Stored: {stored}. Failed: {failed}.");

        // Collect movie metadata for all premiere movie IDs
        var premiereMovieIds = allPremieres.Select(p => p.CsfdId).Distinct().ToList();
        Console.WriteLine($"[PremiereStoreService] Collecting movies for {premiereMovieIds.Count} premiere IDs...");
        var (fetched, skipped, movieFailed) = await movieCollectorService.CollectMoviesAsync(premiereMovieIds, cancellationToken);
        Console.WriteLine($"[PremiereStoreService] Movies — Fetched: {fetched}. Skipped: {skipped}. Failed: {movieFailed}.");

        return (stored, failed);
    }
}
