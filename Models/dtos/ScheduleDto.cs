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

    [BsonElement("venue_url")]
    public string? VenueUrl { get; set; }

    [BsonElement("badges")]
    public List<CinemaBadgeDto> Badges { get; set; } = new();

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
    public DateTime StartAt { get; set; }

    [BsonElement("tickets_available")]
    public bool TicketsAvailable { get; set; }

    [BsonElement("ticket_url")]
    public string? TicketUrl { get; set; }

    [BsonElement("is_past")]
    public bool IsPast { get; set; }
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
                VenueUrl = p.VenueUrl,
                Badges = p.Badges.Select(b => new CinemaBadgeDto { Kind = b.Kind, Code = b.Code, Description = b.Description }).ToList(),
                Showtimes = p.Showtimes.Select(s => new ShowtimeDto { StartAt = DateTime.SpecifyKind(s.StartAt, DateTimeKind.Utc), TicketsAvailable = s.TicketsAvailable, TicketUrl = s.TicketUrl }).ToList()
            }).ToList(),
            StoredAt = DateTime.UtcNow
        };

    public static Schedule ToSchedule(this ScheduleDto dto)
        => new Schedule
        {
            Date = DateOnly.FromDateTime(dto.Date.ToUniversalTime()),
            MovieId = dto.MovieId,
            MovieTitle = dto.MovieTitle ?? string.Empty,
        };

    public static void Populate(this Schedule target, ScheduleDto dto)
    {
        target.Performances.Clear();
        foreach (var p in dto.Performances)
        {
            var perf = new Performance
            {
                VenueId = p.VenueId,
                VenueUrl = p.VenueUrl
            };

            foreach (var b in p.Badges)
            {
                perf.Badges.Add(new CinemaBadge { Kind = b.Kind, Code = b.Code, Description = b.Description });
            }

            foreach (var s in p.Showtimes)
            {
                perf.Showtimes.Add(new Showtime { StartAt = DateTime.SpecifyKind(s.StartAt, DateTimeKind.Utc), TicketsAvailable = s.TicketsAvailable, TicketUrl = s.TicketUrl });
            }

            target.Performances.Add(perf);
        }
    }
}