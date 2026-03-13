namespace vkinegrab.Services.Csfd;

public interface IHtmlFetcher
{
    Task<string> FetchAsync(Uri uri, CancellationToken cancellationToken = default);
}
