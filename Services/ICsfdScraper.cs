using vkinegrab.Models;

namespace vkinegrab.Services.Csfd
{
    public interface ICsfdScraper
    {
        Task<CsfdMovie> ScrapeMovie(int movieId);
        Task<TmdbMovie?> ResolveTmdb(CsfdMovie movie);
    }
}
