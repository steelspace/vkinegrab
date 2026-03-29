using vkinegrab.Models;

namespace vkinegrab.Services.VenueScrapers;

public interface IVenueScraper
{
    /// <summary>Stable identifier matching Venue.Id in MongoDB.</summary>
    int VenueId { get; }

    /// <summary>
    /// Scrape this venue's schedule and return parsed performances, discovered
    /// venues, and any movie stubs (title, csfd_id if known, etc.) found on site.
    /// Full metadata is resolved later by MovieCollectorService.
    /// </summary>
    Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result returned by a venue-specific scraper.
/// Schedules and MovieStubs feed into the existing storage and enrichment pipeline.
/// </summary>
public record VenueScrapeResult(
    Venue Venue,
    IReadOnlyList<Schedule> Schedules,
    /// <summary>
    /// Partial movie data available from the venue's own site.
    /// Fields that are unknown can be left null/empty; they will be filled
    /// by the CSFD → TMDB → IMDb enrichment pipeline if CsfdId is set.
    /// </summary>
    IReadOnlyList<CsfdMovie> MovieStubs
);
