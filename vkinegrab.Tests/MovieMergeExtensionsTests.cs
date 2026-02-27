using System.Collections.Generic;
using Xunit;
using vkinegrab.Models;

namespace vkinegrab.Tests;

public class MovieMergeExtensionsTests
{
    [Fact]
    public void Merge_WithBothCsfdAndTmdb_UsesCsfdForTextAndTmdbForMedia()
    {
        // Arrange
        var csfdMovie = new CsfdMovie
        {
            Id = 123,
            Title = "CSFD Title",
            OriginalTitle = "CSFD Original",
            Year = "2023",
            Description = "CSFD Description",
            Origin = "Česká republika",
            Origins = new List<string> { "Česká republika" },
            ImdbId = "tt1234567",
            Genres = new List<string> { "Drama", "Crime" },
            Directors = new List<string> { "Director One" },
            Cast = new List<string> { "Actor One", "Actor Two" },
            PosterUrl = "https://csfd.cz/poster.jpg",
            LocalizedTitles = new Dictionary<string, string> { { "en", "English Title" } }
        };

        var tmdbMovie = new TmdbMovie
        {
            Id = 456,
            Title = "TMDB Title",
            OriginalTitle = "TMDB Original",
            ReleaseDate = "2023-01-15",
            Overview = "TMDB Description",
            PosterPath = "/tmdb-poster.jpg",
            BackdropPath = "/tmdb-backdrop.jpg",
            VoteAverage = 7.5,
            VoteCount = 1000,
            Popularity = 25.5,
            OriginalLanguage = "cs",
            Adult = false
        };

        // Act
        var merged = csfdMovie.Merge(tmdbMovie);

        // Assert
        // IDs
        Assert.Equal(123, merged.CsfdId);
        Assert.Equal(456, merged.TmdbId);
        Assert.Equal("TMDB Title", merged.TmdbTitle);
        Assert.Equal("tt1234567", merged.ImdbId);

        // CSFD text data (primary)
        Assert.Equal("CSFD Title", merged.Title);
        Assert.Equal("CSFD Original", merged.OriginalTitle);
        Assert.Equal("2023", merged.Year);
        Assert.Equal("CSFD Description", merged.DescriptionCs);
        Assert.Equal("TMDB Description", merged.DescriptionEn);
        Assert.Equal("Česká republika", merged.Origin);
        Assert.Equal(new List<string> { "CZ" }, merged.OriginCountryCodes);
        Assert.Equal(new List<string> { "Drama", "Crime" }, merged.Genres);
        Assert.Equal(new List<string> { "Director One" }, merged.Directors);
        Assert.Equal(new List<string> { "Actor One", "Actor Two" }, merged.Cast);

        // TMDB media (primary)
        Assert.Equal("https://image.tmdb.org/t/p/original/tmdb-poster.jpg", merged.PosterUrl);
        Assert.Equal("https://csfd.cz/poster.jpg", merged.CsfdPosterUrl);
        Assert.Equal("https://image.tmdb.org/t/p/original/tmdb-backdrop.jpg", merged.BackdropUrl);

        // TMDB metadata
        Assert.Equal(7.5, merged.VoteAverage);
        Assert.Equal(1000, merged.VoteCount);
        Assert.Equal(25.5, merged.Popularity);
        Assert.Equal("cs", merged.OriginalLanguage);
        Assert.False(merged.Adult);

        // Localization
        Assert.Single(merged.LocalizedTitles);
        Assert.Equal("English Title", merged.LocalizedTitles["en"]);
    }

