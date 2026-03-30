using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Kino 35 (Institut français) — Štěpánská 644/35, Praha 1. Program: https://kino35.ifp.cz/en
///
/// JavaScript-rendered site. The schedule is loaded client-side via a
/// <c>calendardays</c> JS object. Playwright renders the page but the parsing
/// of the rendered output still needs to be implemented.
///
/// TODO: Use Playwright to fully render the page, then inspect the resulting
/// DOM and implement the XPath parsing here.
/// </summary>
public class Kino35Scraper : VenueScraperBase
{
    private static readonly VenueConfig Config = new(
        VenueId: 0,  // TODO: replace with CSFD venue ID
        Name: "Kino 35",
        City: "Praha",
        BaseUrl: new Uri("https://kino35.ifp.cz/en"));

    public Kino35Scraper(IHtmlFetcher fetcher, ILogger<Kino35Scraper> logger)
        : base(fetcher, logger) { }

    public override int VenueId => Config.VenueId;

    public override Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogWarning("[Kino35] Scraper not yet implemented (JS-rendered, Playwright parsing needed).");
        var venue = new Venue { Id = Config.VenueId, Name = Config.Name, City = Config.City };
        return Task.FromResult(new VenueScrapeResult(venue, [], []));
    }
}
