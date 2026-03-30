using Microsoft.Extensions.Logging;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>Cinema City Chodov — Roztylská 2321/19, Praha 4. Cinema City ID: 1056.</summary>
public class CinemaCityChodovScraper(IHttpClientFactory factory, ILogger<CinemaCityChodovScraper> logger)
    : CinemaCityPlatformScraper(
        new VenueConfig(0, "Cinema City Chodov", "Praha", new Uri("https://www.cinemacity.cz/cinemas/chodov/1056")),
        cinemaCityId: 1056,
        factory,
        logger);
