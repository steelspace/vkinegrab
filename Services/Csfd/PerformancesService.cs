using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using vkinegrab.Models;

namespace vkinegrab.Services.Csfd;

public class PerformancesService : IPerformancesService
{
    private static readonly Uri DefaultBaseUri = new("https://www.csfd.cz/");
    private static readonly Regex CinemaIdRegex = new("cinema-(\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FilmIdRegex = new("/film/(\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DateRegex = new("\\b\\d{1,2}\\.\\d{1,2}\\.\\d{4}\\b", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new("\\b\\d{1,2}:\\d{2}\\b", RegexOptions.Compiled);
    private static readonly string[] TimeFormats = new[] { "H:mm", "HH:mm" };

    private readonly HttpClient httpClient;
    private readonly Uri baseUri;

    public PerformancesService(HttpClient? httpClient = null, Uri? baseUri = null)
    {
        this.baseUri = baseUri ?? DefaultBaseUri;
        this.httpClient = httpClient ?? CreateDefaultClient(this.baseUri);
        if (this.httpClient.BaseAddress is null)
        {
            this.httpClient.BaseAddress = this.baseUri;
        }
    }

    public async Task<IReadOnlyList<Schedule>> GetSchedules(
        Uri? pageUri = null,
        string period = "today",
        CancellationToken cancellationToken = default)
    {
        var (schedules, _) = await GetSchedulesWithVenues(pageUri, period, cancellationToken).ConfigureAwait(false);
        return schedules;
    }

    public async Task<(IReadOnlyList<Schedule> Schedules, IReadOnlyList<Venue> Venues)> GetSchedulesWithVenues(
        Uri? pageUri = null,
        string period = "today",
        CancellationToken cancellationToken = default)
    {
        pageUri ??= new Uri(baseUri, "/kino/1-praha/?period=all");
        var requestUri = AppendPeriod(pageUri, period);
        var html = await FetchHtml(requestUri, cancellationToken).ConfigureAwait(false);
        return ParseSchedulesAndVenues(html, requestUri);
    }

    private static HttpClient CreateDefaultClient(Uri baseUri)
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = baseUri
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "cs-CZ,cs;q=0.9,en;q=0.8");
        return client;
    }

    private static Uri AppendPeriod(Uri uri, string? period)
    {
        if (string.IsNullOrWhiteSpace(period))
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        var existingQuery = builder.Query.TrimStart('?');
        var pairs = existingQuery.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                 .Where(p => !p.StartsWith("period=", StringComparison.OrdinalIgnoreCase));
        var query = string.Join("&", pairs);
        if (query.Length > 0)
        {
            query += "&";
        }
        query += $"period={Uri.EscapeDataString(period)}";
        builder.Query = query;
        return builder.Uri;
    }

    private async Task<string> FetchHtml(Uri requestUri, CancellationToken cancellationToken)
    {
        if (!requestUri.IsAbsoluteUri)
        {
            requestUri = new Uri(httpClient.BaseAddress ?? baseUri, requestUri);
        }

        return await httpClient.GetStringAsync(requestUri, cancellationToken).ConfigureAwait(false);
    }

    // Parse schedules and return any discovered venue metadata from the same page (best-effort)
    private (IReadOnlyList<Schedule> Schedules, IReadOnlyList<Venue> Venues) ParseSchedulesAndVenues(string html, Uri requestUri)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var sections = doc.DocumentNode.SelectNodes("//section[contains(@class,'updated-box-cinema')]");
        if (sections is null || sections.Count == 0)
        {
            return (Array.Empty<Schedule>(), Array.Empty<Venue>());
        }

        var schedules = new Dictionary<(DateOnly date, int movieId), Schedule>();
        var venues = new Dictionary<int, Venue>();

        foreach (var section in sections)
        {
            var idValue = section.GetAttributeValue("id", string.Empty);
            var cinemaId = ExtractInt(CinemaIdRegex, idValue);

            // Prefer slugged /kino/ link when possible, otherwise fallback to any /kino/ link
            var venueLink = section.SelectSingleNode(".//a[contains(@href,'/kino/') and contains(@href,'-')]")
                            ?? section.SelectSingleNode(".//a[contains(@href,'/kino/')]");
            var venueUrl = venueLink != null ? ToAbsoluteUrl(venueLink.GetAttributeValue("href", string.Empty), requestUri) : null;

            // If the venue link contains an explicit numeric ID (e.g. /kino/1-praha/110-slug/), prefer it over the section id
            if (!string.IsNullOrWhiteSpace(venueUrl))
            {
                var urlMatch = Regex.Match(venueUrl, "/kino/(?:[^/]+/)?(\\d+)", RegexOptions.IgnoreCase);
                if (urlMatch.Success && int.TryParse(urlMatch.Groups[1].Value, out var urlCinemaId))
                {
                    cinemaId = urlCinemaId;
                }
            }

            // Extract venue metadata from the section (best-effort)
            if (cinemaId > 0 && !venues.ContainsKey(cinemaId))
            {
                var venue = new Venue { Id = cinemaId };
                venue.DetailUrl = venueUrl;

                // Name heuristics
                var name = venueLink != null ? Clean(venueLink.InnerText) : null;
                if (string.IsNullOrWhiteSpace(name))
                {
                    var header = section.SelectSingleNode(".//h3") ?? section.SelectSingleNode(".//h2") ?? section.SelectSingleNode(".//h4");
                    name = Clean(header?.InnerText);
                }
                venue.Name = name;

                // Address heuristics
                var addressNode = section.SelectSingleNode(".//*[contains(@class,'address')]") ?? section.SelectSingleNode(".//address");
                if (addressNode != null)
                {
                    venue.Address = Clean(addressNode.InnerText);
                }
                else
                {
                    // Look for surrounding text near the map link
                    var mapAnchor = section.SelectSingleNode(".//a[contains(@href,'google.com') or contains(@href,'mapy.cz') or contains(@href,'maps')]");
                    if (mapAnchor != null)
                    {
                        var parentText = mapAnchor.ParentNode?.InnerText;
                        if (!string.IsNullOrWhiteSpace(parentText)) venue.Address = Clean(parentText);
                    }
                }

                // Map URL
                var mapAnchor2 = section.SelectSingleNode(".//a[contains(@href,'google.com') or contains(@href,'mapy.cz') or contains(@href,'maps')]");
                if (mapAnchor2 != null)
                {
                    var href = mapAnchor2.GetAttributeValue("href", string.Empty);
                    if (!string.IsNullOrWhiteSpace(href)) venue.MapUrl = href.StartsWith("//") ? "https:" + href : href;
                }

                // City: try extract from URL slug (e.g. /kino/1-praha/)
                if (!string.IsNullOrWhiteSpace(venueUrl))
                {
                    var m = Regex.Match(venueUrl, "/kino/([^/]+)/", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var citySlug = m.Groups[1].Value;
                        // Remove numeric prefix (e.g. '1-praha' -> 'praha') then replace hyphens
                        var city = Regex.Replace(citySlug, "^\\d+-", "");
                        city = city.Replace('-', ' ').Trim();
                        venue.City = Clean(city);
                    }
                }

                // Only store venues with at least an ID
                venues[cinemaId] = venue;
            }

            var subHeader = section.SelectSingleNode(".//div[contains(@class,'update-box-sub-header')]");
            var dateText = subHeader?.ChildNodes
                                     .Where(n => n.NodeType == HtmlNodeType.Text)
                                     .Select(n => n.InnerText)
                                     .FirstOrDefault();
            var scheduleDate = ParseDate(dateText);
            var showDate = scheduleDate ?? DateOnly.FromDateTime(DateTime.Today);

            var rows = section.SelectNodes(".//table[contains(@class,'cinema-table')]//tr");
            if (rows == null)
            {
                continue;
            }

            foreach (var row in rows)
            {
                var performance = ParsePerformance(row, showDate, requestUri);
                if (performance == null || performance.Showtimes.Count == 0)
                {
                    continue;
                }

                performance.VenueId = cinemaId;
                performance.VenueUrl = venueUrl;

                var key = (showDate, performance.MovieId);
                if (!schedules.TryGetValue(key, out var schedule))
                {
                    schedule = new Schedule
                    {
                        Date = showDate,
                        MovieId = performance.MovieId,
                        MovieTitle = performance.MovieTitle
                    };
                    schedules[key] = schedule;
                }

                var existing = schedule.Performances.FirstOrDefault(p => p.VenueId == performance.VenueId);
                if (existing != null)
                {
                    // merge showtimes uniquely
                    foreach (var st in performance.Showtimes)
                    {
                        if (!existing.Showtimes.Any(s => s.StartAt == st.StartAt))
                        {
                            existing.Showtimes.Add(st);
                        }
                    }

                    // merge badges uniquely
                    foreach (var badge in performance.Badges)
                    {
                        if (!existing.Badges.Any(b => b.Kind == badge.Kind && b.Code == badge.Code))
                        {
                            existing.Badges.Add(badge);
                        }
                    }
                }
                else
                {
                    schedule.Performances.Add(performance);
                }
            }
        }

        var ordered = schedules.Values.OrderBy(s => s.Date).ThenBy(s => s.MovieId).ToList();
        return (ordered, venues.Values.OrderBy(v => v.Id).ToList());
    }


