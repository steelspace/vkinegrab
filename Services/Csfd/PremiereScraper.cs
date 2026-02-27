using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using vkinegrab.Models;

namespace vkinegrab.Services.Csfd;

public class PremiereScraper : IPremiereScraper
{
    private static readonly Regex FilmIdRegex = new("/film/(\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IHttpClientFactory httpClientFactory;

    public PremiereScraper(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<IReadOnlyList<Premiere>> ScrapePremieresAsync(int year, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.csfd.cz/kino/prehled/?year={year}";
        Console.WriteLine($"[PremiereScraper] Fetching premieres from: {url}");

        var client = httpClientFactory.CreateClient("Csfd");
        var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"[PremiereScraper] Downloaded {html.Length:N0} bytes");

        return ParsePremieres(html, year);
    }

    internal static IReadOnlyList<Premiere> ParsePremieres(string html, int year)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var premieres = new List<Premiere>();

        // Each table represents one month; each <tbody> groups rows for one premiere date.
        // The first <tr> in a <tbody> has a date cell (<td class="date-only" rowspan="N">DD.MM.</td>)
        // followed by name/dist/incinema cells. Subsequent <tr>s only have name/dist/incinema.
        var tables = doc.DocumentNode.SelectNodes("//table[contains(@class,'table-cinema-premieres')]");
        if (tables == null)
            return premieres;

        foreach (var table in tables)
        {
            var tbodies = table.SelectNodes("./tbody");
            if (tbodies == null)
                continue;

            foreach (var tbody in tbodies)
            {
                // Extract date from the date cell in this tbody
                var dateCell = tbody.SelectSingleNode(".//td[contains(@class,'date-only')]");
                if (dateCell == null)
                    continue;

                var dateText = WebUtility.HtmlDecode(dateCell.InnerText).Trim();
                if (!TryParseDate(dateText, year, out var premiereDate))
                    continue;

                // Parse all rows in this tbody — each row is a movie for this date
                var rows = tbody.SelectNodes(".//tr");
                if (rows == null)
                    continue;

                foreach (var row in rows)
                {
                    // Find the film link anywhere in the row
                    var movieLink = row.SelectSingleNode(".//a[contains(@href,'/film/')]");
                    if (movieLink == null)
                        continue;

                    var href = movieLink.GetAttributeValue("href", "");
                    var filmIdMatch = FilmIdRegex.Match(href);
                    if (!filmIdMatch.Success)
                        continue;

                    var csfdId = int.Parse(filmIdMatch.Groups[1].Value);

                    premieres.Add(new Premiere
                    {
                        CsfdId = csfdId,
                        PremiereDate = premiereDate
                    });
                }
            }
        }

        Console.WriteLine($"[PremiereScraper] Parsed {premieres.Count} premieres for year {year}");
        return premieres;
    }

    private static bool TryParseDate(string text, int year, out DateOnly date)
    {
        date = default;
        // Expected format: "DD.MM." or "D.M."
        var parts = text.TrimEnd('.').Split('.');
        if (parts.Length < 2)
            return false;

        if (!int.TryParse(parts[0], out var day) || !int.TryParse(parts[1], out var month))
            return false;

        date = new DateOnly(year, month, day);
        return true;
    }
}
