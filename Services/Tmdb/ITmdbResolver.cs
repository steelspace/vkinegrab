using vkinegrab.Models;

namespace vkinegrab.Services.Tmdb
{
    public interface ITmdbResolver
    {
        Task<TmdbMovie?> ResolveTmdbMovie(CsfdMovie movie);
        Task<TmdbMovie?> GetMovieById(int id);
    }
}
