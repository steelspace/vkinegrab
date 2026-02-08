using Mongo2Go;
using vkinegrab.Models;
using vkinegrab.Services;
using vkinegrab.Services.Csfd;

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
            var existingMovie = new Movie { CsfdId = 1, Title = "Existing", ImdbId = "tt-existing" };
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
            var collector = new MovieCollectorService(csfdScraper, dbService);

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
            // No CSFD fetch for the already stored movie (skipped), so no ReceivedExistingImdbId is expected
            Assert.Null(csfdScraper.ReceivedExistingImdbId);
        }

        [Fact]
        public async Task CollectMoviesFromSchedules_UpdatesRecentlyReleasedMovieDaily()
        {
            using var runner = MongoDbRunner.Start();
            var connectionString = runner.ConnectionString;

            var dbService = new DatabaseService(connectionString);

            // Pre-store a movie that premiered 7 days ago, but last stored 2 days ago -> should be fetched (daily updates)
            var releaseDate = DateTime.UtcNow.Date.AddDays(-7);
            var storedAt = DateTime.UtcNow.Date.AddDays(-2);
            var existingMovie = new Movie { CsfdId = 10, Title = "Recent", ReleaseDate = releaseDate, StoredAt = storedAt };
            await dbService.StoreMovie(existingMovie);

            var schedules = new[] { new Schedule { Date = new System.DateOnly(2026, 2, 4), MovieId = 10, MovieTitle = "Recent" } };

            var csfdScraper = new FakeCsfdScraper();
            var collector = new MovieCollectorService(csfdScraper, dbService);

            var (fetched, skipped, failed) = await collector.CollectMoviesFromSchedulesAsync(schedules);

            Assert.Equal(1, fetched);
            Assert.Equal(0, skipped);
            Assert.Equal(0, failed);
        }

        [Fact]
        public async Task CollectMoviesFromSchedules_UsesExistingIds_WhenPresent()
        {
            using var runner = MongoDbRunner.Start();
            var connectionString = runner.ConnectionString;

            var dbService = new DatabaseService(connectionString);

            // Pre-store a movie that premiered 7 days ago and has both IDs; make it stale so collector will try to update
            var releaseDate = DateTime.UtcNow.Date.AddDays(-7);
            var storedAt = DateTime.UtcNow.Date.AddDays(-10);
            var existingMovie = new Movie { CsfdId = 20, Title = "WithIds", ReleaseDate = releaseDate, StoredAt = storedAt, TmdbId = 500, ImdbId = "tt500" };
            await dbService.StoreMovie(existingMovie);

            var schedules = new[] { new Schedule { Date = new System.DateOnly(2026, 2, 4), MovieId = 20, MovieTitle = "WithIds" } };

            var csfdScraper = new SpyCsfdScraper();
            var collector = new MovieCollectorService(csfdScraper, dbService);

            var (fetched, skipped, failed) = await collector.CollectMoviesFromSchedulesAsync(schedules);

            Assert.Equal(1, fetched);
            Assert.Equal(0, skipped);
            Assert.Equal(0, failed);

            // CSFD is fetched (recent release), TMDB should be resolved at that time
            Assert.True(csfdScraper.ResolveCalled, "ResolveTmdb should be called when CSFD is fetched");

            var stored = await dbService.GetMovie(20);
            Assert.NotNull(stored);
            Assert.Equal(500, stored.TmdbId);
            Assert.Equal("tt500", stored.ImdbId);
        }

        [Fact]
        public async Task CollectMoviesFromSchedules_RefreshesTmdbMetadataById()
        {
            using var runner = MongoDbRunner.Start();
            var connectionString = runner.ConnectionString;

            var dbService = new DatabaseService(connectionString);

            // Pre-store a movie with TmdbId and make it stale (older than monthly interval)
            var releaseDate = DateTime.UtcNow.Date.AddYears(-2);
            var storedAt = DateTime.UtcNow.Date.AddDays(-40); // older than monthly interval
            var existingMovie = new Movie { CsfdId = 30, Title = "RefreshMe", ReleaseDate = releaseDate, StoredAt = storedAt, TmdbId = 501 };
            await dbService.StoreMovie(existingMovie);

            var schedules = new[] { new Schedule { Date = new System.DateOnly(2026, 2, 4), MovieId = 30, MovieTitle = "RefreshMe" } };

            var csfdScraper = new SpyCsfdScraper();
            var collector = new MovieCollectorService(csfdScraper, dbService);

            var (fetched, skipped, failed) = await collector.CollectMoviesFromSchedulesAsync(schedules);

            Assert.Equal(1, fetched);
            Assert.Equal(0, skipped);
            Assert.Equal(0, failed);

            // CSFD was fetched, TMDB should be resolved (not fetched by id as separate logic is removed)
            Assert.True(csfdScraper.ResolveCalled, "ResolveTmdb should be called when CSFD is fetched");
            Assert.False(csfdScraper.FetchByIdCalled, "FetchTmdbById should not be called as separate refresh logic was removed");

            var stored = await dbService.GetMovie(30);
            Assert.NotNull(stored);
            Assert.Equal(9.1, stored.VoteAverage);
            Assert.Equal(99.0, stored.Popularity);
            Assert.Equal("https://image.tmdb.org/t/p/original/resolved.jpg", stored.PosterUrl);
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

            public Task<(IReadOnlyList<Schedule> Schedules, IReadOnlyList<Venue> Venues)> GetSchedulesWithVenues(System.Uri? pageUri = null, string period = "today", System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(((IReadOnlyList<Schedule>)schedules, (IReadOnlyList<Venue>)Array.Empty<Venue>()));
            }
        }

        private class FakeCsfdScraper : ICsfdScraper
        {
                public string? ReceivedExistingImdbId { get; private set; }

                public Task<CsfdMovie> ScrapeMovie(int movieId, string? existingImdbId = null)
                {
                    ReceivedExistingImdbId = existingImdbId;
                    var csfd = new CsfdMovie { Id = movieId, Title = movieId == 2 ? "New Movie" : "Unknown" };
                    if (!string.IsNullOrEmpty(existingImdbId)) csfd.ImdbId = existingImdbId;
                    return Task.FromResult(csfd);
                }

            public Task<vkinegrab.Models.TmdbMovie?> ResolveTmdb(CsfdMovie movie)
            {
                return Task.FromResult<vkinegrab.Models.TmdbMovie?>(null);
            }

            public Task<vkinegrab.Models.TmdbMovie?> FetchTmdbById(int tmdbId)
            {
                return Task.FromResult<vkinegrab.Models.TmdbMovie?>(null);
            }

            public Task<Venue> ScrapeVenue(int venueId)
            {
                return Task.FromResult(new Venue { Id = venueId, Name = string.Empty });
            }

            public Task<Venue> ScrapeVenue(string url)
            {
                return Task.FromResult(new Venue { Id = 0, DetailUrl = url });
            }
        }

        private class SpyCsfdScraper : ICsfdScraper
        {
            public bool ResolveCalled { get; private set; }
            public bool FetchByIdCalled { get; private set; }

            public Task<CsfdMovie> ScrapeMovie(int movieId, string? existingImdbId = null)
            {
                // Return a generic CSFD movie
                var csfd = new CsfdMovie { Id = movieId, Title = "WithIds" };
                if (!string.IsNullOrEmpty(existingImdbId)) csfd.ImdbId = existingImdbId;
                return Task.FromResult(csfd);
            }

            public Task<vkinegrab.Models.TmdbMovie?> ResolveTmdb(CsfdMovie movie)
            {
                ResolveCalled = true;
                var tmdb = new vkinegrab.Models.TmdbMovie
                {
                    Id = 500,
                    Title = "TMDB Resolved",
                    Overview = "Overview",
                    PosterPath = "/resolved.jpg",
                    BackdropPath = "/backdrop.jpg",
                    VoteAverage = 9.1,
                    VoteCount = 500,
                    Popularity = 99.0,
                    OriginalLanguage = "en",
                    Adult = false,
                    ReleaseDate = DateTime.UtcNow.AddYears(-2).ToString("yyyy-MM-dd")
                };
                return Task.FromResult<vkinegrab.Models.TmdbMovie?>(tmdb);
            }

            public Task<vkinegrab.Models.TmdbMovie?> FetchTmdbById(int tmdbId)
            {
                FetchByIdCalled = true;
                var tmdb = new vkinegrab.Models.TmdbMovie
                {
                    Id = tmdbId,
                    Title = "TMDB From Id",
                    Overview = "Overview",
                    PosterPath = "/fetched.jpg",
                    BackdropPath = "/backdrop.jpg",
                    VoteAverage = 9.1,
                    VoteCount = 500,
                    Popularity = 99.0,
                    OriginalLanguage = "en",
                    Adult = false,
                    ReleaseDate = DateTime.UtcNow.AddYears(-2).ToString("yyyy-MM-dd")
                };
                return Task.FromResult<vkinegrab.Models.TmdbMovie?>(tmdb);
            }

            public Task<Venue> ScrapeVenue(int venueId)
            {
                return Task.FromResult(new Venue { Id = venueId, Name = string.Empty });
            }

            public Task<Venue> ScrapeVenue(string url)
            {
                return Task.FromResult(new Venue { Id = 0, DetailUrl = url });
            }
        }

        [Fact]
        public async Task CollectMoviesFromSchedules_3To12MonthOld_WeeklyInterval()
        {
            using var runner = MongoDbRunner.Start();
            var connectionString = runner.ConnectionString;

            var dbService = new DatabaseService(connectionString);

            // Release date 6 months ago
            var releaseDate = DateTime.UtcNow.Date.AddMonths(-6);

            // Stored 5 days ago -> should be skipped (weekly interval)
            var storedRecent = DateTime.UtcNow.Date.AddDays(-5);
            var existing = new Movie { CsfdId = 100, Title = "SixMonths", ReleaseDate = releaseDate, StoredAt = storedRecent };
            await dbService.StoreMovie(existing);

            var schedules = new[] { new Schedule { Date = new System.DateOnly(2026, 2, 4), MovieId = 100, MovieTitle = "SixMonths" } };

            var csfdScraper = new FakeCsfdScraper();
            var collector = new MovieCollectorService(csfdScraper, dbService);

            var (fetched1, skipped1, failed1) = await collector.CollectMoviesFromSchedulesAsync(schedules);

            Assert.Equal(0, fetched1);
            Assert.Equal(1, skipped1);
            Assert.Equal(0, failed1);

            // Stored 8 days ago -> should be fetched (week passed)
            var storedOld = DateTime.UtcNow.Date.AddDays(-8);
            existing.StoredAt = storedOld;
            await dbService.StoreMovie(existing);

            var (fetched2, skipped2, failed2) = await collector.CollectMoviesFromSchedulesAsync(schedules);

            Assert.Equal(1, fetched2);
            Assert.Equal(0, skipped2);
            Assert.Equal(0, failed2);
        }

        [Fact]
        public async Task CollectMoviesFromSchedules_OlderThan12Months_TwoWeekInterval()
        {
            using var runner = MongoDbRunner.Start();
            var connectionString = runner.ConnectionString;

            var dbService = new DatabaseService(connectionString);

            // Release date 2 years ago
            var releaseDate = DateTime.UtcNow.Date.AddYears(-2);

            // Stored 10 days ago -> should be skipped (2-week interval)
            var storedRecent = DateTime.UtcNow.Date.AddDays(-10);
            var existing = new Movie { CsfdId = 200, Title = "OldMovie", ReleaseDate = releaseDate, StoredAt = storedRecent };
            await dbService.StoreMovie(existing);

            var schedules = new[] { new Schedule { Date = new System.DateOnly(2026, 2, 4), MovieId = 200, MovieTitle = "OldMovie" } };

            var csfdScraper = new FakeCsfdScraper();
            var collector = new MovieCollectorService(csfdScraper, dbService);

            var (fetched1, skipped1, failed1) = await collector.CollectMoviesFromSchedulesAsync(schedules);

            Assert.Equal(0, fetched1);
            Assert.Equal(1, skipped1);
            Assert.Equal(0, failed1);

            // Stored 15 days ago -> should be fetched (two-week passed)
            var storedOld = DateTime.UtcNow.Date.AddDays(-15);
            existing.StoredAt = storedOld;
            await dbService.StoreMovie(existing);

            var (fetched2, skipped2, failed2) = await collector.CollectMoviesFromSchedulesAsync(schedules);

            Assert.Equal(1, fetched2);
            Assert.Equal(0, skipped2);
            Assert.Equal(0, failed2);
        }
    }
}