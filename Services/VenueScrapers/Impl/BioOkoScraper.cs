using Microsoft.Extensions.Logging;
using vkinegrab.Services.Csfd;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>Bio Oko — Františka Křížka 460/15, Praha 7. Program: https://biooko.net/en</summary>
public class BioOkoScraper(IHtmlFetcher fetcher, ILogger<BioOkoScraper> logger)
    : JsonLdEventPlatformScraper(
        new VenueConfig(0, "Bio Oko", "Praha", new Uri("https://biooko.net/en")),
        fetcher,
        logger);
