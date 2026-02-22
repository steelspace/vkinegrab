using System.Collections.Generic;
using System.Threading.Tasks;
using vkinegrab.Models;

namespace vkinegrab.Services
{
    public interface IDatabaseService
    {
        Task<bool> TestConnection();
        Task StoreMovie(Movie movie);
        Task<Movie?> GetMovie(int csfdId);
        Task<IReadOnlyList<Schedule>> GetSchedulesAsync();
        Task<long> RemoveLegacyOriginCountriesFieldAsync();
        // Retrieval
        Task<IReadOnlyList<Movie>> GetAllMoviesAsync();
        Task<IReadOnlyList<Movie>> GetMoviesWithMissingMetadataAsync();
        Task<IReadOnlyList<Movie>> GetMoviesWithMissingImdbAsync();
        // Schedules
        Task StoreSchedule(Schedule schedule);
        Task StoreSchedules(IEnumerable<Schedule> schedules);

        // Venues
        Task StoreVenue(Venue venue);
        Task<Venue?> GetVenue(int venueId);
        Task StoreVenues(IEnumerable<Venue> venues);
        Task<IReadOnlyList<Venue>> GetVenuesAsync();

        // Deletes all stored schedules. Use when re-grabbing fresh schedules.
        Task ClearSchedulesAsync();

        // Deletes all stored movies.
        Task ClearMoviesAsync();
    }
}
