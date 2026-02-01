using MongoDB.Bson;
using MongoDB.Driver;
using vkinegrab.Models;
using vkinegrab.Models.Dtos;

namespace vkinegrab.Services;

/// <summary>
/// Service for storing and retrieving movie data from MongoDB.
/// </summary>
public class DatabaseService
{
    private readonly IMongoDatabase database;
    private readonly IMongoCollection<MovieDto> moviesCollection;
    private readonly IMongoCollection<ScheduleDto> schedulesCollection;

    public DatabaseService(string connectionString)
    {
        var client = new MongoClient(connectionString);
        database = client.GetDatabase("movies");
        moviesCollection = database.GetCollection<MovieDto>("movies");
        schedulesCollection = database.GetCollection<ScheduleDto>("schedule");
        
        // Create index on CsfdId for faster lookups
        var indexOptions = new CreateIndexOptions { Unique = true };
        var indexModel = new CreateIndexModel<MovieDto>(
            Builders<MovieDto>.IndexKeys.Ascending(m => m.CsfdId),
            indexOptions
        );
        moviesCollection.Indexes.CreateOne(indexModel);

        // Create compound unique index on date + movie_id for schedules
        var scheduleIndexOptions = new CreateIndexOptions { Unique = true };
        var scheduleIndex = new CreateIndexModel<ScheduleDto>(
            Builders<ScheduleDto>.IndexKeys.Ascending(s => s.Date).Ascending(s => s.MovieId),
            scheduleIndexOptions
        );
        schedulesCollection.Indexes.CreateOne(scheduleIndex);
    }

    /// <summary>
    /// Stores a movie in the database.
    /// </summary>
    public async Task StoreMovie(Movie movie)
    {
        var storedMovie = new MovieDto
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
            await moviesCollection.ReplaceOneAsync(
                Builders<MovieDto>.Filter.Eq(m => m.CsfdId, movie.CsfdId),
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
    /// Stores a schedule (per date + movie) in the database. Replaces existing document for the same date + movie.
    /// </summary>
    public async Task StoreSchedule(Schedule schedule)
    {
        var dto = schedule.ToDto();

        try
        {
            var filter = Builders<ScheduleDto>.Filter.And(
                Builders<ScheduleDto>.Filter.Eq(s => s.Date, dto.Date),
                Builders<ScheduleDto>.Filter.Eq(s => s.MovieId, dto.MovieId)
            );

            await schedulesCollection.ReplaceOneAsync(
                filter,
                dto,
                new ReplaceOptions { IsUpsert = true }
            );
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException($"Failed to store schedule for movie {schedule.MovieId} on {schedule.Date}", ex);
        }
    }

    /// <summary>
    /// Stores multiple schedules.
    /// </summary>
    public async Task StoreSchedules(IEnumerable<Schedule> schedules)
    {
        foreach (var schedule in schedules)
        {
            await StoreSchedule(schedule);
        }
    }

    /// <summary>
    /// Retrieves a movie by CSFD ID from the database.
    /// </summary>
    public async Task<Movie?> GetMovie(int csfdId)
    {
        try
        {
            var storedMovie = await moviesCollection.Find(m => m.CsfdId == csfdId).FirstOrDefaultAsync();
            
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
            await database.Client.GetDatabase("admin")
                .RunCommandAsync((Command<BsonDocument>)"{ping:1}");
            return true;
        }
        catch (MongoException)
        {
            return false;
        }
    }

}
