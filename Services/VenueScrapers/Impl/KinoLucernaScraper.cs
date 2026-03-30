using Microsoft.Extensions.Logging;
using vkinegrab.Services.Csfd;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>Kino Lucerna — Vodičkova 36, Praha 1. Program: https://kinolucerna.cz/en</summary>
public class KinoLucernaScraper(IHtmlFetcher fetcher, ILogger<KinoLucernaScraper> logger)
    : JsonLdEventPlatformScraper(
        new VenueConfig(0, "Kino Lucerna", "Praha", new Uri("https://kinolucerna.cz/en")),
        fetcher,
        logger);
