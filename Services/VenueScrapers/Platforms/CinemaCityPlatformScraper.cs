using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers.Platforms;

/// <summary>
/// Shared platform scraper for all Cinema City Prague locations.
/// Uses the Cinema City Quickbook REST API — no HTML parsing required.
///
/// API: GET https://www.cinemacity.cz/cz/data-api-service/v1/quickbook/10100/film-events/in-cinema/{cinemaId}/at-date/{YYYY-MM-DD}
///
/// Response: { body: { films: [...], events: [...] } }
///
/// Usage: derive a thin subclass supplying a <see cref="VenueConfig"/> and the
/// Cinema City <c>cinemaId</c> (e.g. 1033 for Slovanský dům).
/// </summary>
public abstract class CinemaCityPlatformScraper : IVenueScraper
{
    private const string BaseApiUrl =
        "https://www.cinemacity.cz/cz/data-api-service/v1/quickbook/10100/film-events/in-cinema/{0}/at-date/{1}";

    // Scrape this many days ahead (inclusive of today).
    private const int DaysAhead = 7;

    private readonly VenueConfig _config;
    private readonly int _cinemaCityId;
    private readonly IHttpClientFactory _httpClientFactory;
    protected readonly ILogger Logger;

    protected CinemaCityPlatformScraper(
        VenueConfig config,
        int cinemaCityId,
        IHttpClientFactory httpClientFactory,
        ILogger logger)
    {
        _config = config;
        _cinemaCityId = cinemaCityId;
        _httpClientFactory = httpClientFactory;
        Logger = logger;
    }

    public int VenueId => _config.VenueId;

    public async Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("[{Venue}] Fetching Cinema City API for cinema {Id}", _config.Name, _cinemaCityId);

        var venue = new Venue
        {
            Id   = _config.VenueId,
            Name = _config.Name,
            City = _config.City
        };

        var client = _httpClientFactory.CreateClient("Csfd");
        var allSchedules = new List<Schedule>();

        for (int day = 0; day < DaysAhead; day++)
        {
            var date = DateOnly.FromDateTime(DateTime.Today.AddDays(day));
            var url  = string.Format(BaseApiUrl, _cinemaCityId, date.ToString("yyyy-MM-dd"));

            ApiResponse? response = null;
            try
            {
                response = await client.GetFromJsonAsync<ApiResponse>(url, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[{Venue}] API call failed for {Date}: {Msg}", _config.Name, date, ex.Message);
                continue;
            }

            if (response?.Body == null) continue;

            var filmById = response.Body.Films
                .ToDictionary(f => f.Id, f => f);

            foreach (var ev in response.Body.Events)
            {
                if (!filmById.TryGetValue(ev.FilmId, out var film)) continue;

                var showtime = new Showtime
                {
                    StartAt          = TimeOnly.Parse(ev.EventDateTime[11..16]), // "HH:mm" from "YYYY-MM-DDTHH:MM:SS"
                    TicketsAvailable = !ev.SoldOut,
                    TicketUrl        = ev.BookingLink
                };

                if (!string.IsNullOrEmpty(ev.Auditorium))
                    showtime.Badges.Add(new CinemaBadge { Kind = BadgeKind.Hall, Code = ev.Auditorium });

                var schedule = allSchedules.FirstOrDefault(
                    s => s.Date == date && s.MovieTitle == film.Name);

                if (schedule == null)
                {
                    schedule = new Schedule
                    {
                        Date       = date,
                        MovieId    = 0, // No CSFD ID available — matched by title later
                        MovieTitle = film.Name,
                        StoredAt   = DateTime.UtcNow
                    };
                    allSchedules.Add(schedule);
                }

                var perf = schedule.Performances.FirstOrDefault(
                    p => p.VenueId == _config.VenueId
                      && p.Showtimes.Count > 0
                      && p.Showtimes[0].Badges.Select(b => b.Code).SequenceEqual(
                             showtime.Badges.Select(b => b.Code)));

                if (perf == null)
                {
                    perf = new Performance
                    {
                        MovieId    = 0,
                        MovieTitle = film.Name,
                        MovieUrl   = film.Link,
                        VenueId    = _config.VenueId
                    };
                    schedule.Performances.Add(perf);
                }

                perf.Showtimes.Add(showtime);
            }
        }

        Logger.LogInformation("[{Venue}] Scraped {Count} schedule(s)", _config.Name, allSchedules.Count);
        return new VenueScrapeResult(venue, allSchedules, []);
    }

    // ── API response model ────────────────────────────────────────────────────

    private record ApiResponse(
        [property: JsonPropertyName("body")] ApiBody? Body);

    private record ApiBody(
        [property: JsonPropertyName("films")]  List<ApiFilm>  Films,
        [property: JsonPropertyName("events")] List<ApiEvent> Events);

    private record ApiFilm(
        [property: JsonPropertyName("id")]          string Id,
        [property: JsonPropertyName("name")]        string Name,
        [property: JsonPropertyName("length")]      int?   Length,
        [property: JsonPropertyName("posterLink")]  string? PosterLink,
        [property: JsonPropertyName("link")]        string? Link);

    private record ApiEvent(
        [property: JsonPropertyName("id")]            string Id,
        [property: JsonPropertyName("filmId")]        string FilmId,
        [property: JsonPropertyName("cinemaId")]      string CinemaId,
        [property: JsonPropertyName("name")]          string Name,
        [property: JsonPropertyName("eventDateTime")] string EventDateTime,
        [property: JsonPropertyName("bookingLink")]   string? BookingLink,
        [property: JsonPropertyName("soldOut")]       bool SoldOut,
        [property: JsonPropertyName("auditorium")]    string? Auditorium);
}