    [Fact]
    public void Merge_WithOnlyCsfd_UsesAllCsfdData()
    {
        // Arrange
        var csfdMovie = new CsfdMovie
        {
            Id = 789,
            Title = "Movie Title",
            Description = "Movie Description",
            PosterUrl = "https://csfd.cz/poster.jpg",
            Origins = new List<string> { "USA", "Československo" }
        };

        // Act
        var merged = csfdMovie.Merge(null);

        // Assert
        Assert.Equal(789, merged.CsfdId);
        Assert.Null(merged.TmdbId);
        Assert.Null(merged.TmdbTitle);
        Assert.Equal("Movie Title", merged.Title);
        Assert.Equal("Movie Description", merged.DescriptionCs);
        Assert.Null(merged.DescriptionEn);
        Assert.Equal("https://csfd.cz/poster.jpg", merged.PosterUrl);
        Assert.Equal(new List<string> { "US", "CS" }, merged.OriginCountryCodes);
        Assert.Equal("https://csfd.cz/poster.jpg", merged.CsfdPosterUrl);
        Assert.Null(merged.BackdropUrl);
    }

    [Fact]
    public void Merge_WithMissingCsfdTitle_FallsBackToTmdb()
    {
        // Arrange
        var csfdMovie = new CsfdMovie
        {
            Id = 111,
            Title = null,
            Description = "CSFD Desc"
        };

        var tmdbMovie = new TmdbMovie
        {
            Id = 222,
            Title = "TMDB Fallback Title",
            Overview = "TMDB Overview"
        };

        // Act
        var merged = csfdMovie.Merge(tmdbMovie);

        // Assert
        Assert.Equal("TMDB Fallback Title", merged.Title);
        Assert.Equal("TMDB Fallback Title", merged.TmdbTitle);
        Assert.Equal("CSFD Desc", merged.DescriptionCs);
        Assert.Equal("TMDB Overview", merged.DescriptionEn);
    }

    [Fact]
    public void Merge_PrefersTmdbPosterOverCsfd()
    {
        // Arrange
        var csfdMovie = new CsfdMovie
        {
            Id = 333,
            PosterUrl = "https://csfd.cz/poster.jpg"
        };

        var tmdbMovie = new TmdbMovie
        {
            Id = 444,
            PosterPath = "/tmdb-poster.jpg"
        };

        // Act
        var merged = csfdMovie.Merge(tmdbMovie);

        // Assert
        Assert.Equal("https://image.tmdb.org/t/p/original/tmdb-poster.jpg", merged.PosterUrl);
        Assert.Equal("https://csfd.cz/poster.jpg", merged.CsfdPosterUrl);
    }

    [Fact]
    public void Merge_UsesCsfdPosterWhenTmdbMissing()
    {
        // Arrange
        var csfdMovie = new CsfdMovie
        {
            Id = 555,
            PosterUrl = "https://csfd.cz/poster.jpg"
        };

        var tmdbMovie = new TmdbMovie
        {
            Id = 666,
            PosterPath = null
        };

        // Act
        var merged = csfdMovie.Merge(tmdbMovie);

        // Assert
        Assert.Equal("https://csfd.cz/poster.jpg", merged.PosterUrl);
        Assert.Equal("https://csfd.cz/poster.jpg", merged.CsfdPosterUrl);
    }

    [Fact]
    public void Merge_GeneratesCorrectUrls()
    {
        // Arrange
        var csfdMovie = new CsfdMovie { Id = 999, ImdbId = "tt9999999" };
        var tmdbMovie = new TmdbMovie { Id = 888 };

        // Act
        var merged = csfdMovie.Merge(tmdbMovie);

        // Assert
        Assert.Equal("https://www.csfd.cz/film/999", merged.CsfdUrl);
        Assert.Equal("https://www.themoviedb.org/movie/888", merged.TmdbUrl);
        Assert.Equal("https://www.imdb.com/title/tt9999999", merged.ImdbUrl);
    }

    [Fact]
    public void Merge_HandlesEmptyCollections()
    {
        // Arrange
        var csfdMovie = new CsfdMovie
        {
            Id = 100,
            Genres = new List<string>(),
            Directors = new List<string>(),
            Cast = new List<string>()
        };

        // Act
        var merged = csfdMovie.Merge(null);

        // Assert
        Assert.Empty(merged.Genres);
        Assert.Empty(merged.Directors);
        Assert.Empty(merged.Cast);
    }
}
