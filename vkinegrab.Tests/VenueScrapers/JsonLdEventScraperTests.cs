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

/// <summary>
/// Tests the JSON-LD Event platform scraper using the KinoAero fixture
/// (canonical representative for Bio Oko / Aero / Světozor / Lucerna / Přítomnost).
/// Also verifies the Edison Filmhub variant (startDate as array).
/// </summary>
public class JsonLdEventScraperTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "VenueScrapers", "Fixtures", name);

    private static Mock<IHtmlFetcher> FetcherFor(string fixtureName)
    {
        var html = File.ReadAllText(FixturePath(fixtureName));
        var mock = new Mock<IHtmlFetcher>();
        mock.Setup(f => f.FetchAsync(It.IsAny<Uri>(), default))
            .ReturnsAsync(html);
        return mock;
    }

    // ── KinoAero (canonical JSON-LD platform) ─────────────────────────────────

    [Fact]
    public async Task KinoAero_ParsesTwoScreeningDays()
    {
        var scraper = new KinoAeroScraper(FetcherFor("KinoAero.html").Object, NullLogger<KinoAeroScraper>.Instance);

        var result = await scraper.ScrapeAsync();

        // Day 1: 2 films (They Will Kill You + The Fall)
        // Day 2: 1 film (Grace — two showtimes should be merged into one Schedule)
        Assert.Equal(3, result.Schedules.Count);
    }

    [Fact]
    public async Task KinoAero_ParsesShowtimesForFirstFilm()
    {
        var scraper = new KinoAeroScraper(FetcherFor("KinoAero.html").Object, NullLogger<KinoAeroScraper>.Instance);

        var result = await scraper.ScrapeAsync();

        var schedule = result.Schedules.First(s => s.MovieTitle == "They Will Kill You");
        Assert.Single(schedule.Performances);
        var showtime = schedule.Performances[0].Showtimes.Single();
        Assert.Equal(new TimeOnly(18, 15), showtime.StartAt);
        Assert.True(showtime.TicketsAvailable);
    }

    [Fact]
    public async Task KinoAero_MergesTwoShowtimesForSameTitleOnSameDay()
    {
        var scraper = new KinoAeroScraper(FetcherFor("KinoAero.html").Object, NullLogger<KinoAeroScraper>.Instance);

        var result = await scraper.ScrapeAsync();

        var schedule = result.Schedules.First(s => s.MovieTitle == "Grace");
        // Both 15:00 and 18:00 screenings should live in ONE schedule with one performance
        Assert.Single(schedule.Performances);
        Assert.Equal(2, schedule.Performances[0].Showtimes.Count);
    }

    [Fact]
    public async Task KinoAero_ReturnsCorrectVenueId()
    {
        var scraper = new KinoAeroScraper(FetcherFor("KinoAero.html").Object, NullLogger<KinoAeroScraper>.Instance);

        var result = await scraper.ScrapeAsync();

        Assert.Equal(scraper.VenueId, result.Venue.Id);
        Assert.Equal("Kino Aero", result.Venue.Name);
    }

    // ── Edison Filmhub variant (startDate as JSON array) ──────────────────────

    [Fact]
    public async Task EdisonFilmhub_ParsesArrayStartDate()
    {
        var scraper = new EdisonFilmhubScraper(FetcherFor("EdisonFilmhub.html").Object, NullLogger<EdisonFilmhubScraper>.Instance);

        var result = await scraper.ScrapeAsync();

        Assert.Equal(2, result.Schedules.Count);
        Assert.Contains(result.Schedules, s => s.MovieTitle == "Mother");
        Assert.Contains(result.Schedules, s => s.MovieTitle == "Pillion");
    }

    [Fact]
    public async Task EdisonFilmhub_CorrectShowtime()
    {
        var scraper = new EdisonFilmhubScraper(FetcherFor("EdisonFilmhub.html").Object, NullLogger<EdisonFilmhubScraper>.Instance);

        var result = await scraper.ScrapeAsync();

        var pillion = result.Schedules.First(s => s.MovieTitle == "Pillion");
        Assert.Equal(new TimeOnly(16, 30), pillion.Performances[0].Showtimes[0].StartAt);
    }
}
