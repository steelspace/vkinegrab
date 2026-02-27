using vkinegrab.Services.Csfd;

namespace vkinegrab.Services;

public class PremiereStoreService
{
    private readonly IPremiereScraper premiereScraper;
    private readonly IDatabaseService databaseService;

    public PremiereStoreService(IPremiereScraper premiereScraper, IDatabaseService databaseService)
    {
        this.premiereScraper = premiereScraper;
        this.databaseService = databaseService;
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
        return (stored, failed);
    }
}
