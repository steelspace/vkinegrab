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
    }
}
