using Microsoft.Extensions.Logging;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>Cinema City Nový Smíchov — Plzeňská 8, Praha 5. Cinema City ID: 1031.</summary>
public class CinemaCityNovySmichovScraper(IHttpClientFactory factory, ILogger<CinemaCityNovySmichovScraper> logger)
    : CinemaCityPlatformScraper(
        new VenueConfig(0, "Cinema City Nový Smíchov", "Praha", new Uri("https://www.cinemacity.cz/cinemas/novysmichov/1031")),
        cinemaCityId: 1031,
        factory,
        logger);
