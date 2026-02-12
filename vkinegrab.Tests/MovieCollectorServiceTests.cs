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
            var existingMovie = new Movie { CsfdId = 1, Title = "Existing" };
            await dbService.StoreMovie(existingMovie);

            // Schedules for movie IDs 1 and 2 (pretend these were loaded from DB)
            var schedules = new[]
            {
                new Schedule { Date = new System.DateOnly(2026, 2, 4), MovieId = 1, MovieTitle = "Existing" },
                new Schedule { Date = new System.DateOnly(2026, 2, 4), MovieId = 2, MovieTitle = "New Movie" }
            };

            // Fake orchestrator that returns a simple Movie for movie ID 2
            var orchestrator = new FakeMovieMetadataOrchestrator();

            var perfService = new FakePerformancesService(schedules);
            var collector = new MovieCollectorService(orchestrator, dbService);

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

            var orchestrator = new FakeMovieMetadataOrchestrator();
            var collector = new MovieCollectorService(orchestrator, dbService);

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

            var orchestrator = new SpyMovieMetadataOrchestrator();
            var collector = new MovieCollectorService(orchestrator, dbService);

            var (fetched, skipped, failed) = await collector.CollectMoviesFromSchedulesAsync(schedules);

            Assert.Equal(1, fetched);
            Assert.Equal(0, skipped);
            Assert.Equal(0, failed);

            Assert.False(orchestrator.ResolveCalled, "ResolveTmdb should not be called when existing movie already has TmdbId and ImdbId");

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

            var orchestrator = new SpyMovieMetadataOrchestrator();
            var collector = new MovieCollectorService(orchestrator, dbService);

            var (fetched, skipped, failed) = await collector.CollectMoviesFromSchedulesAsync(schedules);

            Assert.Equal(1, fetched);
            Assert.Equal(0, skipped);
            Assert.Equal(0, failed);

            Assert.False(orchestrator.ResolveCalled, "ResolveTmdb should not be called when we have TmdbId");
            Assert.True(orchestrator.FetchByIdCalled, "FetchTmdbById should be called to refresh metadata by TmdbId");

            var stored = await dbService.GetMovie(30);
            Assert.NotNull(stored);
            Assert.Equal(9.1, stored.VoteAverage);
            Assert.Equal(99.0, stored.Popularity);
            Assert.Equal("https://image.tmdb.org/t/p/original/fetched.jpg", stored.PosterUrl);
            Assert.Equal("https://csfd.cz/poster.jpg", stored.CsfdPosterUrl);
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

        private class FakeMovieMetadataOrchestrator : IMovieMetadataOrchestrator
        {
            public Task<Movie> ResolveMovieMetadataAsync(int csfdId, Movie? existingMovie, CancellationToken ct = default)
            {
                var movie = existingMovie ?? new Movie { CsfdId = csfdId, Title = "Unknown" };
                if (csfdId == 2) movie.Title = "New Movie";
                movie.StoredAt = DateTime.UtcNow;
                return Task.FromResult(movie);
            }
        }

        private class SpyMovieMetadataOrchestrator : IMovieMetadataOrchestrator
        {
            public bool ResolveCalled { get; private set; }
            public bool FetchByIdCalled { get; private set; }

            public Task<Movie> ResolveMovieMetadataAsync(int csfdId, Movie? existingMovie, CancellationToken ct = default)
            {
                // Simulate old logic for testing skip/fetch in collector
                var movie = existingMovie ?? new Movie { CsfdId = csfdId, Title = "WithIds" };

                if (existingMovie != null && existingMovie.TmdbId != 0 && !string.IsNullOrEmpty(existingMovie.ImdbId))
                {
                    // Neither ResolveCalled nor FetchByIdCalled
                }
                else if (existingMovie != null && existingMovie.TmdbId != 0)
                {
                    FetchByIdCalled = true;
                    movie.VoteAverage = 9.1;
                    movie.Popularity = 99.0;
                    movie.PosterUrl = "https://image.tmdb.org/t/p/original/fetched.jpg";
                    movie.CsfdPosterUrl = "https://csfd.cz/poster.jpg";
                }
                else
                {
                    ResolveCalled = true;
                }

                movie.StoredAt = DateTime.UtcNow;
                return Task.FromResult(movie);
            }
        }
    }
}
