using HtmlAgilityPack;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using vkinegrab.Services.Csfd;
using vkinegrab.Services.Imdb;
using vkinegrab.Models;
using Xunit;

namespace vkinegrab.Tests
{
    public class CsfdScraperTests
    {
        [Fact]
        public async Task ScrapeMovie_UsesValidateImdbById_WhenExistingProvided()
        {
            // Arrange: make an HttpClient that returns a minimal film page
            var handler = new TestHttpMessageHandler("<html><body><h1>Test Movie</h1></body></html>");
            var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://www.csfd.cz/") };

            var spyImdb = new SpyImdbResolver();
            var fakeTmdb = new FakeTmdbResolver();

            var scraper = new CsfdScraper(client, fakeTmdb, spyImdb);

            // Act: call ScrapeMovie with existing imdb id
            var csfd = await scraper.ScrapeMovie("https://www.csfd.cz/film/123", "tt123");

            // Assert: ValidateImdbId called and ResolveImdbId not called
            Assert.True(spyImdb.ValidateCalled);
            Assert.False(spyImdb.ResolveCalled);
            Assert.Equal("tt123", csfd.ImdbId);
        }

        private class SpyImdbResolver : IImdbResolver
        {
            public bool ResolveCalled { get; private set; }
            public bool ValidateCalled { get; private set; }

            public Task<string?> ResolveImdbId(HtmlDocument csfdDoc, CsfdMovie movie)
            {
                ResolveCalled = true;
                return Task.FromResult<string?>(null);
            }

            public Task<bool> ValidateImdbId(string imdbId, CsfdMovie movie)
            {
                ValidateCalled = true;
                return Task.FromResult(true);
            }
        }

        private class FakeTmdbResolver : vkinegrab.Services.Tmdb.ITmdbResolver
        {
            public Task<vkinegrab.Models.TmdbMovie?> ResolveTmdbMovie(CsfdMovie movie)
            {
                return Task.FromResult<vkinegrab.Models.TmdbMovie?>(null);
            }

            public Task<vkinegrab.Models.TmdbMovie?> GetMovieById(int tmdbId)
            {
                return Task.FromResult<vkinegrab.Models.TmdbMovie?>(null);
            }
        }

        private class TestHttpMessageHandler : HttpMessageHandler
        {
            private readonly string html;
            public TestHttpMessageHandler(string html) => this.html = html;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(html)
                };
                return Task.FromResult(response);
            }
        }
    }
}