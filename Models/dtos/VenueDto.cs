using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace vkinegrab.Models.Dtos;

internal class VenueDto
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("venue_id")]
    public int VenueId { get; set; }

    [BsonElement("city")]
    public string? City { get; set; }

    [BsonElement("name")]
    public string? Name { get; set; }

    [BsonElement("detail_url")]
    public string? DetailUrl { get; set; }

    [BsonElement("address")]
    public string? Address { get; set; }

    [BsonElement("map_url")]
    public string? MapUrl { get; set; }
}

internal static class VenueDtoExtensions
{
    public static VenueDto ToDto(this Venue venue)
        => new VenueDto
        {
            Id = ObjectId.GenerateNewId(),
            VenueId = venue.Id,
            City = venue.City,
            Name = venue.Name,
            DetailUrl = venue.DetailUrl,
            Address = venue.Address,
            MapUrl = venue.MapUrl
        };

    public static Venue ToVenue(this VenueDto dto)
        => new Venue
        {
            Id = dto.VenueId,
            City = dto.City,
            Name = dto.Name,
            DetailUrl = dto.DetailUrl,
            Address = dto.Address,
            MapUrl = dto.MapUrl
        };
}