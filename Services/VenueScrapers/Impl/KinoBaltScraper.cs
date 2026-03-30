using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Kino Balt (Prostor Balt) — Blahnikova 575/1, Praha 3. Program: https://kino.prostorbalt.cz/en/
///
/// WordPress + Qubely page builder. The schedule content is rendered entirely
/// client-side; the raw HTTP response contains only the page skeleton.
///
/// TODO: Determine whether a WP REST API endpoint is available, or implement
/// Playwright-based rendering + DOM parsing once the rendered structure is known.
/// </summary>
public class KinoBaltScraper : VenueScraperBase
{
    private static readonly VenueConfig Config = new(
        VenueId: 0,  // TODO: replace with CSFD venue ID
        Name: "Kino Balt",
        City: "Praha",
        BaseUrl: new Uri("https://kino.prostorbalt.cz/en/"));

    public KinoBaltScraper(IHtmlFetcher fetcher, ILogger<KinoBaltScraper> logger)
        : base(fetcher, logger) { }

    public override int VenueId => Config.VenueId;

    public override Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogWarning("[KinoBalt] Scraper not yet implemented (JS-rendered WordPress, parsing not yet designed).");
        var venue = new Venue { Id = Config.VenueId, Name = Config.Name, City = Config.City };
        return Task.FromResult(new VenueScrapeResult(venue, [], []));
    }
}
