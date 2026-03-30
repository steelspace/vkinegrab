using Microsoft.Extensions.Logging;
using vkinegrab.Services.Csfd;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>Přítomnost Boutique Cinema — Siwiecova 1, Praha 3. Program: https://www.kinopritomnost.cz/en</summary>
public class KinoPritomnostScraper(IHtmlFetcher fetcher, ILogger<KinoPritomnostScraper> logger)
    : JsonLdEventPlatformScraper(
        new VenueConfig(0, "Přítomnost", "Praha", new Uri("https://www.kinopritomnost.cz/en")),
        fetcher,
        logger);
