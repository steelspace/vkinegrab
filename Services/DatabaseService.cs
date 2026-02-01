using MongoDB.Bson;
using MongoDB.Driver;
using vkinegrab.Models;
using vkinegrab.Models.Dtos;

namespace vkinegrab.Services;

/// <summary>
/// Service for storing and retrieving movie data from MongoDB.
/// </summary>
public class DatabaseService : IDatabaseService
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
        
        InitializeIndexes();
    }

    // Internal constructor for tests to inject a mock database
    internal DatabaseService(IMongoDatabase database)
    {
        this.database = database;
        moviesCollection = database.GetCollection<MovieDto>("movies");
        schedulesCollection = database.GetCollection<ScheduleDto>("schedule");

        InitializeIndexes();
    }

    private void InitializeIndexes()
    {
        try
        {
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
        catch (Exception ex)
        {
            // Don't fail the entire application during initialization just because MongoDB is unreachable.
            // This typically happens when DNS/network prevents connecting to the configured host.
            Console.WriteLine($"⚠ Warning: Unable to initialize MongoDB indexes: {ex.Message}");
        }
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
            var filter = Builders<MovieDto>.Filter.Eq(m => m.CsfdId, movie.CsfdId);
            var update = Builders<MovieDto>.Update
                .Set(m => m.CsfdId, storedMovie.CsfdId)
                .Set(m => m.TmdbId, storedMovie.TmdbId)
                .Set(m => m.ImdbId, storedMovie.ImdbId)
                .Set(m => m.Title, storedMovie.Title)
                .Set(m => m.OriginalTitle, storedMovie.OriginalTitle)
                .Set(m => m.Year, storedMovie.Year)
                .Set(m => m.Description, storedMovie.Description)
                .Set(m => m.Origin, storedMovie.Origin)
                .Set(m => m.Genres, storedMovie.Genres)
                .Set(m => m.Directors, storedMovie.Directors)
                .Set(m => m.Cast, storedMovie.Cast)
                .Set(m => m.PosterUrl, storedMovie.PosterUrl)
                .Set(m => m.BackdropUrl, storedMovie.BackdropUrl)
                .Set(m => m.VoteAverage, storedMovie.VoteAverage)
                .Set(m => m.VoteCount, storedMovie.VoteCount)
                .Set(m => m.Popularity, storedMovie.Popularity)
                .Set(m => m.OriginalLanguage, storedMovie.OriginalLanguage)
                .Set(m => m.Adult, storedMovie.Adult)
                .Set(m => m.LocalizedTitles, storedMovie.LocalizedTitles)
                .Set(m => m.StoredAt, storedMovie.StoredAt);

            await moviesCollection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true }
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

            var update = Builders<ScheduleDto>.Update
                .Set(s => s.Date, dto.Date)
                .Set(s => s.MovieId, dto.MovieId)
                .Set(s => s.MovieTitle, dto.MovieTitle)
                .Set(s => s.Performances, dto.Performances)
                .Set(s => s.StoredAt, dto.StoredAt);

            await schedulesCollection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true }
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
    /// Retrieves all schedules stored in the database and maps them to domain objects.
    /// </summary>
    public async Task<IReadOnlyList<Schedule>> GetSchedulesAsync()
    {
        try
        {
            var dtos = await schedulesCollection.Find(_ => true).ToListAsync();
            var schedules = dtos.Select(dto =>
            {
                var s = dto.ToSchedule();
                s.Populate(dto);
                return s;
            }).ToList();

            return schedules;
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException("Failed to retrieve schedules from database", ex);
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
