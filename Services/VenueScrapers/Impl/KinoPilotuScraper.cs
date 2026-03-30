using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Kino Pilotů — Donská 168/19, Praha 10. Program: https://kinopilotu.cz
///
/// Server-rendered definition list structure:
///   &lt;dt&gt;&lt;strong&gt;Pondělí&lt;/strong&gt; 30. března&lt;/dt&gt;
///   &lt;dd&gt;
///     &lt;li&gt;
///       &lt;em&gt;HH:MM&lt;/em&gt;
///       &lt;strong&gt;&lt;a href="/detail/[ID]"&gt;Title&lt;/a&gt;&lt;/strong&gt;
///       &lt;a href="https://shop.entradio.cz/event/[ID]"&gt;Price Kč&lt;/a&gt;
///     &lt;/li&gt;
///   &lt;/dd&gt;
/// </summary>
public class KinoPilotuScraper : VenueScraperBase
{
    private static readonly Uri ProgramUri = new("https://kinopilotu.cz");
    // Czech month names → month number
    private static readonly Dictionary<string, int> CzechMonths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ledna"] = 1, ["února"] = 2, ["března"] = 3, ["dubna"] = 4,
        ["května"] = 5, ["června"] = 6, ["července"] = 7, ["srpna"] = 8,
        ["září"] = 9, ["října"] = 10, ["listopadu"] = 11, ["prosince"] = 12
    };
    private static readonly Regex DtDateRegex = new(@"(\d{1,2})\.\s*(\w+)", RegexOptions.Compiled);

    private static readonly VenueConfig Config = new(
        VenueId: 0,  // TODO: replace with CSFD venue ID
        Name: "Kino Pilotů",
        City: "Praha",
        BaseUrl: ProgramUri);

    public KinoPilotuScraper(IHtmlFetcher fetcher, ILogger<KinoPilotuScraper> logger)
        : base(fetcher, logger) { }

    public override int VenueId => Config.VenueId;

    public override async Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("[KinoPilotů] Fetching {Url}", ProgramUri);
        var doc = await FetchDocumentAsync(ProgramUri, cancellationToken);

        var venue = new Venue { Id = Config.VenueId, Name = Config.Name, City = Config.City };
        var schedules = new List<Schedule>();
        var year = DateTime.Today.Year;
        DateOnly? currentDate = null;

        // Walk <dt> and <dd> pairs
        var nodes = doc.DocumentNode.SelectNodes("//dt | //dd");
        if (nodes == null) return new VenueScrapeResult(venue, schedules, []);

        foreach (var node in nodes)
        {
            if (node.Name == "dt")
            {
                var text  = node.InnerText.Trim();
                var match = DtDateRegex.Match(text);
                if (!match.Success) continue;

                var day       = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var monthName = match.Groups[2].Value;
                if (!CzechMonths.TryGetValue(monthName, out var month)) continue;
                if (month < DateTime.Today.Month) year++;
                currentDate = new DateOnly(year, month, day);
                continue;
            }

            if (currentDate == null) continue;

            // <dd> contains <li> items
            var items = node.SelectNodes(".//li");
            if (items == null) continue;

            foreach (var item in items)
            {
                var timeText  = item.SelectSingleNode(".//em")?.InnerText.Trim();
                var titleNode = item.SelectSingleNode(".//strong/a[contains(@href,'/detail/')]");
                if (timeText == null || titleNode == null) continue;

                var time  = ParseTime(timeText);
                if (time == null) continue;

                var title = HtmlEntity.DeEntitize(titleNode.InnerText.Trim());
                var href  = titleNode.GetAttributeValue("href", null);

                var ticketNode = item.SelectSingleNode(".//a[contains(@href,'entradio.cz')]");
                var ticketUrl  = ticketNode?.GetAttributeValue("href", null);

                schedules.Add(BuildSchedule(currentDate.Value, 0, title,
                    href != null ? $"https://kinopilotu.cz{href}" : null,
                    Config.VenueId,
                    [new Showtime { StartAt = time.Value, TicketsAvailable = ticketUrl != null, TicketUrl = ticketUrl }]));
            }
        }

        Logger.LogInformation("[KinoPilotů] Scraped {Count} schedule(s)", schedules.Count);
        return new VenueScrapeResult(venue, schedules, []);
    }
}
