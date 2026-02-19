using vkinegrab.Models;

namespace vkinegrab.Services.Csfd
{
    public interface ICsfdScraper
    {
        Task<CsfdMovie> ScrapeMovie(int movieId);
        Task<Venue> ScrapeVenue(int venueId);
        Task<TmdbMovie?> ResolveTmdb(CsfdMovie movie);
        Task<TmdbMovie?> FetchTmdbById(int tmdbId);
        Task<string?> FetchTrailerUrl(int tmdbId);
    }
}
