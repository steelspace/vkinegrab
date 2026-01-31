using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using vkinegrab.Models;

namespace vkinegrab.Services.Csfd;

public class PerformancesService
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

    public async Task<IReadOnlyList<VenueSchedule>> GetPerformances(
        Uri? pageUri = null,
        string period = "today",
        CancellationToken cancellationToken = default)
    {
        pageUri ??= new Uri(baseUri, "/kino/1-praha/?period=all");
        var requestUri = AppendPeriod(pageUri, period);
        var html = await FetchHtml(requestUri, cancellationToken).ConfigureAwait(false);
        return ParseCinemas(html, requestUri);
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

    private IReadOnlyList<VenueSchedule> ParseCinemas(string html, Uri requestUri)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var sections = doc.DocumentNode.SelectNodes("//section[contains(@class,'updated-box-cinema')]");
        if (sections is null || sections.Count == 0)
        {
            return Array.Empty<VenueSchedule>();
        }

        var schedules = new List<VenueSchedule>(sections.Count);
        foreach (var section in sections)
        {
            var schedule = ParseCinema(section, requestUri);
            if (schedule != null)
            {
                schedules.Add(schedule);
            }
        }

        return schedules;
    }

    private VenueSchedule? ParseCinema(HtmlNode section, Uri requestUri)
    {
        var idValue = section.GetAttributeValue("id", string.Empty);
        var cinemaId = ExtractInt(CinemaIdRegex, idValue);

        var header = section.SelectSingleNode(".//header[contains(@class,'updated-box-header')]");
        var titleLink = header?.SelectSingleNode(".//h2/a");
        var fullTitle = Clean(titleLink?.InnerText);

        string? city = null;
        string? cinemaName = fullTitle;
        if (!string.IsNullOrWhiteSpace(fullTitle))
        {
            var parts = fullTitle.Split(" - ", 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                city = parts[0];
                cinemaName = parts[1];
            }
        }

        var mapLink = header?.SelectSingleNode(".//a[contains(@class,'btn-web')]");
        var address = Clean(mapLink?.InnerText);
        var mapUrl = ToAbsoluteUrl(mapLink?.GetAttributeValue("href", string.Empty), requestUri);
        var detailUrl = ToAbsoluteUrl(titleLink?.GetAttributeValue("href", string.Empty), requestUri);

        var subHeader = section.SelectSingleNode(".//div[contains(@class,'update-box-sub-header')]");
        var dateText = subHeader?.ChildNodes
                                 .Where(n => n.NodeType == HtmlNodeType.Text)
                                 .Select(n => n.InnerText)
                                 .FirstOrDefault();
        var scheduleDate = ParseDate(dateText);
        var showDate = scheduleDate ?? DateOnly.FromDateTime(DateTime.Today);

        var rows = section.SelectNodes(".//table[contains(@class,'cinema-table')]//tr");
        var venue = new Venue
        {
            Id = cinemaId,
            City = city,
            Name = cinemaName,
            DetailUrl = detailUrl,
            Address = address,
            MapUrl = mapUrl
        };

        var schedule = new VenueSchedule
        {
            Venue = venue,
            ScheduleDate = scheduleDate
        };

        if (rows != null)
        {
            foreach (var row in rows)
            {
                var performance = ParsePerformance(row, showDate, requestUri);
                if (performance != null && performance.Showtimes.Count > 0)
                {
                    schedule.Performances.Add(performance);
                }
            }
        }

        if (schedule.Performances.Count == 0)
        {
            return null;
        }

        return schedule;
    }

    private CinemaPerformance? ParsePerformance(HtmlNode row, DateOnly date, Uri requestUri)
    {
        var movieLink = row.SelectSingleNode(".//td[contains(@class,'name')]//a[contains(@href,'/film/')]");
        if (movieLink == null)
        {
            return null;
        }

        var movieTitle = Clean(movieLink.InnerText);
        var movieUrl = ToAbsoluteUrl(movieLink.GetAttributeValue("href", string.Empty), requestUri);
        var movieId = ExtractInt(FilmIdRegex, movieUrl);

        var performance = new CinemaPerformance
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

    private IEnumerable<CsfdCinemaBadge> ExtractHallBadges(HtmlNode row)
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
            yield return new CsfdCinemaBadge
            {
                Kind = CsfdBadgeKind.Hall,
                Code = code,
                Description = description
            };
        }
    }

    private IEnumerable<CsfdCinemaBadge> ExtractFormatBadges(HtmlNode row)
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
            yield return new CsfdCinemaBadge
            {
                Kind = CsfdBadgeKind.Format,
                Code = code,
                Description = description
            };
        }
    }

    private IEnumerable<CsfdShowtime> ExtractShowtimes(HtmlNode row, DateOnly date, Uri requestUri)
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
                        yield return new CsfdShowtime
                        {
                            StartAt = start,
                            TicketsAvailable = true,
                            TicketUrl = ToAbsoluteUrl(anchor.GetAttributeValue("href", string.Empty), requestUri),
                            IsPast = isPast
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
                    yield return new CsfdShowtime
                    {
                        StartAt = start,
                        TicketsAvailable = hasTicketClass,
                        TicketUrl = hasTicketClass ? ToAbsoluteUrl(cell.SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty), requestUri) : null,
                        IsPast = isPast
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
            return absolute.ToString();
        }

        if (href.StartsWith("//", StringComparison.Ordinal))
        {
            return requestUri.Scheme + ":" + href;
        }

        var baseUri = requestUri;
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
