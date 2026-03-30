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

public class KinoPonrepoScraperTests
{
    private static KinoPonrepoScraper CreateScraper()
    {
        var html = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "VenueScrapers", "Fixtures", "KinoPonrepo.html"));
        var mock = new Mock<IHtmlFetcher>();
        mock.Setup(f => f.FetchAsync(It.IsAny<Uri>(), default)).ReturnsAsync(html);
        return new KinoPonrepoScraper(mock.Object, NullLogger<KinoPonrepoScraper>.Instance);
    }

    [Fact]
    public async Task ParsesThreeScreenings()
    {
        var result = await CreateScraper().ScrapeAsync();
        Assert.Equal(3, result.Schedules.Count);
    }

    [Fact]
    public async Task ExtractsCzechTitleOnly()
    {
        var result = await CreateScraper().ScrapeAsync();
        // "Něco z Alenky / Alice" → Czech part only
        Assert.Contains(result.Schedules, s => s.MovieTitle == "Něco z Alenky");
        // "Ostře sledované vlaky / Closely Watched Trains" → Czech part
        Assert.Contains(result.Schedules, s => s.MovieTitle == "Ostře sledované vlaky");
    }

    [Fact]
    public async Task ParsesScreeningTime()
    {
        var result = await CreateScraper().ScrapeAsync();
        var alice = result.Schedules.First(s => s.MovieTitle == "Něco z Alenky");
        Assert.Equal(new TimeOnly(15, 0), alice.Performances[0].Showtimes[0].StartAt);
    }

    [Fact]
    public async Task ParsesDate()
    {
        var result = await CreateScraper().ScrapeAsync();
        var alice = result.Schedules.First(s => s.MovieTitle == "Něco z Alenky");
        Assert.Equal(new DateOnly(2026, 3, 30), alice.Date);
    }
}
