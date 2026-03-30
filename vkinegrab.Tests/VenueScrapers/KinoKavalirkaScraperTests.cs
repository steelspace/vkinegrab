using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using vkinegrab.Services.Csfd;
using vkinegrab.Services.VenueScrapers.Impl;
using Xunit;

namespace vkinegrab.Tests.VenueScrapers;

public class KinoKavalirkaScraperTests
{
    private static KinoKavalirkaScraper CreateScraper()
    {
        var html = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "VenueScrapers", "Fixtures", "KinoKavalirka.html"));
        var mock = new Mock<IHtmlFetcher>();
        mock.Setup(f => f.FetchAsync(It.IsAny<Uri>(), default)).ReturnsAsync(html);
        return new KinoKavalirkaScraper(mock.Object, NullLogger<KinoKavalirkaScraper>.Instance);
    }

    [Fact]
    public async Task ParsesThreeScreenings()
    {
        var result = await CreateScraper().ScrapeAsync();
        Assert.Equal(3, result.Schedules.Count);
    }

    [Fact]
    public async Task ParsesPipeSeparatedDateTime()
    {
        var result = await CreateScraper().ScrapeAsync();

        var film = result.Schedules.First(s => s.MovieTitle == "Otec Matka Sestra Bratr");
        Assert.Equal(new TimeOnly(18, 0), film.Performances[0].Showtimes[0].StartAt);
    }

    [Fact]
    public async Task TicketUrlIsKavalirka()
    {
        var result = await CreateScraper().ScrapeAsync();

        var film = result.Schedules.First(s => s.MovieTitle == "Otec Matka Sestra Bratr");
        Assert.Contains("kinokavalirka.cz/cs/koupit", film.Performances[0].Showtimes[0].TicketUrl);
    }
}
