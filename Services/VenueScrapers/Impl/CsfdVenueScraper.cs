using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Default venue scraper that delegates to the existing CSFD performances pipeline.
/// Use this for any venue whose own website is not yet supported.
///
/// Pass a CSFD venue URL like: https://www.csfd.cz/kino/1-praha/?period=all
/// The VenueId should match the numeric ID embedded in that URL.
/// </summary>
public class CsfdVenueScraper : IVenueScraper
{
    private readonly VenueConfig _config;
    private readonly IPerformancesService _performances;
    private readonly ILogger<CsfdVenueScraper> _logger;

    public CsfdVenueScraper(
        VenueConfig config,
        IPerformancesService performances,
        ILogger<CsfdVenueScraper> logger)
    {
        _config = config;
        _performances = performances;
        _logger = logger;
    }

    public int VenueId => _config.VenueId;

    public async Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[{Venue}] Scraping via CSFD at {Url}", _config.Name, _config.BaseUrl);

        var (schedules, venues) = await _performances
            .GetSchedulesWithVenues(_config.BaseUrl, "all", cancellationToken)
            .ConfigureAwait(false);

        // Best-effort: pick the venue entry that matches our ID, or build a minimal one.
        var venue = venues.FirstOrDefault(v => v.Id == _config.VenueId)
            ?? new Venue { Id = _config.VenueId, Name = _config.Name, City = _config.City };

        // CSFD pages already contain CSFD IDs, so no movie stubs needed —
        // MovieCollectorService will pick up new movie IDs from the stored schedules.
        return new VenueScrapeResult(venue, schedules, []);
    }
}
