using Microsoft.Extensions.Logging;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>Cinema City Zličín — Řevnická 121/1, Praha 5. Cinema City ID: 1051.</summary>
public class CinemaCityZlicinScraper(IHttpClientFactory factory, ILogger<CinemaCityZlicinScraper> logger)
    : CinemaCityPlatformScraper(
        new VenueConfig(0, "Cinema City Zličín", "Praha", new Uri("https://www.cinemacity.cz/cinemas/zlicin/1051")),
        cinemaCityId: 1051,
        factory,
        logger);
