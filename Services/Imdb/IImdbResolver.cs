using HtmlAgilityPack;
using vkinegrab.Models;

namespace vkinegrab.Services.Imdb
{
    public interface IImdbResolver
    {
        Task<string?> ResolveImdbId(HtmlDocument csfdDoc, CsfdMovie movie);

        /// <summary>
        /// Validate or fetch IMDb metadata by explicit IMDb id (used when we already have an id stored).
        /// Returns true when the id's metadata is acceptable for the provided movie (year/directors check).
        /// </summary>
        Task<bool> ValidateImdbId(string imdbId, CsfdMovie movie);
    }
}
