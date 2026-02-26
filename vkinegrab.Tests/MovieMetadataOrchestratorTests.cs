using vkinegrab.Models;
using vkinegrab.Services;
using vkinegrab.Services.Csfd;

namespace vkinegrab.Tests;

public class MovieMetadataOrchestratorTests
{
    [Fact]
    public async Task ResolveMovieMetadata_NoExisting_PerformsFullImdbResolution()
    {
        var scraper = new FakeCsfdScraper();
        var orchestrator = new MovieMetadataOrchestrator(scraper);

        var result = await orchestrator.ResolveMovieMetadataAsync(1, existing: null);

        Assert.True(scraper.ScrapeCalledWithResolveImdb, "ScrapeMovie should be called with resolveImdb=true when no existing IMDB ID");
        Assert.False(scraper.FetchImdbRatingCalled, "FetchImdbRating should not be called when no existing IMDB ID");
        Assert.Equal("tt999", result.ImdbId);
        Assert.Equal(7.5, result.ImdbRating);
        Assert.Equal(1000, result.ImdbRatingCount);
    }

    [Fact]
    public async Task ResolveMovieMetadata_WithExistingImdbId_SkipsSearchAndFetchesFreshRating()
    {
        var scraper = new FakeCsfdScraper();
        var orchestrator = new MovieMetadataOrchestrator(scraper);

        var existing = new Movie { CsfdId = 1, ImdbId = "tt123", ImdbRating = 6.0, ImdbRatingCount = 500 };

        var result = await orchestrator.ResolveMovieMetadataAsync(1, existing);

        Assert.False(scraper.ScrapeCalledWithResolveImdb, "ScrapeMovie should be called with resolveImdb=false when existing IMDB ID is present");
        Assert.True(scraper.FetchImdbRatingCalled, "FetchImdbRating should be called to get fresh rating");
        Assert.Equal("tt123", scraper.FetchImdbRatingCalledWithId);
        Assert.Equal("tt123", result.ImdbId);
        // Fresh rating should be used, not outdated DB values
        Assert.Equal(8.2, result.ImdbRating);
        Assert.Equal(2000, result.ImdbRatingCount);
    }

    [Fact]
    public async Task ResolveMovieMetadata_WithExistingTmdbId_FetchesByIdInsteadOfSearching()
    {
        var scraper = new FakeCsfdScraper();
        var orchestrator = new MovieMetadataOrchestrator(scraper);

        var existing = new Movie { CsfdId = 1, TmdbId = 42 };

        var result = await orchestrator.ResolveMovieMetadataAsync(1, existing);

        Assert.True(scraper.FetchTmdbByIdCalled, "FetchTmdbById should be called when existing TmdbId is present");
        Assert.Equal(42, scraper.FetchTmdbByIdCalledWithId);
        Assert.False(scraper.ResolveTmdbCalled, "ResolveTmdb should not be called when TmdbId is already known");
    }

    [Fact]
    public async Task ResolveMovieMetadata_NoExistingTmdbId_PerformsTmdbSearch()
    {
        var scraper = new FakeCsfdScraper();
        var orchestrator = new MovieMetadataOrchestrator(scraper);

        var result = await orchestrator.ResolveMovieMetadataAsync(1, existing: null);

        Assert.True(scraper.ResolveTmdbCalled, "ResolveTmdb should be called when no existing TmdbId");
        Assert.False(scraper.FetchTmdbByIdCalled, "FetchTmdbById should not be called when no existing TmdbId");
    }

    private class FakeCsfdScraper : ICsfdScraper
    {
        // Tracking flags
        public bool ScrapeCalledWithResolveImdb { get; private set; }
        public bool FetchImdbRatingCalled { get; private set; }
        public string? FetchImdbRatingCalledWithId { get; private set; }
        public bool ResolveTmdbCalled { get; private set; }
        public bool FetchTmdbByIdCalled { get; private set; }
        public int? FetchTmdbByIdCalledWithId { get; private set; }

        public Task<CsfdMovie> ScrapeMovie(int movieId, bool resolveImdb = true)
        {
            ScrapeCalledWithResolveImdb = resolveImdb;

            var movie = new CsfdMovie { Id = movieId, Title = "Test Movie" };
            if (resolveImdb)
            {
                movie.ImdbId = "tt999";
                movie.ImdbRating = 7.5;
                movie.ImdbRatingCount = 1000;
            }
            return Task.FromResult(movie);
        }

        public Task<Venue> ScrapeVenue(int venueId)
        {
            return Task.FromResult(new Venue());
        }

        public Task<TmdbMovie?> ResolveTmdb(CsfdMovie movie)
        {
            ResolveTmdbCalled = true;
            return Task.FromResult<TmdbMovie?>(new TmdbMovie { Id = 100, Title = "Test Movie" });
        }

        public Task<TmdbMovie?> FetchTmdbById(int tmdbId)
        {
            FetchTmdbByIdCalled = true;
            FetchTmdbByIdCalledWithId = tmdbId;
            return Task.FromResult<TmdbMovie?>(new TmdbMovie { Id = tmdbId, Title = "Test Movie" });
        }

        public Task<(double? Rating, int? RatingCount)> FetchImdbRating(string imdbId)
        {
            FetchImdbRatingCalled = true;
            FetchImdbRatingCalledWithId = imdbId;
            return Task.FromResult<(double? Rating, int? RatingCount)>((8.2, 2000));
        }

        public Task<string?> FetchTrailerUrl(int tmdbId)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<List<CrewMember>> FetchCredits(int tmdbId)
        {
            return Task.FromResult(new List<CrewMember>());
        }
    }
}
