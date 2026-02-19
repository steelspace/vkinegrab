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

    [BsonElement("duration")]
    public string? Duration { get; set; }

    [BsonElement("rating")]
    public string? Rating { get; set; }

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("origin")]
    public string? Origin { get; set; }

    [BsonElement("origin_countries")]
    public List<string> OriginCountries { get; set; } = new();

    [BsonElement("genres")]
    public List<string> Genres { get; set; } = new();

    [BsonElement("directors")]
    public List<string> Directors { get; set; } = new();

    [BsonElement("cast")]
    public List<string> Cast { get; set; } = new();

    [BsonElement("poster_url")]
    public string? PosterUrl { get; set; }

    [BsonElement("csfd_poster_url")]
    public string? CsfdPosterUrl { get; set; }

    [BsonElement("backdrop_url")]
    public string? BackdropUrl { get; set; }

    [BsonElement("imdb_rating")]
    public double? ImdbRating { get; set; }

    [BsonElement("imdb_rating_count")]
    public int? ImdbRatingCount { get; set; }

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

    [BsonElement("homepage")]
    public string? Homepage { get; set; }

    [BsonElement("trailer_url")]
    public string? TrailerUrl { get; set; }

    [BsonElement("localized_titles")]
    public Dictionary<string, string> LocalizedTitles { get; set; } = new();

    [BsonElement("release_date")]
    public DateTime? ReleaseDate { get; set; }

    [BsonElement("stored_at")]
    public DateTime StoredAt { get; set; }

    public Movie ToMovie()
    {
        return new Movie
        {
            CsfdId = CsfdId,
            TmdbId = TmdbId,
            ImdbId = ImdbId,
            Title = Title,
            OriginalTitle = OriginalTitle,
            Year = Year,
            Duration = Duration,
            Rating = Rating,
            Description = Description,
            Origin = Origin,
            OriginCountries = OriginCountries ?? new List<string>(),
            Genres = Genres ?? new List<string>(),
            Directors = Directors ?? new List<string>(),
            Cast = Cast ?? new List<string>(),
            PosterUrl = PosterUrl,
            CsfdPosterUrl = CsfdPosterUrl,
            BackdropUrl = BackdropUrl,
            ImdbRating = ImdbRating,
            ImdbRatingCount = ImdbRatingCount,
            VoteAverage = VoteAverage,
            VoteCount = VoteCount,
            Popularity = Popularity,
            OriginalLanguage = OriginalLanguage,
            Adult = Adult,
            Homepage = Homepage,
            TrailerUrl = TrailerUrl,
            LocalizedTitles = LocalizedTitles ?? new Dictionary<string, string>(),
            ReleaseDate = ReleaseDate,
            StoredAt = StoredAt
        };
    }
}