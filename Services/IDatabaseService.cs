using System.Collections.Generic;
using System.Threading.Tasks;
using vkinegrab.Models;

namespace vkinegrab.Services
{
    public interface IDatabaseService
    {
        Task StoreMovie(Movie movie);
        Task<Movie?> GetMovie(int csfdId);
        Task<IReadOnlyList<Schedule>> GetSchedulesAsync();

        // Schedules
        Task StoreSchedule(Schedule schedule);
        Task StoreSchedules(IEnumerable<Schedule> schedules);

        // Venues
        Task StoreVenue(Venue venue);
        Task<Venue?> GetVenue(int venueId);
        Task StoreVenues(IEnumerable<Venue> venues);
        Task<IReadOnlyList<Venue>> GetVenuesAsync();

        // Add canonical venue URL to any schedule performances that reference the venue id but lack the URL
        Task AddVenueUrlToSchedulesAsync(int venueId, string venueUrl);
    }
}
