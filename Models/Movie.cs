namespace vkinegrab.Models;

public class Movie
{
    public int CsfdId { get; set; }
    public int? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    
    // Primary data (from CSFD, fallback to TMDB)
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Year { get; set; }
    public string? Duration { get; set; }
    public string? Rating { get; set; }
    public string? DescriptionCs { get; set; }
    public string? DescriptionEn { get; set; }
    public string? Origin { get; set; }
    public List<string> OriginCountryCodes { get; set; } = new();
    public List<string> Genres { get; set; } = new();
    public List<string> Directors { get; set; } = new();
    public List<string> Cast { get; set; } = new();
    
    // Media (primarily from TMDB, keep CSFD source separately)
    public string? PosterUrl { get; set; }
    public string? CsfdPosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    
    // IMDb specific
    public double? ImdbRating { get; set; }
    public int? ImdbRatingCount { get; set; }
    
    // TMDB specific
    public double? VoteAverage { get; set; }
    public int? VoteCount { get; set; }
    public double? Popularity { get; set; }
    public string? OriginalLanguage { get; set; }
    public bool? Adult { get; set; }
    public string? Homepage { get; set; }
    public string? TrailerUrl { get; set; }
    public List<CrewMember> Credits { get; set; } = new();
    
    // Localization
    public Dictionary<string, string> LocalizedTitles { get; set; } = new();

    // Release / storage metadata
    public DateTime? ReleaseDate { get; set; }
    public DateTime? StoredAt { get; set; }
    
    public string CsfdUrl => $"https://www.csfd.cz/film/{CsfdId}";
    public string TmdbUrl => TmdbId.HasValue ? $"https://www.themoviedb.org/movie/{TmdbId}" : string.Empty;
    public string ImdbUrl => !string.IsNullOrEmpty(ImdbId) ? $"https://www.imdb.com/title/{ImdbId}" : string.Empty;
}
