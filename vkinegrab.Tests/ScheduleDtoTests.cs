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
    }
}