    private Performance? ParsePerformance(HtmlNode row, DateOnly date, Uri requestUri)
    {
        var movieLink = row.SelectSingleNode(".//td[contains(@class,'name')]//a[contains(@href,'/film/')]");
        if (movieLink == null)
        {
            return null;
        }

        var movieTitle = Clean(movieLink.InnerText);
        var movieUrl = ToAbsoluteUrl(movieLink.GetAttributeValue("href", string.Empty), requestUri);
        var movieId = ExtractInt(FilmIdRegex, movieUrl);

        var performance = new Performance
        {
            MovieId = movieId,
            MovieTitle = movieTitle ?? string.Empty,
            MovieUrl = movieUrl
        };

        foreach (var badge in ExtractHallBadges(row))
        {
            performance.Badges.Add(badge);
        }

        foreach (var badge in ExtractFormatBadges(row))
        {
            performance.Badges.Add(badge);
        }

        var showtimes = ExtractShowtimes(row, date, requestUri);
        foreach (var showtime in showtimes)
        {
            if (performance.Showtimes.All(s => s.StartAt != showtime.StartAt))
            {
                performance.Showtimes.Add(showtime);
            }
        }

        return performance.Showtimes.Count > 0 ? performance : null;
    }

    private IEnumerable<CinemaBadge> ExtractHallBadges(HtmlNode row)
    {
        var hallSpans = row.SelectNodes(".//td[contains(@class,'name')]//span[contains(@class,'cinema-icon')]");
        if (hallSpans == null)
        {
            yield break;
        }

        foreach (var span in hallSpans)
        {
            var code = Clean(span.InnerText)?.TrimEnd(',') ?? string.Empty;
            var description = Clean(span.GetAttributeValue("data-tippy-content", string.Empty)) ?? code;
            yield return new CinemaBadge
            {
                Kind = BadgeKind.Hall,
                Code = code,
                Description = description
            };
        }
    }

