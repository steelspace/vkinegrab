namespace vkinegrab.Services.VenueScrapers;

/// <summary>
/// Static configuration for a single cinema venue.
/// One instance per concrete IVenueScraper implementation.
/// </summary>
public record VenueConfig(
    int VenueId,
    string Name,
    string City,
    Uri BaseUrl
);
