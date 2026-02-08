using vkinegrab.Models;

namespace vkinegrab.Services.Csfd
{
    public interface ICsfdScraper
    {
        // Accept optional existing IMDb id to avoid running heuristic lookup when we already have an id
        Task<CsfdMovie> ScrapeMovie(int movieId, string? existingImdbId = null);
        Task<TmdbMovie?> ResolveTmdb(CsfdMovie movie);
        Task<TmdbMovie?> FetchTmdbById(int tmdbId);

        // Venue scraping
        Task<Venue> ScrapeVenue(int venueId);
        Task<Venue> ScrapeVenue(string url);
    }
}
