using HtmlAgilityPack;
using vkinegrab.Models;

namespace vkinegrab.Services.Csfd;

public class BadgeExtractor : IBadgeExtractor
{
    public IEnumerable<CinemaBadge> ExtractHallBadges(HtmlNode row)
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

    public IEnumerable<CinemaBadge> ExtractFormatBadges(HtmlNode row)
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

    public IEnumerable<CinemaBadge> ExtractBadges(HtmlNode row)
    {
        foreach (var b in ExtractHallBadges(row)) yield return b;
        foreach (var b in ExtractFormatBadges(row)) yield return b;
    }

    // small helper shared across parsers
    private static string? Clean(string? input) => (input ?? string.Empty).Trim();
}