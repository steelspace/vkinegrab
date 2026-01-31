namespace vkinegrab.Models.Dtos;

internal class VenueDto
{
    public int Id { get; set; }
    public string? City { get; set; }
    public string? Name { get; set; }
    public string? DetailUrl { get; set; }
    public string? Address { get; set; }
    public string? MapUrl { get; set; }
}

internal static class VenueDtoExtensions
{
    public static VenueDto ToDto(this Venue venue)
        => new VenueDto
        {
            Id = venue.Id,
            City = venue.City,
            Name = venue.Name,
            DetailUrl = venue.DetailUrl,
            Address = venue.Address,
            MapUrl = venue.MapUrl
        };

    public static Venue ToVenue(this VenueDto dto)
        => new Venue
        {
            Id = dto.Id,
            City = dto.City,
            Name = dto.Name,
            DetailUrl = dto.DetailUrl,
            Address = dto.Address,
            MapUrl = dto.MapUrl
        };
}