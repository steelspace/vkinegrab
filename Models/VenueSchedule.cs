namespace vkinegrab.Models;

public class VenueSchedule
{
    public Venue Venue { get; set; } = new Venue();
    public DateOnly? ScheduleDate { get; set; }
    public List<CinemaPerformance> Performances { get; } = new();
}