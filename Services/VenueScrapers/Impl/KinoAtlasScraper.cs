using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Kino Atlas — Ke Štvanici 371/4, Praha 8. Program: https://www.kinoatlaspraha.cz
///
/// Server-rendered chronological list. Structure per entry:
///   &lt;time&gt;HH:MM&lt;/time&gt;
///   Hall name (text node, one of: Velký sál / Modrý sál / Galerie)
///   &lt;h3&gt;&lt;a href="?program=[ID]"&gt;Title&lt;/a&gt;&lt;/h3&gt;
///   &lt;p&gt;Director / Country / Year&lt;/p&gt;
///   GoOut ticket link: https://goout.net/cs/listky/[slug]/[code]/
///
/// Dates: page sections headed by "Dnes", "Zítra", or "Den DD. M." text.
/// AJAX loads more via ajax_get_program.php — this scraper parses the initial load.
/// </summary>
public class KinoAtlasScraper : VenueScraperBase
{
    private static readonly Uri ProgramUri = new("https://www.kinoatlaspraha.cz");
    private static readonly Regex DateTextRegex = new(@"\b(\d{1,2})\.\s*(\d{1,2})\b", RegexOptions.Compiled);

    private static readonly VenueConfig Config = new(
        VenueId: 0,  // TODO: replace with CSFD venue ID
        Name: "Kino Atlas",
        City: "Praha",
        BaseUrl: ProgramUri);

    public KinoAtlasScraper(IHtmlFetcher fetcher, ILogger<KinoAtlasScraper> logger)
        : base(fetcher, logger) { }

    public override int VenueId => Config.VenueId;

    public override async Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("[KinoAtlas] Fetching {Url}", ProgramUri);
        var doc = await FetchDocumentAsync(ProgramUri, cancellationToken);

        var venue = new Venue { Id = Config.VenueId, Name = Config.Name, City = Config.City };
        var schedules = new List<Schedule>();

        // The page groups entries under date section headers.
        // Walk all nodes; when a date header is found, update currentDate.
        // When a program item is found, parse it against currentDate.

        DateOnly? currentDate = null;
        var year = DateTime.Today.Year;

        // Date headers: a node containing "Dnes", "Zítra" or "DD. M." pattern
        // Program entries: container with <time> + <h3><a href="?program=...">
        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;

        foreach (var node in body.DescendantsAndSelf())
        {
            if (node.NodeType != HtmlNodeType.Element) continue;

            // Date header detection
            var text = node.InnerText.Trim();
            if (node.Name is "h2" or "h3" or "div" && !node.HasChildNodes)
            {
                if (text.Equals("Dnes", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Dnes", StringComparison.CurrentCultureIgnoreCase))
                {
                    currentDate = DateOnly.FromDateTime(DateTime.Today);
                    continue;
                }
                if (text.Equals("Zítra", StringComparison.OrdinalIgnoreCase))
                {
                    currentDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
                    continue;
                }
                var dm = DateTextRegex.Match(text);
                if (dm.Success && text.Length < 20)
                {
                    var d = int.Parse(dm.Groups[1].Value, CultureInfo.InvariantCulture);
                    var m = int.Parse(dm.Groups[2].Value, CultureInfo.InvariantCulture);
                    currentDate = new DateOnly(year, m, d);
                    continue;
                }
            }

            if (currentDate == null) continue;

            // Program entry: must have <time> and <h3><a href="?program=...">
            var timeNode  = node.SelectSingleNode(".//time");
            var titleNode = node.SelectSingleNode(".//h3/a[contains(@href,'?program=')]");
            if (timeNode == null || titleNode == null) continue;

            var time  = ParseTime(timeNode.InnerText.Trim());
            if (time == null) continue;

            var title = HtmlEntity.DeEntitize(titleNode.InnerText.Trim());
            var href  = titleNode.GetAttributeValue("href", null);

            var showtime = new Showtime
            {
                StartAt          = time.Value,
                TicketsAvailable = node.SelectSingleNode(".//a[contains(@href,'goout.net')]") != null
            };

            schedules.Add(BuildSchedule(currentDate.Value, 0, title, href, Config.VenueId, [showtime]));
        }

        // Deduplicate: merge showtimes for same date+title
        schedules = MergeSchedules(schedules);

        Logger.LogInformation("[KinoAtlas] Scraped {Count} schedule(s)", schedules.Count);
        return new VenueScrapeResult(venue, schedules, []);
    }

    private static List<Schedule> MergeSchedules(List<Schedule> raw)
    {
        var merged = new Dictionary<(DateOnly, string), Schedule>();
        foreach (var s in raw)
        {
            var key = (s.Date, s.MovieTitle ?? string.Empty);
            if (!merged.TryGetValue(key, out var existing))
            {
                merged[key] = s;
            }
            else
            {
                existing.Performances[0].Showtimes.AddRange(s.Performances[0].Showtimes);
            }
        }
        return merged.Values.ToList();
    }
}
