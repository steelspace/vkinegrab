using System;
using HtmlAgilityPack;
using vkinegrab.Models;

namespace vkinegrab.Services.Csfd;

public interface IShowtimeExtractor
{
    IEnumerable<Showtime> ExtractShowtimes(HtmlNode row, DateOnly date, Uri requestUri);
}