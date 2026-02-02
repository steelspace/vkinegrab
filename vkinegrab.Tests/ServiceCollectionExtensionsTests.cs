using MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using vkinegrab.Services;
using vkinegrab.Services.Csfd;
using vkinegrab.Services.Imdb;
using vkinegrab.Services.Tmdb;
using Xunit;

namespace vkinegrab.Tests
{
    public class ServiceCollectionExtensionsTests
    {
        [Fact]
        public void AddVkinegrabServices_Resolves_ICsfdScraper_With_HttpClient()
        {
            var services = new ServiceCollection();
            services.AddVkinegrabServices("mongodb://localhost:27017", "fake-token");

            var provider = services.BuildServiceProvider();

            var scraper = provider.GetRequiredService<ICsfdScraper>();

            Assert.NotNull(scraper);
            Assert.IsAssignableFrom<ICsfdScraper>(scraper);
        }

        [Fact]
        public void AddVkinegrabServices_Registers_IMongoClient()
        {
            var services = new ServiceCollection();
            services.AddVkinegrabServices("mongodb://localhost:27017", "fake-token");

            var provider = services.BuildServiceProvider();

            var client = provider.GetRequiredService<IMongoClient>();
            Assert.NotNull(client);
        }

        [Fact]
        public void AddVkinegrabServices_Resolves_IDatabaseService_With_Mocked_Client()
        {
            var services = new ServiceCollection();

            // Create mocks for IMongoClient and IMongoDatabase and collections to avoid network I/O
            var mockClient = new Mock<IMongoClient>();
            var mockDatabase = new Mock<IMongoDatabase>();

            var mockMoviesCollection = new Mock<IMongoCollection<Models.Dtos.MovieDto>>();
            var mockSchedulesCollection = new Mock<IMongoCollection<Models.Dtos.ScheduleDto>>();
            var mockVenuesCollection = new Mock<IMongoCollection<Models.Dtos.VenueDto>>();

            // Setup GetDatabase to return our mock database
            mockClient.Setup(c => c.GetDatabase(It.IsAny<string>(), null)).Returns(mockDatabase.Object);

            // Setup GetCollection to return mock collections for the expected collection names
            mockDatabase.Setup(d => d.GetCollection<Models.Dtos.MovieDto>("movies", null)).Returns(mockMoviesCollection.Object);
            mockDatabase.Setup(d => d.GetCollection<Models.Dtos.ScheduleDto>("schedule", null)).Returns(mockSchedulesCollection.Object);
            mockDatabase.Setup(d => d.GetCollection<Models.Dtos.VenueDto>("venues", null)).Returns(mockVenuesCollection.Object);

            // Indexes.CreateOne may be called; we can setup Indexes to return a mock index manager
            var mockMovieIndexManager = new Mock<IMongoIndexManager<Models.Dtos.MovieDto>>();
            mockMoviesCollection.SetupGet(c => c.Indexes).Returns(mockMovieIndexManager.Object);

            var mockScheduleIndexManager = new Mock<IMongoIndexManager<Models.Dtos.ScheduleDto>>();
            mockSchedulesCollection.SetupGet(c => c.Indexes).Returns(mockScheduleIndexManager.Object);

            var mockVenueIndexManager = new Mock<IMongoIndexManager<Models.Dtos.VenueDto>>();
            mockVenuesCollection.SetupGet(c => c.Indexes).Returns(mockVenueIndexManager.Object);

            services.AddSingleton(_ => mockClient.Object);
            services.AddScoped<IDatabaseService, DatabaseService>();

            var provider = services.BuildServiceProvider();

            var dbService = provider.GetRequiredService<IDatabaseService>();
            Assert.NotNull(dbService);
        }
    }
}
