using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Platforms;

/// <summary>
/// Shared platform scraper for CineStar Prague locations.
///
/// CineStar runs a JavaScript SPA (Vue.js); no static HTML is available.
/// This stub uses Playwright (via <see cref="IHtmlFetcher"/>) to render the page
/// and then parses the resulting HTML.
///
/// TODO: Inspect the rendered HTML via Playwright and implement the actual
/// parsing logic. Alternatively, reverse-engineer the SPA's internal API endpoint.
/// </summary>
public abstract class CineStarPlatformScraper : VenueScraperBase
{
    private readonly VenueConfig _config;

    protected CineStarPlatformScraper(VenueConfig config, IHtmlFetcher htmlFetcher, ILogger logger)
        : base(htmlFetcher, logger)
    {
        _config = config;
    }

    public override int VenueId => _config.VenueId;

    public override async Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogWarning(
            "[{Venue}] CineStar scraper is not yet implemented. " +
            "The site is a JS SPA — implement after inspecting Playwright-rendered HTML.",
            _config.Name);

        var venue = new Venue
        {
            Id   = _config.VenueId,
            Name = _config.Name,
            City = _config.City
        };

        // Playwright renders the page — uncomment and implement parsing once the
        // rendered HTML structure is known.
        // var doc = await FetchDocumentAsync(_config.BaseUrl, cancellationToken);
        // TODO: parse doc

        return new VenueScrapeResult(venue, [], []);
    }
}
