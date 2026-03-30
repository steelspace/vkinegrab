using Microsoft.Extensions.Logging;
using vkinegrab.Services.Csfd;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>Kino Aero — Biskupcova 31, Praha 3. Program: https://www.kinoaero.cz/en</summary>
public class KinoAeroScraper(IHtmlFetcher fetcher, ILogger<KinoAeroScraper> logger)
    : JsonLdEventPlatformScraper(
        new VenueConfig(0, "Kino Aero", "Praha", new Uri("https://www.kinoaero.cz/en")),
        fetcher,
        logger);
