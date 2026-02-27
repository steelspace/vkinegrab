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
    private readonly IMongoCollection<PremiereDto> premieresCollection;

    public DatabaseService(string connectionString)
    {
        var client = new MongoClient(connectionString);
        database = client.GetDatabase("movies");
        moviesCollection = database.GetCollection<MovieDto>("movies", null);
        schedulesCollection = database.GetCollection<ScheduleDto>("schedule", null);
        venuesCollection = database.GetCollection<VenueDto>("venues", null);
        premieresCollection = database.GetCollection<PremiereDto>("premieres", null);
        
        InitializeIndexes();
    }

    // Internal constructor for tests to inject a mock database
    internal DatabaseService(IMongoDatabase database)
    {
        this.database = database;
        moviesCollection = database.GetCollection<MovieDto>("movies", null);
        schedulesCollection = database.GetCollection<ScheduleDto>("schedule", null);
        venuesCollection = database.GetCollection<VenueDto>("venues", null);
        premieresCollection = database.GetCollection<PremiereDto>("premieres", null);

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

        // Create unique index on premiere CsfdId
        try
        {
            var premiereIndexOptions = new CreateIndexOptions { Unique = true };
            var premiereIndex = new CreateIndexModel<PremiereDto>(
                Builders<PremiereDto>.IndexKeys.Ascending(p => p.CsfdId),
                premiereIndexOptions
            );
            premieresCollection.Indexes.CreateOne(premiereIndex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Warning: Unable to create premiere index: {ex.Message}");
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
            TmdbTitle = movie.TmdbTitle,
            ImdbId = movie.ImdbId,
            IsStudentFilm = movie.IsStudentFilm,
            Title = movie.Title,
            OriginalTitle = movie.OriginalTitle,
            Year = movie.Year,
            Duration = movie.Duration,
            Rating = movie.Rating,
            DescriptionCs = movie.DescriptionCs,
            DescriptionEn = movie.DescriptionEn,
            Origin = movie.Origin,
            OriginCountryCodes = movie.OriginCountryCodes ?? new List<string>(),
            Genres = movie.Genres,
            Directors = movie.Directors,
            Cast = movie.Cast,
            PosterUrl = movie.PosterUrl,
            CsfdPosterUrl = movie.CsfdPosterUrl,
            BackdropUrl = movie.BackdropUrl,
            ImdbRating = movie.ImdbRating,
            ImdbRatingCount = movie.ImdbRatingCount,
            VoteAverage = movie.VoteAverage,
            VoteCount = movie.VoteCount,
            Popularity = movie.Popularity,
            OriginalLanguage = movie.OriginalLanguage,
            Adult = movie.Adult,
            Homepage = movie.Homepage,
            TrailerUrl = movie.TrailerUrl,
            Credits = movie.Credits?.Select(CrewMemberDto.FromModel).ToList() ?? new List<CrewMemberDto>(),
            LocalizedTitles = movie.LocalizedTitles,
            LocalizedDescriptions = movie.LocalizedDescriptions,
            ReleaseDate = movie.ReleaseDate,
            StoredAt = movie.StoredAt ?? DateTime.UtcNow
        };

        try
        {
            var filter = Builders<MovieDto>.Filter.Eq(m => m.CsfdId, movie.CsfdId);
            var update = Builders<MovieDto>.Update
                .Set(m => m.CsfdId, storedMovie.CsfdId)
                .Set(m => m.TmdbId, storedMovie.TmdbId)
                .Set(m => m.TmdbTitle, storedMovie.TmdbTitle)
                .Set(m => m.ImdbId, storedMovie.ImdbId)
                .Set(m => m.Title, storedMovie.Title)
                .Set(m => m.OriginalTitle, storedMovie.OriginalTitle)
                .Set(m => m.Year, storedMovie.Year)
                .Set(m => m.Duration, storedMovie.Duration)
                .Set(m => m.Rating, storedMovie.Rating)
                .Set(m => m.DescriptionCs, storedMovie.DescriptionCs)
                .Set(m => m.DescriptionEn, storedMovie.DescriptionEn)
                .Set(m => m.Origin, storedMovie.Origin)
                .Set(m => m.OriginCountryCodes, storedMovie.OriginCountryCodes)
                .Set(m => m.Genres, storedMovie.Genres)
                .Set(m => m.Directors, storedMovie.Directors)
                .Set(m => m.Cast, storedMovie.Cast)
                .Set(m => m.PosterUrl, storedMovie.PosterUrl)
                .Set(m => m.CsfdPosterUrl, storedMovie.CsfdPosterUrl)
                .Set(m => m.BackdropUrl, storedMovie.BackdropUrl)
                .Set(m => m.ImdbRating, storedMovie.ImdbRating)
                .Set(m => m.ImdbRatingCount, storedMovie.ImdbRatingCount)
                .Set(m => m.IsStudentFilm, storedMovie.IsStudentFilm)
                .Set(m => m.VoteAverage, storedMovie.VoteAverage)
                .Set(m => m.VoteCount, storedMovie.VoteCount)
                .Set(m => m.Popularity, storedMovie.Popularity)
                .Set(m => m.OriginalLanguage, storedMovie.OriginalLanguage)
                .Set(m => m.Adult, storedMovie.Adult)
                .Set(m => m.Homepage, storedMovie.Homepage)
                .Set(m => m.TrailerUrl, storedMovie.TrailerUrl)
                .Set(m => m.Credits, storedMovie.Credits)
                .Set(m => m.LocalizedTitles, storedMovie.LocalizedTitles)
                .Set(m => m.LocalizedDescriptions, storedMovie.LocalizedDescriptions)
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
    /// Clears all stored movies from the database.
    /// </summary>
    public async Task ClearMoviesAsync()
    {
        try
        {
            await moviesCollection.DeleteManyAsync(Builders<MovieDto>.Filter.Empty);
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException("Failed to clear movies", ex);
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
            return storedMovie?.ToMovie();
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException($"Failed to retrieve movie with ID {csfdId}", ex);
        }
    }

    /// <summary>
    /// Retrieves all movies from the database.
    /// </summary>
    public async Task<IReadOnlyList<Movie>> GetAllMoviesAsync()
    {
        try
        {
            var dtos = await moviesCollection.Find(_ => true).ToListAsync();
            return dtos.Select(d => d.ToMovie()).ToList();
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException("Failed to retrieve all movies", ex);
        }
    }

    /// <summary>
    /// Retrieves movies that are missing critical metadata (like TMDB ID or description).
    /// </summary>
    public async Task<IReadOnlyList<Movie>> GetMoviesWithMissingMetadataAsync()
    {
        try
        {
            var filter = Builders<MovieDto>.Filter.Or(
                Builders<MovieDto>.Filter.Eq(m => m.TmdbId, null),
                Builders<MovieDto>.Filter.Eq(m => m.DescriptionCs, null),
                Builders<MovieDto>.Filter.Eq(m => m.DescriptionCs, string.Empty),
                Builders<MovieDto>.Filter.Eq(m => m.DescriptionEn, null),
                Builders<MovieDto>.Filter.Eq(m => m.DescriptionEn, string.Empty)
            );

            var dtos = await moviesCollection.Find(filter).ToListAsync();
            return dtos.Select(d => d.ToMovie()).ToList();
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException("Failed to retrieve movies with missing metadata", ex);
        }
    }

    /// <summary>
    /// Retrieves movies that are missing IMDB data (no ImdbId).
    /// </summary>
    public async Task<IReadOnlyList<Movie>> GetMoviesWithMissingImdbAsync()
    {
        try
        {
            var filter = Builders<MovieDto>.Filter.Or(
                Builders<MovieDto>.Filter.Eq(m => m.ImdbId, null),
                Builders<MovieDto>.Filter.Eq(m => m.ImdbId, string.Empty)
            );

            var dtos = await moviesCollection.Find(filter).ToListAsync();
            return dtos.Select(d => d.ToMovie()).ToList();
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException("Failed to retrieve movies with missing IMDB data", ex);
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

    public async Task StorePremiereAsync(Premiere premiere)
    {
        var dto = premiere.ToDto();

        try
        {
            var filter = Builders<PremiereDto>.Filter.Eq(p => p.CsfdId, dto.CsfdId);
            var update = Builders<PremiereDto>.Update
                .Set(p => p.CsfdId, dto.CsfdId)
                .Set(p => p.PremiereDate, dto.PremiereDate)
                .Set(p => p.StoredAt, dto.StoredAt);

            await premieresCollection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true }
            );
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException($"Failed to store premiere for CSFD ID {premiere.CsfdId}", ex);
        }
    }

    public async Task ClearPremieresAsync()
    {
        try
        {
            await premieresCollection.DeleteManyAsync(Builders<PremiereDto>.Filter.Empty);
        }
        catch (MongoException ex)
        {
            throw new InvalidOperationException("Failed to clear premieres", ex);
        }
    }

    public async Task<IReadOnlyList<Premiere>> GetPremieresAsync()
    {
        var dtos = await premieresCollection.Find(Builders<PremiereDto>.Filter.Empty).ToListAsync();
        return dtos.Select(d => d.ToModel()).ToList();
    }

    public async Task<long> RemoveLegacyOriginCountriesFieldAsync()
    {
        var update = Builders<MovieDto>.Update.Unset("origin_countries");
        var result = await moviesCollection.UpdateManyAsync(Builders<MovieDto>.Filter.Exists("origin_countries"), update);
        return result.ModifiedCount;
    }

}
