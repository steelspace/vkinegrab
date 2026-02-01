using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using vkinegrab.Models;
using vkinegrab.Services;
using vkinegrab.Services.Csfd;
using Xunit;

namespace vkinegrab.Tests
{
    public class MovieCollectorServiceTests
    {
        [Fact]
        public async Task CollectMoviesFromSchedules_StoresMissingMoviesAndSkipsExistingOnes()
        {
            using var runner = MongoDbRunner.Start();
            var connectionString = runner.ConnectionString;

            var dbService = new DatabaseService(connectionString);

            // Pre-store a movie with CSFD ID 1
            var existingMovie = new Movie { CsfdId = 1, Title = "Existing" };
            await dbService.StoreMovie(existingMovie);

            // Schedules for movie IDs 1 and 2 (pretend these were loaded from DB)
            var schedules = new[]
            {
                new Schedule { Date = new System.DateOnly(2026, 2, 4), MovieId = 1, MovieTitle = "Existing" },
                new Schedule { Date = new System.DateOnly(2026, 2, 4), MovieId = 2, MovieTitle = "New Movie" }
            };

            // Fake CSFD scraper that returns a simple CsfdMovie for movie ID 2
            var csfdScraper = new FakeCsfdScraper();

            var perfService = new FakePerformancesService(schedules);
            var collector = new MovieCollectorService(perfService, csfdScraper, dbService);

            var (fetched, skipped, failed) = await collector.CollectMoviesFromSchedulesAsync(schedules);

            Assert.Equal(1, fetched);
            Assert.Equal(1, skipped);
            Assert.Equal(0, failed);

            var storedNew = await dbService.GetMovie(2);
            Assert.NotNull(storedNew);
            Assert.Equal("New Movie", storedNew.Title);

            var storedExisting = await dbService.GetMovie(1);
            Assert.NotNull(storedExisting);
            Assert.Equal("Existing", storedExisting.Title);
        }

        [Fact]
        public async Task StoreMovie_DirectInsert_Works()
        {
            using var runner = MongoDbRunner.Start();
            var connectionString = runner.ConnectionString;

            var dbService = new DatabaseService(connectionString);
            var movie = new Movie { CsfdId = 2, Title = "New Movie" };
            await dbService.StoreMovie(movie);

            var stored = await dbService.GetMovie(2);
            Assert.NotNull(stored);
            Assert.Equal("New Movie", stored.Title);
        }

        private class FakePerformancesService : IPerformancesService
        {
            private readonly IReadOnlyList<Schedule> schedules;
            public FakePerformancesService(IEnumerable<Schedule> schedules)
            {
                this.schedules = schedules.ToList();
            }

            public Task<IReadOnlyList<Schedule>> GetSchedules(System.Uri? pageUri = null, string period = "today", System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(schedules);
            }
        }

        private class FakeCsfdScraper : ICsfdScraper
        {
            public Task<CsfdMovie> ScrapeMovie(int movieId)
            {
                var csfd = new CsfdMovie { Id = movieId, Title = movieId == 2 ? "New Movie" : "Unknown" };
                return Task.FromResult(csfd);
            }

            public Task<vkinegrab.Models.TmdbMovie?> ResolveTmdb(CsfdMovie movie)
            {
                return Task.FromResult<vkinegrab.Models.TmdbMovie?>(null);
            }
        }
    }
}
