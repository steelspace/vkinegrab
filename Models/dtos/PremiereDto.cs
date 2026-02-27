using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace vkinegrab.Models.Dtos;

internal class PremiereDto
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("csfd_id")]
    public int CsfdId { get; set; }

    [BsonElement("premiere_date")]
    public DateOnly PremiereDate { get; set; }

    [BsonElement("stored_at")]
    public DateTime StoredAt { get; set; }
}

internal static class PremiereDtoExtensions
{
    public static PremiereDto ToDto(this Premiere premiere)
        => new PremiereDto
        {
            Id = ObjectId.GenerateNewId(),
            CsfdId = premiere.CsfdId,
            PremiereDate = premiere.PremiereDate,
            StoredAt = DateTime.UtcNow
        };

    public static Premiere ToModel(this PremiereDto dto)
        => new Premiere
        {
            CsfdId = dto.CsfdId,
            PremiereDate = dto.PremiereDate
        };
}
