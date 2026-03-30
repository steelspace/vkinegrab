using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Kino Dlabačov — Bělohorská 125/24, Praha 6. Program: https://dlabacov.cz/en/
///
/// WordPress site that loads schedule data via the WP REST API and delegates
/// ticketing to GoOut.cz.
///
/// TODO: Determine the correct WP REST API endpoint, e.g.:
///   GET https://dlabacov.cz/wp-json/wp/v2/events?per_page=50
/// Then deserialize the JSON response and map to Schedule/Showtime.
/// </summary>
public class DlabacovScraper : VenueScraperBase
{
    private static readonly VenueConfig Config = new(
        VenueId: 0,  // TODO: replace with CSFD venue ID
        Name: "Dlabačov",
        City: "Praha",
        BaseUrl: new Uri("https://dlabacov.cz/en/"));

    public DlabacovScraper(IHtmlFetcher fetcher, ILogger<DlabacovScraper> logger)
        : base(fetcher, logger) { }

    public override int VenueId => Config.VenueId;

    public override Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogWarning("[Dlabačov] Scraper not yet implemented (WP REST API / GoOut integration needed).");
        var venue = new Venue { Id = Config.VenueId, Name = Config.Name, City = Config.City };
        return Task.FromResult(new VenueScrapeResult(venue, [], []));
    }
}
