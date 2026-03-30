using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Kino Radotín — Na Výšince 875/4, Praha 16. Program: https://www.kinoradotin.cz
///
/// CinemAware CMS. Structure:
///   Date headers: abbreviated Czech date "St1.4.", "Čt2.4." (day abbrev + DD.M.)
///   Movie link: &lt;a href="/event/[ID]/[slug]"&gt;Title&lt;/a&gt;
///   Format text: "2D OR 15", "2D ČD MP" etc.
///   Showtime link: &lt;a href="https://system.cinemaware.eu/wstep1.php?id=[hash]"&gt;HH:MM&lt;/a&gt;
/// </summary>
public class KinoRadotinScraper : VenueScraperBase
{
    private static readonly Uri ProgramUri = new("https://www.kinoradotin.cz");
    // "St1.4." / "Čt2.4." — abbreviated day name + DD.M.
    private static readonly Regex DateHeaderRegex = new(@"^[A-Za-zÁ-ža-ž]{2,3}(\d{1,2})\.(\d{1,2})\.", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"^\d{1,2}:\d{2}$", RegexOptions.Compiled);

    private static readonly VenueConfig Config = new(
        VenueId: 0,  // TODO: replace with CSFD venue ID
        Name: "Kino Radotín",
        City: "Praha",
        BaseUrl: ProgramUri);

    public KinoRadotinScraper(IHtmlFetcher fetcher, ILogger<KinoRadotinScraper> logger)
        : base(fetcher, logger) { }

    public override int VenueId => Config.VenueId;

    public override async Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("[KinoRadotín] Fetching {Url}", ProgramUri);
        var doc = await FetchDocumentAsync(ProgramUri, cancellationToken);

        var venue = new Venue { Id = Config.VenueId, Name = Config.Name, City = Config.City };
        var schedules = new List<Schedule>();
        var year = DateTime.Today.Year;
        DateOnly? currentDate = null;

        foreach (var node in doc.DocumentNode.DescendantsAndSelf())
        {
            if (node.NodeType != HtmlNodeType.Element) continue;

            var text = node.InnerText.Trim();

            // Date header detection: short text matching abbreviated Czech date
            if (node.Name is "h2" or "h3" or "strong" or "b" && text.Length < 20)
            {
                var m = DateHeaderRegex.Match(text);
                if (m.Success)
                {
                    var d = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    var mo = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (mo < DateTime.Today.Month) year++;
                    currentDate = new DateOnly(year, mo, d);
                    continue;
                }
            }

            if (currentDate == null) continue;

            // Movie link: /event/[ID]/[slug]
            var movieLink = node.SelectSingleNode(".//a[starts-with(@href,'/event/')]");
            if (movieLink == null) continue;

            var title    = HtmlEntity.DeEntitize(movieLink.InnerText.Trim());
            var movieUrl = $"https://www.kinoradotin.cz{movieLink.GetAttributeValue("href", "")}";

            // Showtime links: cinemaware.eu/wstep1.php?id=...
            var timeLinks = node.SelectNodes(".//a[contains(@href,'cinemaware.eu')]");
            if (timeLinks == null) continue;

            var showtimes = new List<Showtime>();
            foreach (var tl in timeLinks)
            {
                var timeText = tl.InnerText.Trim();
                if (!TimeRegex.IsMatch(timeText)) continue;
                var time = ParseTime(timeText);
                if (time == null) continue;

                showtimes.Add(new Showtime
                {
                    StartAt          = time.Value,
                    TicketsAvailable = true,
                    TicketUrl        = tl.GetAttributeValue("href", null)
                });
            }

            if (showtimes.Count == 0) continue;
            schedules.Add(BuildSchedule(currentDate.Value, 0, title, movieUrl, Config.VenueId, showtimes));
        }

        Logger.LogInformation("[KinoRadotín] Scraped {Count} schedule(s)", schedules.Count);
        return new VenueScrapeResult(venue, schedules, []);
    }
}
