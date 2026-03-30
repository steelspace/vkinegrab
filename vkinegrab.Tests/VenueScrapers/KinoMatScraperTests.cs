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

public class KinoMatScraperTests
{
    private static KinoMatScraper CreateScraper()
    {
        var html = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "VenueScrapers", "Fixtures", "KinoMat.html"));
        var mock = new Mock<IHtmlFetcher>();
        mock.Setup(f => f.FetchAsync(It.IsAny<Uri>(), default)).ReturnsAsync(html);
        return new KinoMatScraper(mock.Object, NullLogger<KinoMatScraper>.Instance);
    }

    [Fact]
    public async Task ParsesDotSeparatedTime()
    {
        var result = await CreateScraper().ScrapeAsync();

        var film = result.Schedules.First(s => s.MovieTitle == "Království mýdlových bublin");
        Assert.Equal(new TimeOnly(16, 0), film.Performances[0].Showtimes[0].StartAt);
    }

    [Fact]
    public async Task ParsesOddMinutes()
    {
        var result = await CreateScraper().ScrapeAsync();

        // "16.15" → 16:15
        var film = result.Schedules
            .Where(s => s.MovieTitle == "Čaroděj z Kremlu")
            .SelectMany(s => s.Performances[0].Showtimes)
            .ToList();

        Assert.Contains(film, t => t.StartAt == new TimeOnly(16, 15));
    }

    [Fact]
    public async Task MovieLinkContainsMat()
    {
        var result = await CreateScraper().ScrapeAsync();

        var film = result.Schedules.First(s => s.MovieTitle == "Království mýdlových bublin");
        Assert.Contains("mat.cz", film.Performances[0].MovieUrl);
    }
}
