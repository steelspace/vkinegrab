using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Modřanský biograf — U Kina 1/44, Praha 12. Program: https://www.modranskybiograf.cz
///
/// Site consistently returns HTTP 429 (rate limit / bot protection).
///
/// TODO: Add appropriate request headers and/or retry delay to work around
/// the rate limit, then implement HTML parsing once the site responds.
/// </summary>
public class ModranskyBiografScraper : VenueScraperBase
{
    private static readonly VenueConfig Config = new(
        VenueId: 0,  // TODO: replace with CSFD venue ID
        Name: "Modřanský biograf",
        City: "Praha",
        BaseUrl: new Uri("https://www.modranskybiograf.cz"));

    public ModranskyBiografScraper(IHtmlFetcher fetcher, ILogger<ModranskyBiografScraper> logger)
        : base(fetcher, logger) { }

    public override int VenueId => Config.VenueId;

    public override Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogWarning("[ModřanskýBiograf] Scraper not yet implemented (site returns 429, bot-protection bypass needed).");
        var venue = new Venue { Id = Config.VenueId, Name = Config.Name, City = Config.City };
        return Task.FromResult(new VenueScrapeResult(venue, [], []));
    }
}
