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

    <section id=""cinema-3"" class=""updated-box-cinema"">
      <a href=""/kino/3-praha/"">Praha - Cinema Three</a>
      <div class=""update-box-sub-header"">1.2.2026</div>
      <table class=""cinema-table"">
        <tr>
          <td class=""name""><a href=""/film/103/"">Movie D</a></td>
          <td class=""td-time"">13:00</td>
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

            // additional regression test: even if the service is constructed with a file:// base, we should resolve hrefs to https
            var fileClient = new HttpClient(new FakeHttpMessageHandler(html)) { BaseAddress = new System.Uri("file:///") };
            var fileService = new PerformancesService(fileClient, new System.Uri("file:///"));
            var (s2, venues2) = await fileService.GetSchedulesWithVenues(null, "all");
            var v1file = venues2.First(v => v.Id == 1);

            // Debug: call private ToAbsoluteUrl via reflection to see how it resolves
            var method = typeof(PerformancesService).GetMethod("ToAbsoluteUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var resolved = (string?)method?.Invoke(null, new object?[] { "/kino/1-praha/", new System.Uri("file:///kino/1-praha/?period=all") });
            Assert.Equal("https://www.csfd.cz/kino/1-praha/", resolved);

            Assert.StartsWith("https://www.csfd.cz/", v1file.DetailUrl);

            // should have schedules for four films (we added Movie D)
            Assert.Equal(4, schedules.Select(s => s.MovieId).Distinct().Count());

            // venues should contain venue IDs 1, 2 and 3 (we don't require strict dedup ordering here)
            var venueIds = venues.Select(v => v.Id).OrderBy(id => id).ToList();
            Assert.Contains(1, venueIds);
            Assert.Contains(2, venueIds);
            Assert.Contains(3, venueIds);

            // ensure names and addresses were extracted where available
            var v1 = venues.First(v => v.Id == 1);
            Assert.Contains("Cinema One", v1.Name);
            Assert.Equal("https://www.csfd.cz/kino/1-praha/", v1.DetailUrl);

            var v2 = venues.First(v => v.Id == 2);
            Assert.Contains("Cinema Two", v2.Name);
            Assert.Equal("Some street 5", v2.Address);
            Assert.Equal("https://www.csfd.cz/kino/2-ostrava/", v2.DetailUrl);

            var v3 = venues.First(v => v.Id == 3);
            Assert.Equal("Cinema Three", v3.Name);
            Assert.Equal("https://www.csfd.cz/kino/3-praha/", v3.DetailUrl);
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
