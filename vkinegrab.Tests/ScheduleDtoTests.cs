using System;
using MongoDB.Bson;
using vkinegrab.Models;
using vkinegrab.Models.Dtos;
using Xunit;

namespace vkinegrab.Tests
{
    public class ScheduleDtoTests
    {
        [Fact]
        public void ToDto_GeneratesNonZeroId_And_SetsDateAndStoredAt()
        {
            var schedule = new Schedule
            {
                Date = new DateOnly(2026, 2, 3),
                MovieId = 123,
                MovieTitle = "Test Movie"
            };

            var dto = schedule.ToDto();

            Assert.NotEqual(ObjectId.Empty, dto.Id);
            Assert.Equal(new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc), dto.Date.ToUniversalTime());
            Assert.True((DateTime.UtcNow - dto.StoredAt).TotalMinutes < 1, "StoredAt should be recent (within 1 minute)");
        }

        [Fact]
        public void ToDto_PreservesTimeAsIs_And_Populate_RoundTrips()
        {
            // Arrange
            var date = new DateOnly(2026, 2, 3);
            var time = new TimeOnly(18, 0);
            
            var schedule = new Schedule
            {
                Date = date,
                MovieId = 1,
            };
            var performance = new Performance { VenueId = 100 };
            performance.Showtimes.Add(new Showtime { StartAt = time, TicketsAvailable = true });
            schedule.Performances.Add(performance);

            // Act -> ToDto
            var dto = schedule.ToDto();
            
            // Assert DTO preserves time as-is
            var dtoShowtime = dto.Performances[0].Showtimes[0];
            Assert.Equal(time, dtoShowtime.StartAt);

            // Roundtrip
            var newSchedule = new Schedule();
            newSchedule.Populate(dto);
            
            var roundTripShowtime = newSchedule.Performances[0].Showtimes[0];
            
            // Assert roundtrip preserves the time
            Assert.Equal(time, roundTripShowtime.StartAt);
        }
    }
}
