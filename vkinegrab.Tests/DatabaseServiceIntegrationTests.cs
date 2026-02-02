using System.Linq;
using System.Threading.Tasks;
using Moq;
using Mongo2Go;
using MongoDB.Driver;
using vkinegrab.Models;
using vkinegrab.Models.Dtos;
using vkinegrab.Services;
using Xunit;

namespace vkinegrab.Tests
{
    public class DatabaseServiceIntegrationTests
    {
        [Fact]
        public async Task StoreSchedule_UpsertsAndCanBeQueried()
        {
            using var runner = MongoDbRunner.Start();
            var connectionString = runner.ConnectionString;

            var service = new DatabaseService(connectionString);

            var schedule = new Schedule
            {
                Date = new System.DateOnly(2026, 2, 3),
                MovieId = 9001,
                MovieTitle = "Integration Test"
            };

            await service.StoreSchedule(schedule);

            var client = new MongoClient(connectionString);
            var db = client.GetDatabase("movies");
            var coll = db.GetCollection<ScheduleDto>("schedule");

            var found = await coll.Find(s => s.MovieId == 9001 && s.Date == schedule.Date.ToDateTime(new System.TimeOnly(0,0), System.DateTimeKind.Utc)).FirstOrDefaultAsync();

            Assert.NotNull(found);
            Assert.Equal(9001, found.MovieId);
            Assert.Equal("Integration Test", found.MovieTitle);
        }

        [Fact]
        public async Task SchedulesStoreService_StoresVenues_And_Deduplicates_InMongo()
        {
            using var runner = MongoDbRunner.Start();
            var connectionString = runner.ConnectionString;

            var databaseService = new DatabaseService(connectionString);
            var perfMock = Mock.Of<vkinegrab.Services.IPerformancesService>();
            var storeService = new SchedulesStoreService(perfMock, databaseService);

            var schedules = new[]
            {
                new Schedule { Date = new System.DateOnly(2026,2,10), MovieId = 777, MovieTitle = "Int" }
            };

            var venues = new[]
            {
                new Venue { Id = 200, Name = "Venue A" },
                new Venue { Id = 200, Name = "Venue A Duplicate" },
                new Venue { Id = 201, Name = "Venue B" }
            };

            var (storedSchedules, failedSchedules, storedVenues, failedVenues) = await storeService.StoreSchedulesAndVenuesAsync(schedules, venues);

            Assert.Equal(1, storedSchedules);
            Assert.Equal(0, failedSchedules);
            Assert.Equal(2, storedVenues);
            Assert.Equal(0, failedVenues);

            var client = new MongoClient(connectionString);
            var db = client.GetDatabase("movies");
            var coll = db.GetCollection<VenueDto>("venues");

            var found = await coll.Find(_ => true).ToListAsync();
            Assert.Equal(2, found.Count);
            Assert.Contains(found, v => v.VenueId == 200);
            Assert.Contains(found, v => v.VenueId == 201);
        }
    }
}
