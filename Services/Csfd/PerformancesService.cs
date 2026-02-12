using System;
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

    private readonly IHttpClientFactory httpClientFactory;
    private readonly Uri baseUri;

    private readonly ICsfdRowParser rowParser;

    public PerformancesService(IHttpClientFactory httpClientFactory, Uri? baseUri = null)
        : this(new CsfdRowParser(new BadgeExtractor(), new ShowtimeExtractor()), httpClientFactory, baseUri)
    {
    }

    public PerformancesService(ICsfdRowParser rowParser, IHttpClientFactory httpClientFactory, Uri? baseUri = null)
    {
        this.rowParser = rowParser ?? throw new ArgumentNullException(nameof(rowParser));
        this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        this.baseUri = baseUri ?? DefaultBaseUri;
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
        
        Console.WriteLine($"[PerformancesService] Fetching schedules from: {requestUri}");
        Console.WriteLine($"[PerformancesService] Period: {period}");
        
        var html = await FetchHtml(requestUri, cancellationToken).ConfigureAwait(false);
        
        Console.WriteLine($"[PerformancesService] Downloaded {html.Length:N0} bytes of HTML");
        
        return ParseSchedulesAndVenues(html, requestUri);
    }

    private async Task<string> FetchHtml(Uri requestUri, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("Csfd");
        if (!requestUri.IsAbsoluteUri)
        {
            requestUri = new Uri(client.BaseAddress ?? baseUri, requestUri);
        }

        var startTime = DateTime.UtcNow;
        var html = await client.GetStringAsync(requestUri, cancellationToken).ConfigureAwait(false);
        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
        
        Console.WriteLine($"[PerformancesService] HTTP request completed in {elapsed:F0}ms");
        
        return html;
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

    // Parse schedules and return any discovered venue metadata from the same page (best-effort)
    private (IReadOnlyList<Schedule> Schedules, IReadOnlyList<Venue> Venues) ParseSchedulesAndVenues(string html, Uri requestUri)
    {
        Console.WriteLine($"[PerformancesService] Parsing HTML...");
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var sections = doc.DocumentNode.SelectNodes("//section[contains(@class,'updated-box-cinema')]");
        if (sections is null || sections.Count == 0)
        {
            Console.WriteLine($"[PerformancesService] ⚠️ No cinema sections found in HTML");
            return (Array.Empty<Schedule>(), Array.Empty<Venue>());
        }
        
        Console.WriteLine($"[PerformancesService] Found {sections.Count} cinema section(s) to parse");

        var schedules = new Dictionary<(DateOnly date, int movieId), Schedule>();
        var venues = new Dictionary<int, Venue>();
        int sectionIndex = 0;
        int totalPerformancesParsed = 0;

        foreach (var section in sections)
        {
            sectionIndex++;
            var idValue = section.GetAttributeValue("id", string.Empty);
            var cinemaId = ExtractInt(CinemaIdRegex, idValue);

            // Prefer slugged /kino/ link when possible, otherwise fallback to any /kino/ link
            var venueLink = section.SelectSingleNode(".//a[contains(@href,'/kino/') and contains(@href,'-')]")
                            ?? section.SelectSingleNode(".//a[contains(@href,'/kino/')]");
            var venueUrl = venueLink != null ? ToAbsoluteUrl(venueLink.GetAttributeValue("href", string.Empty), requestUri) : null;

            // If the venue link contains an explicit numeric ID (e.g. /kino/1-praha/110-slug/), prefer it over the section id
            // IMPORTANT: Only match if there's a venue-specific segment after the region (e.g. /110-name/)
            if (!string.IsNullOrWhiteSpace(venueUrl))
            {
                // This regex requires a second path segment with a number (the venue ID)
                // Pattern: /kino/{region}/{venueId}-{slug}/ or /kino/{region}/{venueId}/
                // It will NOT match just /kino/1-praha/ (to avoid capturing region ID as venue ID)
                var urlMatch = Regex.Match(venueUrl, "/kino/[^/]+/(\\d+)(?:-[^/]+)?/?", RegexOptions.IgnoreCase);
                if (urlMatch.Success && int.TryParse(urlMatch.Groups[1].Value, out var urlCinemaId))
                {
                    var oldId = cinemaId;
                    cinemaId = urlCinemaId;
                    if (oldId != cinemaId && oldId > 0)
                    {
                        Console.WriteLine($"[PerformancesService] Section {sectionIndex}: Venue ID corrected from {oldId} to {cinemaId} using URL");
                    }
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

                // Normalize venue name: remove leading "Praha -" prefix when present
                if (!string.IsNullOrWhiteSpace(name))
                {
                    name = Regex.Replace(name, "^(Praha)\\s*-\\s*", string.Empty, RegexOptions.IgnoreCase);
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
                // Fallback for independent cinemas: look for H3 tags which typically enclose movie titles
                rows = section.SelectNodes(".//h3");
            }

            if (rows == null)
            {
                continue;
            }

            int rowsProcessed = 0;
            foreach (var row in rows)
            {
                var performance = ParsePerformance(row, showDate, requestUri);
                if (performance == null || performance.Showtimes.Count == 0)
                {
                    continue;
                }

                performance.VenueId = cinemaId;
                rowsProcessed++;
                totalPerformancesParsed++;

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

                // compute badge set for the new performance (use first showtime's badges)
                var performanceBadgeSet = BadgeSet.From(performance.Showtimes.FirstOrDefault()?.Badges);

                var existing = schedule.Performances.FirstOrDefault(p => p.VenueId == performance.VenueId && BadgeSet.From(p.Showtimes.FirstOrDefault()?.Badges).Equals(performanceBadgeSet));
                if (existing != null)
                {
                    // merge showtimes uniquely (showtimes now contain their own badges)
                    foreach (var st in performance.Showtimes)
                    {
                        if (!existing.Showtimes.Any(s => s.StartAt == st.StartAt))
                        {
                            existing.Showtimes.Add(st);
                        }
                    }
                }
                else
                {
                    schedule.Performances.Add(performance);
                }
            }
            
            if (rowsProcessed > 0)
            {
                var venueName = venues.ContainsKey(cinemaId) ? venues[cinemaId].Name : $"Venue #{cinemaId}";
                Console.WriteLine($"[PerformancesService] Section {sectionIndex}: Parsed {rowsProcessed} performance(s) for {venueName} (ID: {cinemaId})");
            }
        }

        var ordered = schedules.Values.OrderBy(s => s.Date).ThenBy(s => s.MovieId).ToList();
        var venueList = venues.Values.OrderBy(v => v.Id).ToList();
        
        Console.WriteLine($"[PerformancesService] ✓ Parsing complete:");
        Console.WriteLine($"[PerformancesService]   - {ordered.Count} schedule(s) for {ordered.Select(s => s.MovieId).Distinct().Count()} unique movie(s)");
        Console.WriteLine($"[PerformancesService]   - {totalPerformancesParsed} performance(s) across {venueList.Count} venue(s)");
        Console.WriteLine($"[PerformancesService]   - Date range: {(ordered.Any() ? $"{ordered.Min(s => s.Date):d} to {ordered.Max(s => s.Date):d}" : "N/A")}");
        
        return (ordered, venueList);
    }


    private Performance? ParsePerformance(HtmlNode row, DateOnly date, Uri requestUri)
    {
        return rowParser.Parse(row, date, requestUri);
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
