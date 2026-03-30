using Microsoft.Extensions.Logging;
using vkinegrab.Services.Csfd;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>CineStar Černý Most — Chlumecká 765/6, Praha 9. Program: https://cinestar.cz/cz/praha9</summary>
public class CineStarCernyMostScraper(IHtmlFetcher fetcher, ILogger<CineStarCernyMostScraper> logger)
    : CineStarPlatformScraper(
        new VenueConfig(0, "CineStar Černý Most", "Praha", new Uri("https://cinestar.cz/cz/praha9")),
        fetcher,
        logger);
