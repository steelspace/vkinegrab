using System.Threading;
using vkinegrab.Models;

namespace vkinegrab.Services
{
    public interface IPerformancesService
    {
        Task<IReadOnlyList<Schedule>> GetSchedules(Uri? pageUri = null, string period = "today", CancellationToken cancellationToken = default);
    }
}
