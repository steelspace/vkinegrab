namespace vkinegrab.Models;

public class Venue
{
    public int Id { get; set; }
    public string? City { get; set; }
    public string? Name { get; set; }
    public string? DetailUrl { get; set; }
    public string? Address { get; set; }
    public string? MapUrl { get; set; }
    public DateOnly? ScheduleDate { get; set; }
    public List<CinemaPerformance> Performances { get; } = new();
}

public class CinemaPerformance
{
    public int MovieId { get; set; }
    public string MovieTitle { get; set; } = string.Empty;
    public string? MovieUrl { get; set; }
    public List<CsfdCinemaBadge> Badges { get; } = new();
    public List<CsfdShowtime> Showtimes { get; } = new();
}

public class CsfdCinemaBadge
{
    public CsfdBadgeKind Kind { get; set; } = CsfdBadgeKind.Unknown;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public enum CsfdBadgeKind
{
    Unknown,
    Hall,
    Format
}

public class CsfdShowtime
{
    public DateTime StartAt { get; set; }
    public bool TicketsAvailable { get; set; }
    public string? TicketUrl { get; set; }
    public bool IsPast { get; set; }
}