    private IEnumerable<CinemaBadge> ExtractFormatBadges(HtmlNode row)
    {
        var formatSpans = row.SelectNodes(".//td[contains(@class,'td-title')]//span");
        if (formatSpans == null)
        {
            yield break;
        }

        foreach (var span in formatSpans)
        {
            var code = Clean(span.InnerText)?.TrimEnd(',') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var description = Clean(span.GetAttributeValue("title", string.Empty));
            yield return new CinemaBadge
            {
                Kind = BadgeKind.Format,
                Code = code,
                Description = description
            };
        }
    }

    private IEnumerable<Showtime> ExtractShowtimes(HtmlNode row, DateOnly date, Uri requestUri)
    {
        var cells = row.SelectNodes(".//td[contains(@class,'td-time')]");
        if (cells == null)
        {
            yield break;
        }

        var seen = new HashSet<DateTime>();
        foreach (var cell in cells)
        {
            var classValue = cell.GetAttributeValue("class", string.Empty);
            var classParts = classValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var isPast = classParts.Any(c => c.Equals("td-time-old", StringComparison.OrdinalIgnoreCase));
            var hasTicketClass = classParts.Any(c => c.Equals("td-buy-ticket", StringComparison.OrdinalIgnoreCase));
            var anchors = cell.SelectNodes(".//a");
            if (anchors != null)
            {
                foreach (var anchor in anchors)
                {
                    var timeText = Clean(anchor.InnerText);
                    if (!TryParseTime(timeText, out var time))
                    {
                        continue;
                    }

                    var start = date.ToDateTime(time);
                    if (seen.Add(start))
                    {
                        yield return new Showtime
                        {
                            StartAt = start,
                            TicketsAvailable = true,
                            TicketUrl = ToAbsoluteUrl(anchor.GetAttributeValue("href", string.Empty), requestUri),
                        };
                    }
                }
            }

            var rawText = HtmlEntity.DeEntitize(cell.InnerText ?? string.Empty);
            foreach (Match match in TimeRegex.Matches(rawText))
            {
                if (!TryParseTime(match.Value, out var time))
                {
                    continue;
                }

                var start = date.ToDateTime(time);
                if (seen.Add(start))
                {
                    yield return new Showtime
                    {
                        StartAt = start,
                        TicketsAvailable = hasTicketClass,
                        TicketUrl = hasTicketClass ? ToAbsoluteUrl(cell.SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty), requestUri) : null,
                    };
                }
            }
        }
    }

    private static DateOnly? ParseDate(string? text)
    {
        var cleaned = Clean(text);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        var match = DateRegex.Match(cleaned);
        if (!match.Success)
        {
            return null;
        }

        if (DateTime.TryParseExact(match.Value, "d.M.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
        {
            return DateOnly.FromDateTime(dateTime);
        }

        return null;
    }

    private static int ExtractInt(Regex regex, string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return 0;
        }

        var match = regex.Match(input);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
        {
            return value;
        }

        return 0;
    }

    private static bool TryParseTime(string? text, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim();
        return TimeOnly.TryParseExact(candidate, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    private static string? ToAbsoluteUrl(string? href, Uri requestUri)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            // Only accept explicit http(s) absolute URIs. A leading slash on Unix-like systems
            // can be parsed as an absolute file path (file:///...), which we should treat as a
            // relative CSFD path instead.
            if (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
            {
                return absolute.ToString();
            }
        }

        // protocol-relative URLs (e.g. //img.csfd...)
        if (href.StartsWith("//", StringComparison.Ordinal))
        {
            // If requestUri has no scheme or is a file URI, fall back to https
            var scheme = (requestUri?.IsAbsoluteUri == true && requestUri.Scheme != Uri.UriSchemeFile) ? requestUri.Scheme : "https";
            return scheme + ":" + href;
        }

        // If requestUri is not absolute or is a file:// URI (e.g. when base was incorrect), fall back to the canonical CSFD base
        var baseUri = (requestUri?.IsAbsoluteUri == true && requestUri.Scheme != Uri.UriSchemeFile) ? requestUri : DefaultBaseUri;

        if (!baseUri.ToString().EndsWith("/", StringComparison.Ordinal))
        {
            baseUri = new Uri(baseUri, ".");
        }

        return new Uri(baseUri, href).ToString();
    }

    private static string? Clean(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var decoded = HtmlEntity.DeEntitize(input).Replace('\u00A0', ' ');
        decoded = Regex.Replace(decoded, "\\s+", " ");
        return decoded.Trim();
    }
}
