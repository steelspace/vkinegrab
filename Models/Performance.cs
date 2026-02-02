namespace vkinegrab.Models;

public class Performance
{
    public int MovieId { get; set; }
    public string MovieTitle { get; set; } = string.Empty;
    public string? MovieUrl { get; set; }
    public int VenueId { get; set; }
    public List<CinemaBadge> Badges { get; } = new();
    public List<Showtime> Showtimes { get; } = new();
}

public class CinemaBadge
{
    public BadgeKind Kind { get; set; } = BadgeKind.Unknown;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public enum BadgeKind
{
    Unknown,
    Hall,
    Format
}

public class Showtime
{
    public DateTime StartAt { get; set; }
    public bool TicketsAvailable { get; set; }
    public string? TicketUrl { get; set; }
}
