namespace vkinegrab.Services.Csfd;

public sealed class HttpClientHtmlFetcher : IHtmlFetcher
{
    private readonly IHttpClientFactory httpClientFactory;

    public HttpClientHtmlFetcher(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<string> FetchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("Csfd");
        var startTime = DateTime.UtcNow;
        var html = await client.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
        Console.WriteLine($"[HttpClientHtmlFetcher] HTTP request completed in {elapsed:F0}ms");
        return html;
    }
}
