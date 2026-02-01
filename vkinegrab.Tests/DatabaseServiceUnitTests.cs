using MongoDB.Driver;
using Moq;
using vkinegrab.Models;
using vkinegrab.Models.Dtos;
using vkinegrab.Services;

namespace vkinegrab.Tests
{
    public class DatabaseServiceUnitTests
    {
        [Fact]
        public async Task StoreSchedule_CallsUpdateOne_WithUpsert()
        {
            var mockDb = new Mock<IMongoDatabase>();
            var mockSchedules = new Mock<IMongoCollection<ScheduleDto>>();

            mockDb.Setup(d => d.GetCollection<ScheduleDto>("schedule", null)).Returns(mockSchedules.Object);
            mockDb.Setup(d => d.GetCollection<MovieDto>("movies", null)).Returns(Mock.Of<IMongoCollection<MovieDto>>());

            var service = new DatabaseService(mockDb.Object);

            var schedule = new Schedule { Date = new System.DateOnly(2026,2,3), MovieId = 42, MovieTitle = "Test" };

            mockSchedules.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<ScheduleDto>>(),
                It.IsAny<UpdateDefinition<ScheduleDto>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync((UpdateResult?)null);

            await service.StoreSchedule(schedule);

            mockSchedules.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<ScheduleDto>>(),
                It.IsAny<UpdateDefinition<ScheduleDto>>(),
                It.Is<UpdateOptions>(o => o.IsUpsert),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task StoreSchedule_ThrowsInvalidOperationException_OnMongoException()
        {
            var mockDb = new Mock<IMongoDatabase>();
            var mockSchedules = new Mock<IMongoCollection<ScheduleDto>>();

            mockDb.Setup(d => d.GetCollection<ScheduleDto>("schedule", null)).Returns(mockSchedules.Object);
            mockDb.Setup(d => d.GetCollection<MovieDto>("movies", null)).Returns(Mock.Of<IMongoCollection<MovieDto>>());

            var service = new DatabaseService(mockDb.Object);

            var schedule = new Schedule { Date = new System.DateOnly(2026,2,3), MovieId = 42, MovieTitle = "Test" };

            mockSchedules.Setup(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<ScheduleDto>>(),
                It.IsAny<UpdateDefinition<ScheduleDto>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>())).ThrowsAsync(new MongoException("boom"));

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.StoreSchedule(schedule));
        }
    }
}
