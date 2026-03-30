using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Premiere Cinemas Hostivař — Švehlova 1391/32, Praha 10. Program: https://www.premierecinemas.cz
///
/// Server-rendered tabbed program. Structure:
///   Date tabs: anchor #tab-N with label "Pondělí 30. 3."
///   Per-tab table rows: film title | age | version | [HH:MM links to /vstupenky/{id}/]
/// </summary>
public class PremiereCinemasScraper : VenueScraperBase
{
    private static readonly Uri ProgramUri = new("https://www.premierecinemas.cz");
    private static readonly Regex TabDateRegex = new(@"(\d{1,2})\.\s*(\d{1,2})\.", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"\b\d{1,2}:\d{2}\b", RegexOptions.Compiled);

    private static readonly VenueConfig Config = new(
        VenueId: 0,  // TODO: replace with CSFD venue ID
        Name: "Premiere Cinemas Hostivař",
        City: "Praha",
        BaseUrl: ProgramUri);

    public PremiereCinemasScraper(IHtmlFetcher fetcher, ILogger<PremiereCinemasScraper> logger)
        : base(fetcher, logger) { }

    public override int VenueId => Config.VenueId;

    public override async Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("[PremiereCinemas] Fetching {Url}", ProgramUri);
        var doc = await FetchDocumentAsync(ProgramUri, cancellationToken);

        var venue = new Venue { Id = Config.VenueId, Name = Config.Name, City = Config.City };
        var schedules = new List<Schedule>();

        // Tabs: <li role="tab" ...><a href="#tab-N">Pondělí 30. 3.</a></li>
        // Content panes: <div id="tab-N" role="tabpanel">
        var year = DateTime.Today.Year;

        var tabLinks = doc.DocumentNode.SelectNodes("//ul[contains(@class,'cmsTabs')]//a[@href]");
        if (tabLinks == null) return new VenueScrapeResult(venue, schedules, []);

        foreach (var tabLink in tabLinks)
        {
            var tabId = tabLink.GetAttributeValue("href", "").TrimStart('#');
            var tabLabel = tabLink.InnerText.Trim();
            var dateMatch = TabDateRegex.Match(tabLabel);
            if (!dateMatch.Success) continue;

            var day   = int.Parse(dateMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(dateMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            if (month < DateTime.Today.Month) year++;           // wrap year
            var date = new DateOnly(year, month, day);

            var pane = doc.DocumentNode.SelectSingleNode($"//div[@id='{tabId}']");
            if (pane == null) continue;

            // Each row: <tr> <td class="...filmTitle"><a href="/filmy/...">Title</a></td> ... <td>HH:MM links</td> </tr>
            var rows = pane.SelectNodes(".//table//tr[td]");
            if (rows == null) continue;

            foreach (var row in rows)
            {
                var titleNode = row.SelectSingleNode(".//a[contains(@href,'/filmy/')]");
                if (titleNode == null) continue;
                var title = HtmlEntity.DeEntitize(titleNode.InnerText.Trim());

                var timeNodes = row.SelectNodes(".//a[contains(@href,'/vstupenky/')]");
                if (timeNodes == null) continue;

                var showtimes = new List<Showtime>();
                foreach (var t in timeNodes)
                {
                    var timeText = t.InnerText.Trim();
                    var time = ParseTime(timeText);
                    if (time == null) continue;

                    var ticketId = t.GetAttributeValue("href", "")
                        .Trim('/').Split('/').LastOrDefault();

                    showtimes.Add(new Showtime
                    {
                        StartAt          = time.Value,
                        TicketsAvailable = true,
                        TicketUrl        = ticketId != null
                            ? $"https://www.premierecinemas.cz/vstupenky/{ticketId}/"
                            : null
                    });
                }

                if (showtimes.Count == 0) continue;
                schedules.Add(BuildSchedule(date, 0, title, null, Config.VenueId, showtimes));
            }
        }

        Logger.LogInformation("[PremiereCinemas] Scraped {Count} schedule(s)", schedules.Count);
        return new VenueScrapeResult(venue, schedules, []);
    }
}
