using Microsoft.Extensions.Logging;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>Cinema City Letňany — Veselská 663, Praha 9. Cinema City ID: 1030.</summary>
public class CinemaCityLetnanyScraper(IHttpClientFactory factory, ILogger<CinemaCityLetnanyScraper> logger)
    : CinemaCityPlatformScraper(
        new VenueConfig(0, "Cinema City Letňany", "Praha", new Uri("https://www.cinemacity.cz/cinemas/letnany/1030")),
        cinemaCityId: 1030,
        factory,
        logger);
