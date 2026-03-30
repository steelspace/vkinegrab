using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Kino Kavalírka — Plzeňská 210, Praha 5. Program: https://kinokavalirka.cz/cs/uvod
///
/// Server-rendered. Each screening appears as a block with:
///   Date/time line: "pondělí | 30. 3. | 18:00"  (pipe-separated Czech day | DD. M. | HH:MM)
///   &lt;h2&gt;Film Title&lt;/h2&gt;  (or similar heading)
///   Ticket link: /cs/koupit/[slug]-[ID]
/// </summary>
public class KinoKavalirkaScraper : VenueScraperBase
{
    private static readonly Uri ProgramUri = new("https://kinokavalirka.cz/cs/uvod");
    // "pondělí | 30. 3. | 18:00" — extract day (D), month (M), time (HH:MM)
    private static readonly Regex ScreeningLineRegex = new(
        @"(\d{1,2})\.\s*(\d{1,2})\.\s*\|\s*(\d{1,2}:\d{2})", RegexOptions.Compiled);

    private static readonly VenueConfig Config = new(
        VenueId: 0,  // TODO: replace with CSFD venue ID
        Name: "Kino Kavalírka",
        City: "Praha",
        BaseUrl: ProgramUri);

    public KinoKavalirkaScraper(IHtmlFetcher fetcher, ILogger<KinoKavalirkaScraper> logger)
        : base(fetcher, logger) { }

    public override int VenueId => Config.VenueId;

    public override async Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("[KinoKavalírka] Fetching {Url}", ProgramUri);
        var doc = await FetchDocumentAsync(ProgramUri, cancellationToken);

        var venue = new Venue { Id = Config.VenueId, Name = Config.Name, City = Config.City };
        var schedules = new List<Schedule>();
        var year = DateTime.Today.Year;

        // Each screening block: find nodes whose text matches the date/time pattern,
        // then look for a title heading and ticket link in the same or following sibling.
        var allNodes = doc.DocumentNode
            .SelectNodes("//*[contains(text(),'|')]")
            ?? Enumerable.Empty<HtmlNode>();

        foreach (var node in allNodes)
        {
            var text  = node.InnerText.Replace("\n", " ").Trim();
            var match = ScreeningLineRegex.Match(text);
            if (!match.Success) continue;

            var day   = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (month < DateTime.Today.Month) year++;
            var date  = new DateOnly(year, month, day);
            var time  = ParseTime(match.Groups[3].Value);
            if (time == null) continue;

            // Title: look for the nearest heading sibling or parent heading
            HtmlNode? titleNode = node.SelectSingleNode("following-sibling::h2")
                               ?? node.SelectSingleNode("following-sibling::h3")
                               ?? node.ParentNode?.SelectSingleNode(".//h2")
                               ?? node.ParentNode?.SelectSingleNode(".//h3");
            if (titleNode == null) continue;

            var title = HtmlEntity.DeEntitize(titleNode.InnerText.Trim());

            // Ticket link: /cs/koupit/[slug]-[ID]
            var ticketNode = node.SelectSingleNode("following-sibling::a[contains(@href,'/cs/koupit/')]")
                          ?? node.ParentNode?.SelectSingleNode(".//a[contains(@href,'/cs/koupit/')]");
            var ticketUrl = ticketNode != null
                ? $"https://kinokavalirka.cz{ticketNode.GetAttributeValue("href", "")}"
                : null;

            var showtime = new Showtime
            {
                StartAt          = time.Value,
                TicketsAvailable = ticketUrl != null,
                TicketUrl        = ticketUrl
            };

            schedules.Add(BuildSchedule(date, 0, title, null, Config.VenueId, [showtime]));
        }

        Logger.LogInformation("[KinoKavalírka] Scraped {Count} schedule(s)", schedules.Count);
        return new VenueScrapeResult(venue, schedules, []);
    }
}
