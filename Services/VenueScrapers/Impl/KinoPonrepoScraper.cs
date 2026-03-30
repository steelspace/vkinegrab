using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Kino Ponrepo (NFA) — Bartolomějská 11, Praha 1. Program: https://nfa.cz/cs/kino-ponrepo/program/program
///
/// Server-rendered semantic HTML. Structure:
///   Date header: &lt;h2 id="YYYY-MM-DD"&gt;Den D/M&lt;/h2&gt;
///   Per-screening:
///     &lt;h3&gt;HH:MM&lt;/h3&gt;
///     &lt;h3&gt;&lt;a href="/cs/[ID]-[slug]?screening=[sid]"&gt;Czech Title / Original Title&lt;/a&gt;&lt;/h3&gt;
///     Metadata text: "Country Year / language / format"
///     GoOut ticket link
/// </summary>
public class KinoPonrepoScraper : VenueScraperBase
{
    private static readonly Uri ProgramUri = new("https://nfa.cz/cs/kino-ponrepo/program/program");
    private static readonly Regex DateIdRegex = new(@"^(\d{4})-(\d{2})-(\d{2})$", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"^\d{1,2}:\d{2}$", RegexOptions.Compiled);
    private static readonly Regex FilmIdFromPath = new(@"/cs/(\d+)-", RegexOptions.Compiled);

    private static readonly VenueConfig Config = new(
        VenueId: 0,  // TODO: replace with CSFD venue ID
        Name: "Kino Ponrepo",
        City: "Praha",
        BaseUrl: ProgramUri);

    public KinoPonrepoScraper(IHtmlFetcher fetcher, ILogger<KinoPonrepoScraper> logger)
        : base(fetcher, logger) { }

    public override int VenueId => Config.VenueId;

    public override async Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("[KinoPonrepo] Fetching {Url}", ProgramUri);
        var doc = await FetchDocumentAsync(ProgramUri, cancellationToken);

        var venue = new Venue { Id = Config.VenueId, Name = Config.Name, City = Config.City };
        var schedules = new List<Schedule>();

        DateOnly? currentDate = null;

        // Walk all <h2> and <h3> in document order.
        var headers = doc.DocumentNode.SelectNodes("//h2[@id] | //h3");
        if (headers == null) return new VenueScrapeResult(venue, schedules, []);

        string? pendingTime = null;

        foreach (var node in headers)
        {
            if (node.Name == "h2")
            {
                var id = node.GetAttributeValue("id", "");
                var m  = DateIdRegex.Match(id);
                if (m.Success)
                {
                    currentDate = new DateOnly(
                        int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                        int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                        int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
                    pendingTime = null;
                }
                continue;
            }

            if (currentDate == null) continue;

            var text = node.InnerText.Trim();

            // A bare time: "15:00"
            if (TimeRegex.IsMatch(text))
            {
                pendingTime = text;
                continue;
            }

            // A film title node: contains a link to /cs/[id]-[slug]
            var link = node.SelectSingleNode(".//a[contains(@href,'/cs/')]");
            if (link != null && pendingTime != null)
            {
                var time = ParseTime(pendingTime);
                if (time == null) { pendingTime = null; continue; }

                var href  = link.GetAttributeValue("href", "");
                var title = HtmlEntity.DeEntitize(link.InnerText.Trim());
                // "Czech Title / Original Title" — keep the Czech one
                if (title.Contains(" / "))
                    title = title.Split(" / ")[0].Trim();

                var showtime = new Showtime
                {
                    StartAt          = time.Value,
                    TicketsAvailable = true,
                    TicketUrl        = href.Contains("screening=")
                        ? $"https://nfa.cz{href}"
                        : null
                };

                schedules.Add(BuildSchedule(currentDate.Value, 0, title,
                    $"https://nfa.cz{href.Split('?')[0]}", Config.VenueId, [showtime]));

                pendingTime = null;
            }
        }

        Logger.LogInformation("[KinoPonrepo] Scraped {Count} schedule(s)", schedules.Count);
        return new VenueScrapeResult(venue, schedules, []);
    }
}
