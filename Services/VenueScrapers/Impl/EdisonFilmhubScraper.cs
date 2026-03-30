using Microsoft.Extensions.Logging;
using vkinegrab.Services.Csfd;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>
/// Edison Filmhub — Jeruzalémská 1321/2, Praha 1. Program: https://edisonfilmhub.cz/en/programme
///
/// Uses the same JSON-LD Event platform as Aero/Lucerna/etc., with two quirks:
///   • <c>startDate</c> is an array <c>["2026-03-31T14:00"]</c> (no timezone offset)
///   • <c>endDate</c> is a date-only array — duration not derivable from it
/// Both are handled transparently by <see cref="JsonLdEventPlatformScraper"/>.
/// </summary>
public class EdisonFilmhubScraper(IHtmlFetcher fetcher, ILogger<EdisonFilmhubScraper> logger)
    : JsonLdEventPlatformScraper(
        new VenueConfig(0, "Edison Filmhub", "Praha", new Uri("https://edisonfilmhub.cz/en/programme")),
        fetcher,
        logger);
