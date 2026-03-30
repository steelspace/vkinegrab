using Microsoft.Extensions.Logging;
using vkinegrab.Services.Csfd;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>Kino Světozor — Vodičkova 41, Praha 1. Program: https://www.kinosvetozor.cz/en</summary>
public class KinoSvetozorScraper(IHtmlFetcher fetcher, ILogger<KinoSvetozorScraper> logger)
    : JsonLdEventPlatformScraper(
        new VenueConfig(0, "Kino Světozor", "Praha", new Uri("https://www.kinosvetozor.cz/en")),
        fetcher,
        logger);
