using vkinegrab.Models;

namespace vkinegrab.Services.Csfd
{
    public interface ICsfdScraper
    {
        Task<CsfdMovie> ScrapeMovie(int movieId);
        Task<TmdbMovie?> ResolveTmdb(CsfdMovie movie);
        Task<TmdbMovie?> FetchTmdbById(int tmdbId);

        // Venue scraping
        Task<Venue> ScrapeVenue(int venueId);
        Task<Venue> ScrapeVenue(string url);
    }
}
