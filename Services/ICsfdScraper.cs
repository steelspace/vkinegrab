using vkinegrab.Models;

namespace vkinegrab.Services.Csfd
{
    public interface ICsfdScraper
    {
        Task<CsfdMovie> ScrapeMovie(int movieId, bool resolveImdb = true);
        Task<Venue> ScrapeVenue(int venueId);
        Task<TmdbMovie?> ResolveTmdb(CsfdMovie movie);
        Task<TmdbMovie?> FetchTmdbById(int tmdbId);
        Task<(double? Rating, int? RatingCount)> FetchImdbRating(string imdbId);
        Task<string?> FetchTrailerUrl(int tmdbId);
        Task<List<CrewMember>> FetchCredits(int tmdbId);
    }
}
