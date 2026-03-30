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

public class PremiereCinemasScraperTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "VenueScrapers", "Fixtures", name);

    private static PremiereCinemasScraper CreateScraper(string fixture)
    {
        var html = File.ReadAllText(FixturePath(fixture));
        var mock = new Mock<IHtmlFetcher>();
        mock.Setup(f => f.FetchAsync(It.IsAny<Uri>(), default)).ReturnsAsync(html);
        return new PremiereCinemasScraper(mock.Object, NullLogger<PremiereCinemasScraper>.Instance);
    }

    [Fact]
    public async Task ParsesTwoDateTabs()
    {
        var result = await CreateScraper("PremiereCinemas.html").ScrapeAsync();

        // Tab 1: Spasitel (3 times) + Super Mario (2 times) = 2 schedules
        // Tab 2: Spasitel (2 times) = 1 schedule
        Assert.Equal(3, result.Schedules.Count);
    }

    [Fact]
    public async Task ParsesShowtimesFromTab()
    {
        var result = await CreateScraper("PremiereCinemas.html").ScrapeAsync();

        var schedules = result.Schedules.Where(s => s.MovieTitle == "Spasitel").ToList();
        // Two date tabs → two Schedule entries for Spasitel
        Assert.Equal(2, schedules.Count);

        var day1 = schedules.First();
        Assert.Equal(3, day1.Performances[0].Showtimes.Count);
        Assert.Contains(day1.Performances[0].Showtimes, t => t.StartAt == new TimeOnly(15, 40));
    }

    [Fact]
    public async Task TicketUrlIsPopulated()
    {
        var result = await CreateScraper("PremiereCinemas.html").ScrapeAsync();

        var showtime = result.Schedules
            .First(s => s.MovieTitle == "Spasitel")
            .Performances[0].Showtimes[0];

        Assert.NotNull(showtime.TicketUrl);
        Assert.Contains("/vstupenky/", showtime.TicketUrl);
    }
}
