using Microsoft.Extensions.Logging;
using vkinegrab.Services.VenueScrapers.Platforms;

namespace vkinegrab.Services.VenueScrapers.Impl;

/// <summary>Cinema City Slovanský dům — Na Příkopě 22, Praha 1. Cinema City ID: 1033.</summary>
public class CinemaCitySlovanskyDumScraper(IHttpClientFactory factory, ILogger<CinemaCitySlovanskyDumScraper> logger)
    : CinemaCityPlatformScraper(
        // TODO: replace VenueId 0 with the actual CSFD venue ID (run `grab-venues` to discover)
        new VenueConfig(0, "Cinema City Slovanský dům", "Praha", new Uri("https://www.cinemacity.cz/cinemas/slovanskydum/1033")),
        cinemaCityId: 1033,
        factory,
        logger);
