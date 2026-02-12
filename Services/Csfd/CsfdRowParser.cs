using HtmlAgilityPack;
using vkinegrab.Models;

namespace vkinegrab.Services.Csfd;

using System;

public class CsfdRowParser : ICsfdRowParser
{
    private readonly IBadgeExtractor badgeExtractor;
    private readonly IShowtimeExtractor showtimeExtractor;

    public CsfdRowParser(IBadgeExtractor badgeExtractor, IShowtimeExtractor showtimeExtractor)
    {
        this.badgeExtractor = badgeExtractor ?? throw new ArgumentNullException(nameof(badgeExtractor));
        this.showtimeExtractor = showtimeExtractor ?? throw new ArgumentNullException(nameof(showtimeExtractor));
    }

    public Performance? Parse(HtmlNode row, DateOnly date, Uri requestUri)
    {
        // 1. Try table-based layout (multiplexes)
        var movieLink = row.SelectSingleNode(".//td[contains(@class,'name')]//a[contains(@href,'/film/')]");
        
        // 2. Fallback for header-based or div-based layout (indie cinemas)
        if (movieLink == null)
        {
            movieLink = row.SelectSingleNode(".//a[contains(@href,'/film/')]");
        }

        if (movieLink == null)
        {
            return null;
        }

        var movieTitle = Clean(movieLink.InnerText);
        var movieUrl = ToAbsoluteUrl(movieLink.GetAttributeValue("href", string.Empty), requestUri);
        var movieId = ExtractInt(new System.Text.RegularExpressions.Regex("/film/(\\d+)", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase), movieUrl);

        var performance = new Performance
        {
            MovieId = movieId,
            MovieTitle = movieTitle ?? string.Empty,
            MovieUrl = movieUrl
        };

        var rowBadges = badgeExtractor.ExtractBadges(row).ToList();

        var showtimes = showtimeExtractor.ExtractShowtimes(row, date, requestUri);
        foreach (var showtime in showtimes)
        {
            if (performance.Showtimes.All(s => s.StartAt != showtime.StartAt))
            {
                // copy badges to showtime
                foreach (var badge in rowBadges)
                {
                    showtime.Badges.Add(new CinemaBadge { Kind = badge.Kind, Code = badge.Code, Description = badge.Description });
                }

                performance.Showtimes.Add(showtime);
            }
        }

        return performance.Showtimes.Count > 0 ? performance : null;
    }

    private static string? Clean(string? s) => (s ?? string.Empty).Trim();

    private static int ExtractInt(System.Text.RegularExpressions.Regex rx, string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0;
        var m = rx.Match(input);
        if (!m.Success) return 0;
        if (int.TryParse(m.Groups[1].Value, out var v)) return v;
        return 0;
    }

    private static string? ToAbsoluteUrl(string? url, Uri requestUri)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)) return absolute.ToString();

        var baseHost = new Uri("https://www.csfd.cz/");
        if (Uri.TryCreate(baseHost, url, out var resolved)) return resolved.ToString();
        if (Uri.TryCreate(requestUri, url, out resolved)) return resolved.ToString();
        return url;
    }
}