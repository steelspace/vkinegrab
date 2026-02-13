namespace vkinegrab.Models;

public class CsfdMovie
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? ImdbId { get; set; }
    public string? ImdbUrl => string.IsNullOrEmpty(ImdbId) ? null : $"https://www.imdb.com/title/{ImdbId}/";
    public Dictionary<string, string> LocalizedTitles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Rating { get; set; }
    public double? ImdbRating { get; set; }
    public int? ImdbRatingCount { get; set; }
    public List<string> Genres { get; set; } = new();
    public string? Origin { get; set; }
    public List<string> Origins { get; set; } = new();
    public string? Year { get; set; }
    public string? Duration { get; set; }
    public List<string> Directors { get; set; } = new();
    public List<string> Cast { get; set; } = new();
    public string? Description { get; set; }
    public string? PosterUrl { get; set; }
}
