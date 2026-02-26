using MongoDB.Bson.Serialization.Attributes;
using vkinegrab.Models;

namespace vkinegrab.Models.Dtos;

[BsonIgnoreExtraElements]
internal class CrewMemberDto
{
    [BsonElement("tmdb_id")]
    public int TmdbId { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("role")]
    public string Role { get; set; } = string.Empty;

    [BsonElement("photo_url")]
    public string? PhotoUrl { get; set; }

    public CrewMember ToModel() => new()
    {
        TmdbId = TmdbId,
        Name = Name,
        Role = Role,
        PhotoUrl = PhotoUrl
    };

    public static CrewMemberDto FromModel(CrewMember model) => new()
    {
        TmdbId = model.TmdbId,
        Name = model.Name,
        Role = model.Role,
        PhotoUrl = model.PhotoUrl
    };
}
