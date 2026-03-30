using Microsoft.Extensions.Logging;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>Cinema City Flora (IMAX) — Vinohradská 151, Praha 3. Cinema City ID: 1052.</summary>
public class CinemaCityFloraScraper(IHttpClientFactory factory, ILogger<CinemaCityFloraScraper> logger)
    : CinemaCityPlatformScraper(
        new VenueConfig(0, "Cinema City Flora", "Praha", new Uri("https://www.cinemacity.cz/cinemas/flora/1052")),
        cinemaCityId: 1052,
        factory,
        logger);
