using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Template for scraping a venue's own website directly.
/// Copy this file, rename it to {VenueName}Scraper.cs, and fill in the
/// venue-specific HTML parsing logic.
///
/// To wire it up:
///   1. Add a VenueConfig entry in CsfdServiceCollectionExtensions.AddVenueScrapers()
///   2. Register the scraper with services.AddSingleton&lt;IVenueScraper, YourScraper&gt;()
///
/// The VenueId must match the numeric CSFD venue ID so that performances stored by
/// this scraper link back to the correct venue in MongoDB.
/// </summary>
public class ExampleDirectSiteScraper : VenueScraperBase
{
    private readonly VenueConfig _config;

    public ExampleDirectSiteScraper(
        VenueConfig config,
        IHtmlFetcher htmlFetcher,
        ILogger<ExampleDirectSiteScraper> logger)
        : base(htmlFetcher, logger)
    {
        _config = config;
    }

    public override int VenueId => _config.VenueId;

    public override async Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("[{Venue}] Starting direct-site scrape at {Url}", _config.Name, _config.BaseUrl);

        var doc = await FetchDocumentAsync(_config.BaseUrl, cancellationToken);

        // ── Venue metadata ─────────────────────────────────────────────────────
        var venue = new Venue
        {
            Id   = _config.VenueId,
            Name = _config.Name,
            City = _config.City
            // Optionally populate Address / MapUrl from the page
        };

        // ── Parse showtimes ────────────────────────────────────────────────────
        var schedules = new List<Schedule>();
        var movieStubs = new List<CsfdMovie>();

        // TODO: replace the XPath expressions below with those matching the
        //       actual HTML structure of this venue's website.
        //
        // General pattern:
        //   foreach screening block on the page:
        //     1. Parse the date              → ParseCzechDate(dateText)
        //     2. Parse the movie title       → titleNode.InnerText.Trim()
        //     3. Extract CSFD link if present→ ExtractCsfdId(href)
        //     4. Parse showtimes             → ParseTime(timeText)
        //     5. Call BuildSchedule(...)     → adds to schedules list
        //
        // Example (illustrative, not runnable):
        //
        // var rows = doc.DocumentNode.SelectNodes("//div[@class='program-item']");
        // if (rows != null)
        // {
        //     foreach (var row in rows)
        //     {
        //         var dateText  = row.SelectSingleNode(".//span[@class='date']")?.InnerText;
        //         var titleNode = row.SelectSingleNode(".//a[@class='title']");
        //         var timeText  = row.SelectSingleNode(".//span[@class='time']")?.InnerText;
        //
        //         var date    = ParseCzechDate(dateText);
        //         var csfdId  = ExtractCsfdId(titleNode?.GetAttributeValue("href", null));
        //         var time    = ParseTime(timeText);
        //
        //         if (date == null || csfdId == null || time == null) continue;
        //
        //         var showtime = new Showtime { StartAt = time.Value, TicketsAvailable = false };
        //         schedules.Add(BuildSchedule(date.Value, csfdId.Value, titleNode!.InnerText.Trim(),
        //                                     titleNode.GetAttributeValue("href", null),
        //                                     _config.VenueId, [showtime]));
        //     }
        // }

        Logger.LogInformation("[{Venue}] Scraped {ScheduleCount} schedule(s)", _config.Name, schedules.Count);
        return new VenueScrapeResult(venue, schedules, movieStubs);
    }
}
