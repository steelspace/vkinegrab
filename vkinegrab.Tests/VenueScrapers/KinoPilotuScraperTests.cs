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

public class KinoPilotuScraperTests
{
    private static KinoPilotuScraper CreateScraper()
    {
        var html = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "VenueScrapers", "Fixtures", "KinoPilotu.html"));
        var mock = new Mock<IHtmlFetcher>();
        mock.Setup(f => f.FetchAsync(It.IsAny<Uri>(), default)).ReturnsAsync(html);
        return new KinoPilotuScraper(mock.Object, NullLogger<KinoPilotuScraper>.Instance);
    }

    [Fact]
    public async Task ParsesThreeSchedules()
    {
        var result = await CreateScraper().ScrapeAsync();
        Assert.Equal(3, result.Schedules.Count);
    }

    [Fact]
    public async Task ParsesCorrectDates()
    {
        var result = await CreateScraper().ScrapeAsync();
        Assert.Contains(result.Schedules, s => s.Date == new DateOnly(2026, 3, 30));
        Assert.Contains(result.Schedules, s => s.Date == new DateOnly(2026, 3, 31));
    }

    [Fact]
    public async Task ParsesFilmTitles()
    {
        var result = await CreateScraper().ScrapeAsync();
        Assert.Contains(result.Schedules, s => s.MovieTitle == "Spasitel");
        Assert.Contains(result.Schedules, s => s.MovieTitle == "Céčko: Případ 137");
        Assert.Contains(result.Schedules, s => s.MovieTitle == "Super Mario galaktický film");
    }

    [Fact]
    public async Task TicketUrlIsEntradio()
    {
        var result = await CreateScraper().ScrapeAsync();
        var spasitel = result.Schedules.First(s => s.MovieTitle == "Spasitel");
        Assert.Contains("entradio.cz", spasitel.Performances[0].Showtimes[0].TicketUrl);
    }
}
