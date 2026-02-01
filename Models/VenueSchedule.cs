namespace vkinegrab.Models;

public class Schedule
{
    public DateOnly Date { get; set; }
    public int MovieId { get; set; }
    public string? MovieTitle { get; set; }
    public List<Performance> Performances { get; } = new();
}