using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace vkinegrab.Models.Dtos;

internal class MovieDto
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("csfd_id")]
    public int CsfdId { get; set; }

    [BsonElement("tmdb_id")]
    public int? TmdbId { get; set; }

    [BsonElement("imdb_id")]
    public string? ImdbId { get; set; }

    [BsonElement("title")]
    public string? Title { get; set; }

    [BsonElement("original_title")]
    public string? OriginalTitle { get; set; }

    [BsonElement("year")]
    public string? Year { get; set; }

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("origin")]
    public string? Origin { get; set; }

    [BsonElement("genres")]
    public List<string> Genres { get; set; } = new();

    [BsonElement("directors")]
    public List<string> Directors { get; set; } = new();

    [BsonElement("cast")]
    public List<string> Cast { get; set; } = new();

    [BsonElement("poster_url")]
    public string? PosterUrl { get; set; }

    [BsonElement("backdrop_url")]
    public string? BackdropUrl { get; set; }

    [BsonElement("vote_average")]
    public double? VoteAverage { get; set; }

    [BsonElement("vote_count")]
    public int? VoteCount { get; set; }

    [BsonElement("popularity")]
    public double? Popularity { get; set; }

    [BsonElement("original_language")]
    public string? OriginalLanguage { get; set; }

    [BsonElement("adult")]
    public bool? Adult { get; set; }

    [BsonElement("localized_titles")]
    public Dictionary<string, string> LocalizedTitles { get; set; } = new();

    [BsonElement("release_date")]
    public DateTime? ReleaseDate { get; set; }

    [BsonElement("stored_at")]
    public DateTime StoredAt { get; set; }
}