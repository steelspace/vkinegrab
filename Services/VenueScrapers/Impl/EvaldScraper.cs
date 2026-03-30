using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Komorní kino Evald — Národní 28, Praha 1. Program: https://www.evald.cz
///
/// Site consistently returns HTTP 429 (rate limit / bot protection).
///
/// TODO: Add appropriate request headers (Referer, Accept-Language) and/or
/// configure a retry delay to work around the rate limit.
/// </summary>
public class EvaldScraper : VenueScraperBase
{
    private static readonly VenueConfig Config = new(
        VenueId: 0,  // TODO: replace with CSFD venue ID
        Name: "Kino Evald",
        City: "Praha",
        BaseUrl: new Uri("https://www.evald.cz"));

    public EvaldScraper(IHtmlFetcher fetcher, ILogger<EvaldScraper> logger)
        : base(fetcher, logger) { }

    public override int VenueId => Config.VenueId;

    public override Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogWarning("[Evald] Scraper not yet implemented (site returns 429, bot-protection bypass needed).");
        var venue = new Venue { Id = Config.VenueId, Name = Config.Name, City = Config.City };
        return Task.FromResult(new VenueScrapeResult(venue, [], []));
    }
}
