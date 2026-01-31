namespace vkinegrab.Models;

public class TmdbMovie
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? ReleaseDate { get; set; }
    public string? Overview { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public double? VoteAverage { get; set; }
    public int? VoteCount { get; set; }
    public double? Popularity { get; set; }
    public List<int> GenreIds { get; set; } = new();
    public string? OriginalLanguage { get; set; }
    public bool? Adult { get; set; }
    public string Url => $"https://www.themoviedb.org/movie/{Id}";
    public string? FullPosterUrl => !string.IsNullOrEmpty(PosterPath) ? $"https://image.tmdb.org/t/p/original{PosterPath}" : null;
    public string? FullBackdropUrl => !string.IsNullOrEmpty(BackdropPath) ? $"https://image.tmdb.org/t/p/original{BackdropPath}" : null;
}
