using Microsoft.Extensions.Logging;
using vkinegrab.Services.Csfd;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>CineStar Anděl — Radlická 3179/1E, Praha 5. Program: https://cinestar.cz/cz/praha5</summary>
public class CineStarAndelScraper(IHtmlFetcher fetcher, ILogger<CineStarAndelScraper> logger)
    : CineStarPlatformScraper(
        new VenueConfig(0, "CineStar Anděl", "Praha", new Uri("https://cinestar.cz/cz/praha5")),
        fetcher,
        logger);
