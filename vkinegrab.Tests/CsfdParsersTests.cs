using System;
using System.Linq;
using HtmlAgilityPack;
using Xunit;
using vkinegrab.Services.Csfd;
using vkinegrab.Models;

namespace vkinegrab.Tests
{
    public class CsfdParsersTests
    {
        [Fact]
        public void BadgeExtractor_Returns_Hall_And_Format_Badges()
        {
            var html = @"<tr>
  <td class='name'><span class='cinema-icon'>Gold Class</span><a href='/film/200/'>Movie X</a></td>
  <td class='td-title'><span>3D</span></td>
  <td class='td-time'>10:00</td>
</tr>";
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var row = doc.DocumentNode.SelectSingleNode("//tr");

            var extractor = new BadgeExtractor();
            var badges = extractor.ExtractBadges(row).ToList();

            Assert.Contains(badges, b => b.Kind == BadgeKind.Hall && b.Code == "Gold Class");
            Assert.Contains(badges, b => b.Kind == BadgeKind.Format && b.Code == "3D");
        }

        [Fact]
        public void ShowtimeExtractor_Parses_Times_And_TicketUrl()
        {
            var html = @"<tr>
  <td class='name'><a href='/film/200/'>Movie X</a></td>
  <td class='td-time td-buy-ticket'><a href='http://tickets/1'>10:00</a></td>
  <td class='td-time'>11:30</td>
</tr>";
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var row = doc.DocumentNode.SelectSingleNode("//tr");

            var extractor = new ShowtimeExtractor();
            var showtimes = extractor.ExtractShowtimes(row, DateOnly.FromDateTime(new DateTime(2026,2,2)), new Uri("https://www.csfd.cz/"))
                .OrderBy(s => s.StartAt)
                .ToList();

            Assert.Equal(2, showtimes.Count);
            Assert.Equal("10:00", showtimes[0].StartAt.ToString("HH:mm"));
            Assert.True(showtimes[0].TicketsAvailable);
            Assert.Equal("http://tickets/1", showtimes[0].TicketUrl);
            Assert.Equal("11:30", showtimes[1].StartAt.ToString("HH:mm"));
            Assert.False(showtimes[1].TicketsAvailable);
        }

        [Fact]
        public void CsfdRowParser_Creates_Performance_With_Showtimes_And_Badges_On_Showtimes()
        {
            var html = @"<tr>
  <td class='name'><span class='cinema-icon'>Gold Class</span><a href='/film/300/'>Movie Y</a></td>
  <td class='td-title'><span>3D</span></td>
  <td class='td-time'><a href='/buy/1'>10:00</a></td>
</tr>";
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var row = doc.DocumentNode.SelectSingleNode("//tr");

            var parser = new CsfdRowParser(new BadgeExtractor(), new ShowtimeExtractor());
            var perf = parser.Parse(row, DateOnly.FromDateTime(new DateTime(2026,2,2)), new Uri("https://www.csfd.cz/"));

            Assert.NotNull(perf);
            Assert.Equal(300, perf.MovieId);
            Assert.Single(perf.Showtimes);
            var st = perf.Showtimes.First();
            Assert.Contains(st.Badges, b => b.Kind == BadgeKind.Hall && b.Code == "Gold Class");
            Assert.Contains(st.Badges, b => b.Kind == BadgeKind.Format && b.Code == "3D");
        }
    }
}