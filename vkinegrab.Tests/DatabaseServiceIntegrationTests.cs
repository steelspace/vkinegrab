using System.Linq;
using System.Threading.Tasks;
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
    }
}
