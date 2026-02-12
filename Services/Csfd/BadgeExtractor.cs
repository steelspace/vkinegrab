using HtmlAgilityPack;
using vkinegrab.Models;

namespace vkinegrab.Services.Csfd;

public class BadgeExtractor : IBadgeExtractor
{
    public IEnumerable<CinemaBadge> ExtractHallBadges(HtmlNode row)
    {
        var hallSpans = row.SelectNodes(".//td[contains(@class,'name')]//span[contains(@class,'cinema-icon')]")
                        ?? row.SelectNodes(".//span[contains(@class,'cinema-icon')]");
        if (hallSpans == null)
        {
            // If it's a header-based layout, check following siblings for hall/format spans
            var current = row.NextSibling;
            var siblingsSpans = new List<HtmlNode>();
            while (current != null && !current.Name.Equals("h3", StringComparison.OrdinalIgnoreCase) && !current.Name.Equals("tr", StringComparison.OrdinalIgnoreCase))
            {
                if (current.NodeType == HtmlNodeType.Element)
                {
                    var found = current.SelectNodes(".//span[contains(@class,'cinema-icon')]");
                    if (found != null) siblingsSpans.AddRange(found);
                }
                current = current.NextSibling;
            }
            if (siblingsSpans.Count > 0) hallSpans = new HtmlNodeCollection(row) { /* we can't really construct a collection like this easily in HAP, so we just iterate manually below */ };
            
            // Re-evaluating: HAP's HtmlNodeCollection is a bit annoying to construct manually.
            // Let's just iterate and yield.
            
            if (siblingsSpans.Count > 0)
            {
                foreach (var span in siblingsSpans)
                {
                    var code = Clean(span.InnerText)?.TrimEnd(',') ?? string.Empty;
                    var description = Clean(span.GetAttributeValue("data-tippy-content", string.Empty)) ?? code;
                    yield return new CinemaBadge { Kind = BadgeKind.Hall, Code = code, Description = description };
                }
                yield break;
            }

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
        var formatSpans = row.SelectNodes(".//td[contains(@class,'td-title')]//span")
                          ?? row.SelectNodes(".//span[not(@class)]"); // Fallback
        
        var results = new List<HtmlNode>();
        if (formatSpans != null) results.AddRange(formatSpans);

        // Check siblings for header-based layout
        var current = row.NextSibling;
        while (current != null && !current.Name.Equals("h3", StringComparison.OrdinalIgnoreCase) && !current.Name.Equals("tr", StringComparison.OrdinalIgnoreCase))
        {
            if (current.NodeType == HtmlNodeType.Element)
            {
                var found = current.SelectNodes(".//span[not(@class)]") ?? current.SelectNodes(".//span[@class='badge']");
                if (found != null) results.AddRange(found);
            }
            current = current.NextSibling;
        }

        if (results.Count == 0)
        {
            yield break;
        }

        foreach (var span in results)
        {
            var code = Clean(span.InnerText)?.TrimEnd(',') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code) || code.Length > 5) // Most format codes are short like D, T, 3D
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