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

public class KinoRadotinScraperTests
{
    private static KinoRadotinScraper CreateScraper()
    {
        var html = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "VenueScrapers", "Fixtures", "KinoRadotin.html"));
        var mock = new Mock<IHtmlFetcher>();
        mock.Setup(f => f.FetchAsync(It.IsAny<Uri>(), default)).ReturnsAsync(html);
        return new KinoRadotinScraper(mock.Object, NullLogger<KinoRadotinScraper>.Instance);
    }

    [Fact]
    public async Task ParsesThreeMovies()
    {
        var result = await CreateScraper().ScrapeAsync();
        Assert.Equal(3, result.Schedules.Count);
    }

    [Fact]
    public async Task ParsesTwoShowtimesForMario()
    {
        var result = await CreateScraper().ScrapeAsync();

        var mario = result.Schedules.First(s => s.MovieTitle == "Super Mario galaktický film");
        Assert.Equal(2, mario.Performances[0].Showtimes.Count);
        Assert.Contains(mario.Performances[0].Showtimes, t => t.StartAt == new TimeOnly(17, 0));
        Assert.Contains(mario.Performances[0].Showtimes, t => t.StartAt == new TimeOnly(19, 30));
    }

    [Fact]
    public async Task TicketUrlIsCinemAware()
    {
        var result = await CreateScraper().ScrapeAsync();

        var poberta = result.Schedules.First(s => s.MovieTitle == "Poberta");
        Assert.Contains("cinemaware.eu", poberta.Performances[0].Showtimes[0].TicketUrl);
    }

    [Fact]
    public async Task MovieUrlPointsToKinoRadotin()
    {
        var result = await CreateScraper().ScrapeAsync();

        var poberta = result.Schedules.First(s => s.MovieTitle == "Poberta");
        Assert.StartsWith("https://www.kinoradotin.cz/event/", poberta.Performances[0].MovieUrl);
    }
}
