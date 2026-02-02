using System.Threading;
using vkinegrab.Models;

namespace vkinegrab.Services
{
    public interface IPerformancesService
    {
        Task<IReadOnlyList<Schedule>> GetSchedules(Uri? pageUri = null, string period = "today", CancellationToken cancellationToken = default);

        // Returns schedules and the list of venues discovered on the page (best-effort from the performances HTML)
        Task<(IReadOnlyList<Schedule> Schedules, IReadOnlyList<Venue> Venues)> GetSchedulesWithVenues(Uri? pageUri = null, string period = "today", CancellationToken cancellationToken = default);
    }
}
