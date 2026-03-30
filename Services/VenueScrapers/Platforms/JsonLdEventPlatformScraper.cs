using System.Text.Json;
using System.Text.Json.Nodes;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Platforms;

/// <summary>
/// Shared platform scraper for venues running the same CMS:
///   Bio Oko, Kino Aero, Kino Světozor, Kino Lucerna, Kino Přítomnost, Edison Filmhub.
///
/// All these sites embed schema.org <c>Event</c> JSON-LD blocks per screening and use
/// date anchors in the form <c>#program-day-DD-MM-YYYY</c>.
///
/// Usage: derive a thin subclass that passes a <see cref="VenueConfig"/> to the base ctor.
/// </summary>
public abstract class JsonLdEventPlatformScraper : VenueScraperBase
{
    private readonly VenueConfig _config;

    protected JsonLdEventPlatformScraper(VenueConfig config, IHtmlFetcher htmlFetcher, ILogger logger)
        : base(htmlFetcher, logger)
    {
        _config = config;
    }

    public override int VenueId => _config.VenueId;

    public override async Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("[{Venue}] Fetching {Url}", _config.Name, _config.BaseUrl);

        var doc = await FetchDocumentAsync(_config.BaseUrl, cancellationToken);

        var venue = new Venue
        {
            Id   = _config.VenueId,
            Name = _config.Name,
            City = _config.City
        };

        var events = ParseJsonLdEvents(doc);
        var schedules = BuildSchedules(events);

        Logger.LogInformation("[{Venue}] Parsed {Count} event(s) → {Sched} schedule(s)",
            _config.Name, events.Count, schedules.Count);

        return new VenueScrapeResult(venue, schedules, []);
    }

    // ── JSON-LD parsing ────────────────────────────────────────────────────────

    private List<JsonLdEvent> ParseJsonLdEvents(HtmlDocument doc)
    {
        var results = new List<JsonLdEvent>();

        var scriptNodes = doc.DocumentNode
            .SelectNodes("//script[@type='application/ld+json']");

        if (scriptNodes == null) return results;

        foreach (var node in scriptNodes)
        {
            try
            {
                var json = JsonNode.Parse(node.InnerText);
                if (json == null) continue;

                // Handle both a single Event object and a @graph array.
                if (json["@graph"] is JsonArray graph)
                {
                    foreach (var item in graph)
                        TryAddEvent(item, results);
                }
                else
                {
                    TryAddEvent(json, results);
                }
            }
            catch (JsonException)
            {
                // Malformed block — skip
            }
        }

        return results;
    }

    private static void TryAddEvent(JsonNode? node, List<JsonLdEvent> results)
    {
        if (node == null) return;
        if (node["@type"]?.GetValue<string>() is not ("Event" or "ScreeningEvent")) return;

        var startRaw = ReadStringOrFirstArray(node["startDate"]);
        if (startRaw == null) return;

        if (!DateTimeOffset.TryParse(startRaw, out var start)) return;

        var e = new JsonLdEvent
        {
            Name      = node["name"]?.GetValue<string>() ?? string.Empty,
            StartDate = start,
            Director  = node["director"]?.GetValue<string>(),
            Duration  = node["duration"]?.GetValue<string>(),
            Language  = node["inLanguage"]?.GetValue<string>(),
            Hall      = node["location"]?["name"]?.GetValue<string>(),
            TicketUrl = node["url"]?.GetValue<string>()
                     ?? node["offers"]?["url"]?.GetValue<string>()
        };

        results.Add(e);
    }

    // startDate is a plain string on some sites and an array ["..."] on others (Edison).
    private static string? ReadStringOrFirstArray(JsonNode? node)
    {
        if (node is JsonValue v) return v.GetValue<string>();
        if (node is JsonArray a && a.Count > 0) return a[0]?.GetValue<string>();
        return null;
    }

    // ── Schedule construction ─────────────────────────────────────────────────

    private List<Schedule> BuildSchedules(List<JsonLdEvent> events)
    {
        // Group by (date, title) — each distinct title on a date becomes one Schedule
        // with one Performance containing one Showtime.
        var byDateTitle = events
            .GroupBy(e => (e.StartDate.Date, e.Name));

        var schedules = new List<Schedule>();

        foreach (var group in byDateTitle)
        {
            var first = group.First();
            // No CSFD ID available from these sites — use 0 as placeholder.
            // MovieCollectorService will match by title later.
            var movieId = 0;

            var date = DateOnly.FromDateTime(first.StartDate.LocalDateTime);

            var performance = new Performance
            {
                MovieId    = movieId,
                MovieTitle = first.Name,
                MovieUrl   = first.TicketUrl,
                VenueId    = _config.VenueId
            };

            foreach (var ev in group.OrderBy(e => e.StartDate))
            {
                performance.Showtimes.Add(new Showtime
                {
                    StartAt          = TimeOnly.FromDateTime(ev.StartDate.LocalDateTime),
                    TicketsAvailable = true,
                    TicketUrl        = ev.TicketUrl
                });
            }

            schedules.Add(new Schedule
            {
                Date        = date,
                MovieId     = movieId,
                MovieTitle  = first.Name,
                StoredAt    = DateTime.UtcNow,
                Performances = { performance }
            });
        }

        return schedules;
    }

    // ── Internal DTO ─────────────────────────────────────────────────────────

    protected record JsonLdEvent
    {
        public string Name      { get; init; } = string.Empty;
        public DateTimeOffset StartDate { get; init; }
        public string? Director  { get; init; }
        public string? Duration  { get; init; }
        public string? Language  { get; init; }
        public string? Hall      { get; init; }
        public string? TicketUrl { get; init; }
    }
}
