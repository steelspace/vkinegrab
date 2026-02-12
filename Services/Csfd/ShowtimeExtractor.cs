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
        
        var seen = new HashSet<DateTime>();

        if (cells != null)
        {
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

                        if (seen.Add(date.ToDateTime(time)))
                        {
                            yield return new Showtime
                            {
                                StartAt = time,
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

                    if (seen.Add(date.ToDateTime(time)))
                    {
                        yield return new Showtime
                        {
                            StartAt = time,
                            TicketsAvailable = hasTicketClass,
                            TicketUrl = hasTicketClass ? ToAbsoluteUrl(cell.SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty), requestUri) : null,
                        };
                    }
                }
            }
        }
        else
        {
            // Fallback for non-table layout: search the whole row node and its immediate following text/siblings
            // In some layouts, showtimes are in <a> tags or just raw text following a movie title
            var anchors = row.SelectNodes(".//a[not(contains(@href, '/film/'))]");
            if (anchors != null)
            {
                foreach (var anchor in anchors)
                {
                    var timeText = Clean(anchor.InnerText);
                    if (TryParseTime(timeText, out var time))
                    {
                        if (seen.Add(date.ToDateTime(time)))
                        {
                            yield return new Showtime
                            {
                                StartAt = time,
                                TicketsAvailable = true,
                                TicketUrl = ToAbsoluteUrl(anchor.GetAttributeValue("href", string.Empty), requestUri),
                            };
                        }
                    }
                }
            }

            // Also search for bare times in text nodes
            var rawText = HtmlEntity.DeEntitize(row.InnerText ?? string.Empty);
            
            // If it's a header-based layout, we might need to check following siblings until next H3
            var current = row.NextSibling;
            while (current != null && !current.Name.Equals("h3", StringComparison.OrdinalIgnoreCase) && !current.Name.Equals("tr", StringComparison.OrdinalIgnoreCase))
            {
                rawText += " " + HtmlEntity.DeEntitize(current.InnerText ?? string.Empty);
                
                // Also check for links in siblings
                if (current.NodeType == HtmlNodeType.Element)
                {
                    var siblingAnchors = current.SelectNodes(".//a[not(contains(@href, '/film/'))]");
                    if (siblingAnchors != null)
                    {
                        foreach (var sa in siblingAnchors)
                        {
                            if (TryParseTime(Clean(sa.InnerText), out var stime))
                            {
                                if (seen.Add(date.ToDateTime(stime)))
                                {
                                    yield return new Showtime
                                    {
                                        StartAt = stime,
                                        TicketsAvailable = true,
                                        TicketUrl = ToAbsoluteUrl(sa.GetAttributeValue("href", string.Empty), requestUri),
                                    };
                                }
                            }
                        }
                    }
                }
                current = current.NextSibling;
            }

            foreach (Match match in TimeRegex.Matches(rawText))
            {
                if (TryParseTime(match.Value, out var time))
                {
                    if (seen.Add(date.ToDateTime(time)))
                    {
                        yield return new Showtime
                        {
                            StartAt = time,
                            TicketsAvailable = false, // Hard to tell without specific classes
                            TicketUrl = null
                        };
                    }
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