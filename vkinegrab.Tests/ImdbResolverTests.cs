using HtmlAgilityPack;
using vkinegrab.Models;
using vkinegrab.Services.Imdb;
using Xunit;

namespace vkinegrab.Tests
{
    public class ImdbResolverTests
    {
        [Fact]
        public async Task ResolveImdbId_ForCsfd4983_ShouldFindImdbId()
        {
            // Load the actual HTML from CSFD film 4983
            var html = File.ReadAllText("/tmp/csfd_4983.html");
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var movie = new CsfdMovie
            {
                Id = 4983,
                Title = "Ucho",
                Year = "1970",
                Directors = new List<string> { "Karel Kachyňa" },
                Origin = "Československo",
                LocalizedTitles = new Dictionary<string, string>
                {
                    ["angličtina"] = "The Ear",
                    ["Polsko"] = "Ucho"
                }
            };

            // Use a real HttpClient for IMDb search (integration test)
            var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            var resolver = new ImdbResolver(client);

            var imdbId = await resolver.ResolveImdbId(doc, movie);

            // The movie "Ucho" (The Ear) 1970 should have IMDb ID tt0066498
            Assert.Equal("tt0066498", imdbId);
        }
    }
}