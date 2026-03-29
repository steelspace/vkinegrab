using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services.VenueScrapers;

/// <summary>
/// Abstract base class for venue-specific scrapers.
/// Provides shared helpers: HTML fetching, Czech date parsing, CSFD ID extraction.
/// </summary>
public abstract class VenueScraperBase : IVenueScraper
{
    private static readonly Regex FilmIdRegex = new(@"/film/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CzechDateRegex = new(@"\b(\d{1,2})\.(\d{1,2})\.(\d{4})\b", RegexOptions.Compiled);
    private static readonly string[] TimeFormats = ["H:mm", "HH:mm"];

    protected readonly IHtmlFetcher HtmlFetcher;
    protected readonly ILogger Logger;

    protected VenueScraperBase(IHtmlFetcher htmlFetcher, ILogger logger)
    {
        HtmlFetcher = htmlFetcher ?? throw new ArgumentNullException(nameof(htmlFetcher));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public abstract int VenueId { get; }

    public abstract Task<VenueScrapeResult> ScrapeAsync(CancellationToken cancellationToken = default);

    // ── Shared helpers ─────────────────────────────────────────────────────────

    /// <summary>Fetches HTML from <paramref name="uri"/> and returns a parsed HtmlDocument.</summary>
    protected async Task<HtmlDocument> FetchDocumentAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        var html = await HtmlFetcher.FetchAsync(uri, cancellationToken).ConfigureAwait(false);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }

    /// <summary>
    /// Extracts a CSFD movie ID from a URL like <c>/film/123456-nazev/</c>.
    /// Returns null when the URL doesn't match.
    /// </summary>
    protected static int? ExtractCsfdId(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var m = FilmIdRegex.Match(url);
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    /// <summary>
    /// Parses a Czech date string in <c>DD.MM.YYYY</c> format.
    /// Returns null when the string doesn't match.
    /// </summary>
    protected static DateOnly? ParseCzechDate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = CzechDateRegex.Match(text);
        if (!m.Success) return null;
        var day   = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var year  = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        try { return new DateOnly(year, month, day); }
        catch { return null; }
    }

    /// <summary>
    /// Parses a time string in <c>H:mm</c> or <c>HH:mm</c> format.
    /// Returns null when the string doesn't match.
    /// </summary>
    protected static TimeOnly? ParseTime(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (TimeOnly.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return t;
        return null;
    }

    /// <summary>
    /// Builds a minimal <see cref="Schedule"/> for a single movie + venue with a list of showtimes.
    /// </summary>
    protected static Schedule BuildSchedule(
        DateOnly date,
        int movieId,
        string movieTitle,
        string? movieUrl,
        int venueId,
        IEnumerable<Showtime> showtimes)
    {
        var performance = new Performance
        {
            MovieId    = movieId,
            MovieTitle = movieTitle,
            MovieUrl   = movieUrl,
            VenueId    = venueId
        };
        performance.Showtimes.AddRange(showtimes);

        var schedule = new Schedule
        {
            Date       = date,
            MovieId    = movieId,
            MovieTitle = movieTitle,
            StoredAt   = DateTime.UtcNow
        };
        schedule.Performances.Add(performance);
        return schedule;
    }
}
