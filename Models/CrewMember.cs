namespace vkinegrab.Models;

public class CrewMember
{
    public int TmdbId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string ProfileUrl => $"https://www.themoviedb.org/person/{TmdbId}";
}
