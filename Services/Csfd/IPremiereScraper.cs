using vkinegrab.Models;

namespace vkinegrab.Services.Csfd;

public interface IPremiereScraper
{
    Task<IReadOnlyList<Premiere>> ScrapePremieresAsync(int year, CancellationToken cancellationToken = default);
}
