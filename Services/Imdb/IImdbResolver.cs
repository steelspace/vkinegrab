using HtmlAgilityPack;
using vkinegrab.Models;

namespace vkinegrab.Services.Imdb
{
    public interface IImdbResolver
    {
        Task<string?> ResolveImdbId(HtmlDocument csfdDoc, CsfdMovie movie);
    }
}
