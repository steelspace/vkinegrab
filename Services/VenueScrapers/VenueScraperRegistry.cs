namespace vkinegrab.Services.VenueScrapers;

/// <summary>
/// Holds all registered <see cref="IVenueScraper"/> implementations and provides
/// lookup by venue ID. Register all scrapers with the DI container as
/// <c>IEnumerable&lt;IVenueScraper&gt;</c> and inject this registry where needed.
/// </summary>
public class VenueScraperRegistry
{
    private readonly IReadOnlyDictionary<int, IVenueScraper> _scrapers;

    public VenueScraperRegistry(IEnumerable<IVenueScraper> scrapers)
    {
        _scrapers = scrapers.ToDictionary(s => s.VenueId);
    }

    /// <summary>Returns the scraper for the given venue ID, or null if none registered.</summary>
    public IVenueScraper? GetScraper(int venueId)
        => _scrapers.TryGetValue(venueId, out var s) ? s : null;

    /// <summary>Returns all registered scrapers.</summary>
    public IReadOnlyList<IVenueScraper> GetAll()
        => _scrapers.Values.ToList();
}
