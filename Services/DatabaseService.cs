using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using vkinegrab.Models;

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
    /// Retrieves all movies from the database.
    /// </summary>
    public async Task<List<Movie>> GetAllMovies()
    {
        try
        {
            var storedMovies = await _moviesCollection.Find(_ => true).ToListAsync();
            
            return storedMovies.Select(sm => new Movie
            {
                CsfdId = sm.CsfdId,
                TmdbId = sm.TmdbId,
                ImdbId = sm.ImdbId,
                Title = sm.Title,
                OriginalTitle = sm.OriginalTitle,
                Year = sm.Year,
                Description = sm.Description,
                Origin = sm.Origin,
                Genres = sm.Genres,
                Directors = sm.Directors,
                Cast = sm.Cast,
                PosterUrl = sm.PosterUrl,
                BackdropUrl = sm.BackdropUrl,
                VoteAverage = sm.VoteAverage,
                VoteCount = sm.VoteCount,
                Popularity = sm.Popularity,
                OriginalLanguage = sm.OriginalLanguage,
                Adult = sm.Adult,
                LocalizedTitles = sm.LocalizedTitles
            }).ToList();
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException("Failed to retrieve movies from database", ex);
        }
    }

    /// <summary>
    /// Deletes a movie by CSFD ID from the database.
    /// </summary>
    public async Task DeleteMovie(int csfdId)
    {
        try
        {
            await _moviesCollection.DeleteOneAsync(m => m.CsfdId == csfdId);
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException($"Failed to delete movie with ID {csfdId}", ex);
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

    /// <summary>
    /// Internal model for storing movies in MongoDB.
    /// </summary>
    private class StoredMovie
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("csfd_id")]
        public int CsfdId { get; set; }

        [BsonElement("tmdb_id")]
        public int? TmdbId { get; set; }

        [BsonElement("imdb_id")]
        public string? ImdbId { get; set; }

        [BsonElement("title")]
        public string? Title { get; set; }

        [BsonElement("original_title")]
        public string? OriginalTitle { get; set; }

        [BsonElement("year")]
        public string? Year { get; set; }

        [BsonElement("description")]
        public string? Description { get; set; }

        [BsonElement("origin")]
        public string? Origin { get; set; }

        [BsonElement("genres")]
        public List<string> Genres { get; set; } = new();

        [BsonElement("directors")]
        public List<string> Directors { get; set; } = new();

        [BsonElement("cast")]
        public List<string> Cast { get; set; } = new();

        [BsonElement("poster_url")]
        public string? PosterUrl { get; set; }

        [BsonElement("backdrop_url")]
        public string? BackdropUrl { get; set; }

        [BsonElement("vote_average")]
        public double? VoteAverage { get; set; }

        [BsonElement("vote_count")]
        public int? VoteCount { get; set; }

        [BsonElement("popularity")]
        public double? Popularity { get; set; }

        [BsonElement("original_language")]
        public string? OriginalLanguage { get; set; }

        [BsonElement("adult")]
        public bool? Adult { get; set; }

        [BsonElement("localized_titles")]
        public Dictionary<string, string> LocalizedTitles { get; set; } = new();

        [BsonElement("stored_at")]
        public DateTime StoredAt { get; set; }
    }
}
