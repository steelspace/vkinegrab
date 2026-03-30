using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Kino MAT — Karlovo náměstí 19, Praha 2. Program: https://www.mat.cz/kino/
///
/// Server-rendered. Structure:
///   Date header text: "dnes 30 březen" / "úterý 31 březen"
///   Movie link: &lt;a href="/kino/cz/kino-mat?movie-id=[ID]_[slug]"&gt;Title&lt;/a&gt;
///   Time: dot-separated "16.00" (NOT colon — converted to HH:MM internally)
///   Ticket: https://shop.entradio.cz/event/[ID]
/// </summary>
public class KinoMatScraper : VenueScraperBase
{
    private static readonly Uri ProgramUri = new("https://www.mat.cz/kino/");
    private static readonly Regex DotTimeRegex = new(@"\b(\d{1,2})\.(\d{2})\b", RegexOptions.Compiled);
    // Czech month names for date header parsing
    private static readonly Dictionary<string, int> CzechMonths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["leden"] = 1, ["únor"] = 2, ["březen"] = 3, ["duben"] = 4,
        ["květen"] = 5, ["červen"] = 6, ["červenec"] = 7, ["srpen"] = 8,
        ["září"] = 9, ["říjen"] = 10, ["listopad"] = 11, ["prosinec"] = 12
    };
    private static readonly Regex DateHeaderRegex = new(@"(\d{1,2})\s+(\w+)", RegexOptions.Compiled);

    private static readonly VenueConfig Config = new(
        VenueId: 0,  // TODO: replace with CSFD venue ID
        Name: "Kino MAT",
        City: "Praha",
        BaseUrl: ProgramUri);

    public KinoMatScraper(IHtmlFetcher fetcher, ILogger<KinoMatScraper> logger)
        : base(fetcher, logger) { }

    public override int VenueId => Config.VenueId;

    public override async Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("[KinoMAT] Fetching {Url}", ProgramUri);
        var doc = await FetchDocumentAsync(ProgramUri, cancellationToken);

        var venue = new Venue { Id = Config.VenueId, Name = Config.Name, City = Config.City };
        var schedules = new List<Schedule>();
        var year = DateTime.Today.Year;
        DateOnly? currentDate = null;

        // Walk the document looking for date headers and movie rows
        foreach (var node in doc.DocumentNode.DescendantsAndSelf())
        {
            if (node.NodeType != HtmlNodeType.Element) continue;

            // Date header: typically a <strong>, <b>, or dedicated class node with Czech date text
            var text = node.InnerText.Trim();

            if ((node.Name is "strong" or "b" or "h3" or "h4") && text.Length < 40)
            {
                if (text.Contains("dnes", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Dnes", StringComparison.CurrentCultureIgnoreCase))
                {
                    currentDate = DateOnly.FromDateTime(DateTime.Today);
                    continue;
                }
                var dm = DateHeaderRegex.Match(text);
                if (dm.Success && CzechMonths.TryGetValue(dm.Groups[2].Value, out var mo))
                {
                    var d = int.Parse(dm.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (mo < DateTime.Today.Month) year++;
                    currentDate = new DateOnly(year, mo, d);
                    continue;
                }
            }

            if (currentDate == null) continue;

            // Movie entry: link to ?movie-id=...
            var link = node.SelectSingleNode(".//a[contains(@href,'movie-id=')]");
            if (link == null) continue;

            var title = HtmlEntity.DeEntitize(link.InnerText.Trim());
            var href  = link.GetAttributeValue("href", "");

            // Time: dot-format "16.00" in a nearby <em> or text node
            var timeNode = node.SelectSingleNode(".//em");
            if (timeNode == null) continue;
            var timeMatch = DotTimeRegex.Match(timeNode.InnerText.Trim());
            if (!timeMatch.Success) continue;

            var h = int.Parse(timeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(timeMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var time = new TimeOnly(h, m);

            // Ticket link
            var ticketNode = node.SelectSingleNode(".//a[contains(@href,'entradio.cz')]");
            var ticketUrl  = ticketNode?.GetAttributeValue("href", null);

            schedules.Add(BuildSchedule(currentDate.Value, 0, title,
                $"https://www.mat.cz{href}", Config.VenueId,
                [new Showtime { StartAt = time, TicketsAvailable = ticketUrl != null, TicketUrl = ticketUrl }]));
        }

        Logger.LogInformation("[KinoMAT] Scraped {Count} schedule(s)", schedules.Count);
        return new VenueScrapeResult(venue, schedules, []);
    }
}
