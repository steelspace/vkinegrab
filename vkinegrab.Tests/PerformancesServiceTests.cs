using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using vkinegrab.Services.Csfd;
using System.Net.Http.Headers;
using vkinegrab.Models;
using System.Linq;

namespace vkinegrab.Tests
{
    public class PerformancesServiceTests
    {
        [Fact]
        public async Task GetSchedulesWithVenues_Returns_Venues_And_Deduplicates()
        {
            var html = @"
<html>
  <body>
    <section id=""cinema-1"" class=""updated-box-cinema"">
      <a href=""/kino/1-praha/"">Cinema One</a>
      <div class=""update-box-sub-header"">1.2.2026</div>
      <table class=""cinema-table"">
        <tr>
          <td class=""name""><a href=""/film/100/"">Movie A</a></td>
          <td class=""td-time"">10:00</td>
        </tr>
      </table>
    </section>

    <section id=""cinema-foo"" class=""updated-box-cinema"">
      <a href=""/kino/1-praha/foo-slug/"">Cinema One Duplicate</a>
      <div class=""update-box-sub-header"">1.2.2026</div>
      <table class=""cinema-table"">
        <tr>
          <td class=""name""><a href=""/film/101/"">Movie B</a></td>
          <td class=""td-time"">11:00</td>
        </tr>
      </table>
    </section>

    <section id=""cinema-2"" class=""updated-box-cinema"">
      <a href=""/kino/2-ostrava/"">Cinema Two</a>
      <address>Some street 5</address>
      <div class=""update-box-sub-header"">1.2.2026</div>
      <table class=""cinema-table"">
        <tr>
          <td class=""name""><a href=""/film/102/"">Movie C</a></td>
          <td class=""td-time"">12:00</td>
        </tr>
      </table>
    </section>
  </body>
</html>
";

            var handler = new FakeHttpMessageHandler(html);
            var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://www.csfd.cz/") };

            var service = new PerformancesService(client, new System.Uri("https://www.csfd.cz/"));

            var (schedules, venues) = await service.GetSchedulesWithVenues(null, "all");

            // should have schedules for three films
            Assert.Equal(3, schedules.Select(s => s.MovieId).Distinct().Count());

            // venues should contain two unique venue IDs (1 and 2)
            var venueIds = venues.Select(v => v.Id).OrderBy(id => id).ToList();
            Assert.Equal(new[] { 1, 2 }, venueIds);

            // ensure names and addresses were extracted where available
            var v1 = venues.First(v => v.Id == 1);
            Assert.Contains("Cinema One", v1.Name);

            var v2 = venues.First(v => v.Id == 2);
            Assert.Contains("Cinema Two", v2.Name);
            Assert.Equal("Some street 5", v2.Address);
        }

        private class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly string responseContent;

            public FakeHttpMessageHandler(string responseContent)
            {
                this.responseContent = responseContent;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseContent)
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
                return Task.FromResult(response);
            }
        }
    }
}
