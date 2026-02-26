namespace vkinegrab.Models;

using vkinegrab.Services;

public static class MovieMergeExtensions
{
    /// <summary>
    /// Merges CsfdMovie and TmdbMovie data into a final MergedMovie structure.
    /// Priority: CSFD for text data, TMDB for media and metadata.
    /// </summary>
    public static Movie Merge(this CsfdMovie csfdMovie, TmdbMovie? tmdbMovie = null)
    {
        var merged = new Movie
        {
            CsfdId = csfdMovie.Id,
            TmdbId = tmdbMovie?.Id,
            ImdbId = csfdMovie.ImdbId,
            
            // Primary text data from CSFD, fallback to TMDB
            Title = !string.IsNullOrWhiteSpace(csfdMovie.Title) 
                ? csfdMovie.Title 
                : tmdbMovie?.Title,
            
            OriginalTitle = !string.IsNullOrWhiteSpace(csfdMovie.OriginalTitle)
                ? csfdMovie.OriginalTitle
                : tmdbMovie?.OriginalTitle,
            
            Year = csfdMovie.Year,
            Duration = csfdMovie.Duration,
            Rating = csfdMovie.Rating,
            ImdbRating = csfdMovie.ImdbRating,
            ImdbRatingCount = csfdMovie.ImdbRatingCount,
            DescriptionCs = csfdMovie.Description,
            DescriptionEn = tmdbMovie?.Overview,
            
            Origin = csfdMovie.Origin,
            OriginCountryCodes = CountryCodeMapper.MapToIsoAlpha2(csfdMovie.Origins),
            Genres = csfdMovie.Genres ?? new List<string>(),
            Directors = csfdMovie.Directors ?? new List<string>(),
            Cast = csfdMovie.Cast ?? new List<string>(),
            
            // Media URLs: primary from TMDB, fallback to CSFD
            PosterUrl = !string.IsNullOrWhiteSpace(tmdbMovie?.FullPosterUrl)
                ? tmdbMovie.FullPosterUrl
                : csfdMovie.PosterUrl,

            CsfdPosterUrl = csfdMovie.PosterUrl,
            
            BackdropUrl = tmdbMovie?.FullBackdropUrl,
            
            // TMDB metadata
            VoteAverage = tmdbMovie?.VoteAverage,
            VoteCount = tmdbMovie?.VoteCount,
            Popularity = tmdbMovie?.Popularity,
            OriginalLanguage = tmdbMovie?.OriginalLanguage,
            Adult = tmdbMovie?.Adult,
            Homepage = tmdbMovie?.Homepage,
            TrailerUrl = tmdbMovie?.TrailerUrl,
            Credits = tmdbMovie?.Credits ?? new List<CrewMember>(),
            
            // Localization from CSFD
            LocalizedTitles = csfdMovie.LocalizedTitles ?? new Dictionary<string, string>(),

            // Release date (from TMDB when available)
            ReleaseDate = ParseReleaseDate(tmdbMovie?.ReleaseDate)
        };

        return merged;
    }

    private static DateTime? ParseReleaseDate(string? releaseDate)
    {
        if (string.IsNullOrWhiteSpace(releaseDate)) return null;
        if (DateTime.TryParse(releaseDate, out var dt)) return dt.Date;
        return null;
    }
}
