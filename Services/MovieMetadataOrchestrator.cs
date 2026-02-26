using vkinegrab.Models;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Services;

public interface IMovieMetadataOrchestrator
{
    Task<Movie> ResolveMovieMetadataAsync(int csfdId, Movie? existing, CancellationToken ct = default);
}

public class MovieMetadataOrchestrator : IMovieMetadataOrchestrator
{
    private readonly ICsfdScraper csfdScraper;

    public MovieMetadataOrchestrator(ICsfdScraper csfdScraper)
    {
        this.csfdScraper = csfdScraper;
    }

    public async Task<Movie> ResolveMovieMetadataAsync(int csfdId, Movie? existing, CancellationToken ct = default)
    {
        // 1. Scrape CSFD (skip IMDB resolution if we already have the ID from a previous run)
        var hasExistingImdb = !string.IsNullOrEmpty(existing?.ImdbId);
        var csfdMovie = await csfdScraper.ScrapeMovie(csfdId, resolveImdb: !hasExistingImdb);

        if (hasExistingImdb && !csfdMovie.IsStudentFilm)
        {
            csfdMovie.ImdbId = existing!.ImdbId;
            var (rating, ratingCount) = await csfdScraper.FetchImdbRating(existing.ImdbId!);
            csfdMovie.ImdbRating = rating;
            csfdMovie.ImdbRatingCount = ratingCount;
        }

        // 2. Resolve TMDB (skip search if we already have the ID from a previous run)
        TmdbMovie? tmdbMovie = null;
        if (!csfdMovie.IsStudentFilm)
        {
            if (existing?.TmdbId.HasValue == true)
            {
                tmdbMovie = await csfdScraper.FetchTmdbById(existing.TmdbId.Value);
            }
            else
            {
                tmdbMovie = await csfdScraper.ResolveTmdb(csfdMovie);
            }
        }

        // 3. Merge
        var merged = csfdMovie.Merge(tmdbMovie);

        // 4. Preserve existing fields if merge didn't update them
        if (existing != null)
        {
            if (!merged.TmdbId.HasValue && existing.TmdbId.HasValue)
                merged.TmdbId = existing.TmdbId;

            if (string.IsNullOrWhiteSpace(merged.ImdbId) && !string.IsNullOrWhiteSpace(existing.ImdbId))
                merged.ImdbId = existing.ImdbId;

            if (string.IsNullOrWhiteSpace(merged.CsfdPosterUrl) && !string.IsNullOrWhiteSpace(existing.CsfdPosterUrl))
                merged.CsfdPosterUrl = existing.CsfdPosterUrl;

            if ((merged.OriginCountryCodes == null || merged.OriginCountryCodes.Count == 0) && existing.OriginCountryCodes?.Count > 0)
                merged.OriginCountryCodes = new List<string>(existing.OriginCountryCodes);

            if (!merged.ImdbRating.HasValue && existing.ImdbRating.HasValue)
            {
                merged.ImdbRating = existing.ImdbRating;
                merged.ImdbRatingCount = existing.ImdbRatingCount;
            }

            if (string.IsNullOrWhiteSpace(merged.TrailerUrl) && !string.IsNullOrWhiteSpace(existing.TrailerUrl))
                merged.TrailerUrl = existing.TrailerUrl;
        }

        return merged;
    }
}
