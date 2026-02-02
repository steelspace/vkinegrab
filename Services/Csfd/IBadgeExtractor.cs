using HtmlAgilityPack;
using vkinegrab.Models;

namespace vkinegrab.Services.Csfd;

public interface IBadgeExtractor
{
    IEnumerable<CinemaBadge> ExtractHallBadges(HtmlNode row);
    IEnumerable<CinemaBadge> ExtractFormatBadges(HtmlNode row);
    IEnumerable<CinemaBadge> ExtractBadges(HtmlNode row);
}