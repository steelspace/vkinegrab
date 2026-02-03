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
    private readonly IMongoCollection<VenueDto> venuesCollection;

    public DatabaseService(string connectionString)
    {
        var client = new MongoClient(connectionString);
        database = client.GetDatabase("movies");
        moviesCollection = database.GetCollection<MovieDto>("movies", null);
        schedulesCollection = database.GetCollection<ScheduleDto>("schedule", null);
        venuesCollection = database.GetCollection<VenueDto>("venues", null);
        
        InitializeIndexes();
    }

    // Internal constructor for tests to inject a mock database
    internal DatabaseService(IMongoDatabase database)
    {
        this.database = database;
        moviesCollection = database.GetCollection<MovieDto>("movies", null);
        schedulesCollection = database.GetCollection<ScheduleDto>("schedule", null);
        venuesCollection = database.GetCollection<VenueDto>("venues", null);

        InitializeIndexes();
    }

    private void InitializeIndexes()
    {
        // Create index on CsfdId for faster lookups
        try
        {
            var indexOptions = new CreateIndexOptions { Unique = true };
            var indexModel = new CreateIndexModel<MovieDto>(
                Builders<MovieDto>.IndexKeys.Ascending(m => m.CsfdId),
                indexOptions
            );
            moviesCollection.Indexes.CreateOne(indexModel);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Warning: Unable to create movie index: {ex.Message}");
        }

        // Create compound unique index on date + movie_id for schedules
        try
        {
            var scheduleIndexOptions = new CreateIndexOptions { Unique = true };
            var scheduleIndex = new CreateIndexModel<ScheduleDto>(
                Builders<ScheduleDto>.IndexKeys.Ascending(s => s.Date).Ascending(s => s.MovieId),
                scheduleIndexOptions
            );
            schedulesCollection.Indexes.CreateOne(scheduleIndex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Warning: Unable to create schedule index: {ex.Message}");
        }

        // Create unique index on venue Id
        try
        {
            var venueIndexOptions = new CreateIndexOptions { Unique = true };
            var venueIndex = new CreateIndexModel<VenueDto>(
                Builders<VenueDto>.IndexKeys.Ascending(v => v.VenueId),
                venueIndexOptions
            );
            venuesCollection.Indexes.CreateOne(venueIndex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Warning: Unable to create venue index: {ex.Message}");
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
            OriginCountries = movie.OriginCountries ?? new List<string>(),
            Genres = movie.Genres,
            Directors = movie.Directors,
            Cast = movie.Cast,
            PosterUrl = movie.PosterUrl,
            CsfdPosterUrl = movie.CsfdPosterUrl,
            BackdropUrl = movie.BackdropUrl,
            VoteAverage = movie.VoteAverage,
            VoteCount = movie.VoteCount,
            Popularity = movie.Popularity,
            OriginalLanguage = movie.OriginalLanguage,
            Adult = movie.Adult,
            LocalizedTitles = movie.LocalizedTitles,
            ReleaseDate = movie.ReleaseDate,
            StoredAt = movie.StoredAt ?? DateTime.UtcNow
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
                .Set(m => m.OriginCountries, storedMovie.OriginCountries)
                .Set(m => m.Genres, storedMovie.Genres)
                .Set(m => m.Directors, storedMovie.Directors)
                .Set(m => m.Cast, storedMovie.Cast)
                .Set(m => m.PosterUrl, storedMovie.PosterUrl)
                .Set(m => m.CsfdPosterUrl, storedMovie.CsfdPosterUrl)
                .Set(m => m.BackdropUrl, storedMovie.BackdropUrl)
                .Set(m => m.VoteAverage, storedMovie.VoteAverage)
                .Set(m => m.VoteCount, storedMovie.VoteCount)
                .Set(m => m.Popularity, storedMovie.Popularity)
                .Set(m => m.OriginalLanguage, storedMovie.OriginalLanguage)
                .Set(m => m.Adult, storedMovie.Adult)
                .Set(m => m.LocalizedTitles, storedMovie.LocalizedTitles)
                .Set(m => m.ReleaseDate, storedMovie.ReleaseDate)
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
    /// Clears all stored schedules from the database.
    /// </summary>
    public async Task ClearSchedulesAsync()
    {
        try
        {
            await schedulesCollection.DeleteManyAsync(Builders<ScheduleDto>.Filter.Empty);
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException("Failed to clear schedules", ex);
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
                OriginCountries = storedMovie.OriginCountries ?? new List<string>(),
                Genres = storedMovie.Genres,
                Directors = storedMovie.Directors,
                Cast = storedMovie.Cast,
                PosterUrl = storedMovie.PosterUrl,
                CsfdPosterUrl = storedMovie.CsfdPosterUrl,
                BackdropUrl = storedMovie.BackdropUrl,
                VoteAverage = storedMovie.VoteAverage,
                VoteCount = storedMovie.VoteCount,
                Popularity = storedMovie.Popularity,
                OriginalLanguage = storedMovie.OriginalLanguage,
                Adult = storedMovie.Adult,
                LocalizedTitles = storedMovie.LocalizedTitles,
                ReleaseDate = storedMovie.ReleaseDate,
                StoredAt = storedMovie.StoredAt
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
    /// Stores a venue in the database.
    /// </summary>
    public async Task StoreVenue(Venue venue)
    {
        var dto = venue.ToDto();

        try
        {
            var filter = Builders<VenueDto>.Filter.Eq(v => v.VenueId, dto.VenueId);
            var update = Builders<VenueDto>.Update
                .Set(v => v.VenueId, dto.VenueId)
                .Set(v => v.City, dto.City)
                .Set(v => v.Name, dto.Name)
                .Set(v => v.DetailUrl, dto.DetailUrl)
                .Set(v => v.Address, dto.Address)
                .Set(v => v.MapUrl, dto.MapUrl);

            await venuesCollection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true }
            );
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException($"Failed to store venue with ID {venue.Id}", ex);
        }
    }

    public async Task StoreVenues(IEnumerable<Venue> venues)
    {
        foreach (var v in venues)
        {
            await StoreVenue(v);
        }
    }

    public async Task<IReadOnlyList<Venue>> GetVenuesAsync()
    {
        try
        {
            var dtos = await venuesCollection.Find(_ => true).ToListAsync();
            return dtos.Select(d => d.ToVenue()).ToList();
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException("Failed to retrieve venues from database", ex);
        }
    }

    public async Task<Venue?> GetVenue(int venueId)
    {
        try
        {
            var stored = await venuesCollection.Find(v => v.VenueId == venueId).FirstOrDefaultAsync();
            return stored == null ? null : stored.ToVenue();
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException($"Failed to retrieve venue with ID {venueId}", ex);
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
