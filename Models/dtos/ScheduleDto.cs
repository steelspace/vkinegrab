using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace vkinegrab.Models.Dtos;

public class ScheduleDto
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("date")]
    public DateTime Date { get; set; }

    [BsonElement("movie_id")]
    public int MovieId { get; set; }

    [BsonElement("movie_title")]
    public string? MovieTitle { get; set; }

    [BsonElement("performances")]
    public List<PerformanceDto> Performances { get; set; } = new();

    [BsonElement("stored_at")]
    public DateTime StoredAt { get; set; }
} 

public class PerformanceDto
{
    [BsonElement("venue_id")]
    public int VenueId { get; set; }

    [BsonElement("showtimes")]
    public List<ShowtimeDto> Showtimes { get; set; } = new();
} 

public class CinemaBadgeDto
{
    [BsonElement("kind")]
    public BadgeKind Kind { get; set; }

    [BsonElement("code")]
    public string Code { get; set; } = string.Empty;

    [BsonElement("description")]
    public string? Description { get; set; }
} 

public class ShowtimeDto
{
    [BsonElement("start_at")]
    public TimeOnly StartAt { get; set; }

    [BsonElement("tickets_available")]
    public bool TicketsAvailable { get; set; }

    [BsonElement("ticket_url")]
    public string? TicketUrl { get; set; }

    [BsonElement("is_past")]
    public bool IsPast { get; set; }

    [BsonElement("badges")]
    public List<CinemaBadgeDto> Badges { get; set; } = new();
} 

internal static class ScheduleDtoExtensions
{
    public static ScheduleDto ToDto(this Schedule schedule)
        => new ScheduleDto
        {
            // Ensure a fresh, non-zero ObjectId so upserts don't try to insert the zero ObjectId repeatedly
            Id = ObjectId.GenerateNewId(),
            Date = schedule.Date.ToDateTime(new TimeOnly(0, 0), DateTimeKind.Utc),
            MovieId = schedule.MovieId,
            MovieTitle = schedule.MovieTitle,
            Performances = schedule.Performances.Select(p => new PerformanceDto
            {
                VenueId = p.VenueId,
                Showtimes = p.Showtimes.Select(s => new ShowtimeDto 
                { 
                    StartAt = s.StartAt, 
                    TicketsAvailable = s.TicketsAvailable, 
                    TicketUrl = s.TicketUrl,
                    Badges = s.Badges.Select(b => new CinemaBadgeDto { Kind = b.Kind, Code = b.Code, Description = b.Description }).ToList()
                }).ToList()
            }).ToList(),
            StoredAt = DateTime.UtcNow
        };

    public static Schedule ToSchedule(this ScheduleDto dto)
        => new Schedule
        {
            Date = DateOnly.FromDateTime(dto.Date.ToUniversalTime()),
            MovieId = dto.MovieId,
            MovieTitle = dto.MovieTitle ?? string.Empty,
            StoredAt = dto.StoredAt
        };

    public static void Populate(this Schedule target, ScheduleDto dto)
    {
        target.Performances.Clear();
        foreach (var p in dto.Performances)
        {
            var perf = new Performance
            {
                VenueId = p.VenueId
            };

            foreach (var s in p.Showtimes)
            {
                var showtime = new Showtime 
                { 
                    StartAt = s.StartAt, 
                    TicketsAvailable = s.TicketsAvailable, 
                    TicketUrl = s.TicketUrl 
                };

                foreach (var b in s.Badges)
                {
                    showtime.Badges.Add(new CinemaBadge { Kind = b.Kind, Code = b.Code, Description = b.Description });
                }

                perf.Showtimes.Add(showtime);
            }

            target.Performances.Add(perf);
        }
    }
}