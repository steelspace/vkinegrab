using System.Text.RegularExpressions;
using System.Globalization;
using HtmlAgilityPack;
using vkinegrab.Models;

namespace vkinegrab.Services.Csfd;

public class ShowtimeExtractor : IShowtimeExtractor
{
    private static readonly Regex TimeRegex = new("\\b\\d{1,2}:\\d{2}\\b", RegexOptions.Compiled);
    private static readonly string[] TimeFormats = new[] { "H:mm", "HH:mm" };

    public IEnumerable<Showtime> ExtractShowtimes(HtmlNode row, DateOnly date, Uri requestUri)
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

    private static bool TryParseTime(string? text, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        foreach (var format in TimeFormats)
        {
            if (TimeOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Clean(string? s) => (s ?? string.Empty).Trim();

    private static string? ToAbsoluteUrl(string? url, Uri requestUri)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)) return absolute.ToString();

        // non-absolute: resolve relative to base host
        var baseHost = new Uri("https://www.csfd.cz/");
        if (Uri.TryCreate(baseHost, url, out var resolved)) return resolved.ToString();
        if (Uri.TryCreate(requestUri, url, out resolved)) return resolved.ToString();
        return url;
    }
}