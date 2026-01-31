using MongoDB.Bson;
using MongoDB.Driver;
using vkinegrab.Models;
using vkinegrab.Services.Dtos;

namespace vkinegrab.Services;

/// <summary>
/// Service for storing and retrieving movie data from MongoDB.
/// </summary>
public class DatabaseService
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<StoredMovie> _moviesCollection;

    public DatabaseService(string connectionString)
    {
        var client = new MongoClient(connectionString);
        _database = client.GetDatabase("movies");
        _moviesCollection = _database.GetCollection<StoredMovie>("merged_movies");
        
        // Create index on CsfdId for faster lookups
        var indexOptions = new CreateIndexOptions { Unique = true };
        var indexModel = new CreateIndexModel<StoredMovie>(
            Builders<StoredMovie>.IndexKeys.Ascending(m => m.CsfdId),
            indexOptions
        );
        _moviesCollection.Indexes.CreateOne(indexModel);
    }

    /// <summary>
    /// Stores a movie in the database.
    /// </summary>
    public async Task StoreMovie(Movie movie)
    {
        var storedMovie = new StoredMovie
        {
            CsfdId = movie.CsfdId,
            TmdbId = movie.TmdbId,
            ImdbId = movie.ImdbId,
            Title = movie.Title,
            OriginalTitle = movie.OriginalTitle,
            Year = movie.Year,
            Description = movie.Description,
            Origin = movie.Origin,
            Genres = movie.Genres,
            Directors = movie.Directors,
            Cast = movie.Cast,
            PosterUrl = movie.PosterUrl,
            BackdropUrl = movie.BackdropUrl,
            VoteAverage = movie.VoteAverage,
            VoteCount = movie.VoteCount,
            Popularity = movie.Popularity,
            OriginalLanguage = movie.OriginalLanguage,
            Adult = movie.Adult,
            LocalizedTitles = movie.LocalizedTitles,
            StoredAt = DateTime.UtcNow
        };

        try
        {
            await _moviesCollection.ReplaceOneAsync(
                Builders<StoredMovie>.Filter.Eq(m => m.CsfdId, movie.CsfdId),
                storedMovie,
                new ReplaceOptions { IsUpsert = true }
            );
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException($"Failed to store movie with ID {movie.CsfdId}", ex);
        }
    }

    /// <summary>
    /// Retrieves a movie by CSFD ID from the database.
    /// </summary>
    public async Task<Movie?> GetMovie(int csfdId)
    {
        try
        {
            var storedMovie = await _moviesCollection.Find(m => m.CsfdId == csfdId).FirstOrDefaultAsync();
            
            if (storedMovie == null)
                return null;

            return new Movie
            {
                CsfdId = storedMovie.CsfdId,
                TmdbId = storedMovie.TmdbId,
                ImdbId = storedMovie.ImdbId,
                Title = storedMovie.Title,
                OriginalTitle = storedMovie.OriginalTitle,
                Year = storedMovie.Year,
                Description = storedMovie.Description,
                Origin = storedMovie.Origin,
                Genres = storedMovie.Genres,
                Directors = storedMovie.Directors,
                Cast = storedMovie.Cast,
                PosterUrl = storedMovie.PosterUrl,
                BackdropUrl = storedMovie.BackdropUrl,
                VoteAverage = storedMovie.VoteAverage,
                VoteCount = storedMovie.VoteCount,
                Popularity = storedMovie.Popularity,
                OriginalLanguage = storedMovie.OriginalLanguage,
                Adult = storedMovie.Adult,
                LocalizedTitles = storedMovie.LocalizedTitles
            };
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException($"Failed to retrieve movie with ID {csfdId}", ex);
        }
    }

    /// <summary>
    /// Tests the MongoDB connection.
    /// </summary>
    public async Task<bool> TestConnection()
    {
        try
        {
            await _database.Client.GetDatabase("admin")
                .RunCommandAsync((Command<BsonDocument>)"{ping:1}");
            return true;
        }
        catch (MongoException)
        {
            return false;
        }
    }

}
