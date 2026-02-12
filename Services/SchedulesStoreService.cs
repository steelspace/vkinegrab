using vkinegrab.Models;

namespace vkinegrab.Services;

public class SchedulesStoreService
{
    private readonly IPerformancesService performancesService;
    private readonly IDatabaseService databaseService;

    public SchedulesStoreService(IPerformancesService performancesService, IDatabaseService databaseService)
    {
        this.performancesService = performancesService;
        this.databaseService = databaseService;
    }

    /// <summary>
    /// Fetches schedules (and any discovered venues) and stores schedules and venues into the database.
    /// Returns counts of stored/failed schedules and venues.
    /// </summary>
    public async Task<(IReadOnlyList<Schedule> Schedules, int StoredSchedules, int FailedSchedules, int StoredVenues, int FailedVenues)> StoreSchedulesAndVenuesAsync(
        System.Uri? pageUri = null,
        string period = "today",
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[SchedulesStoreService] Starting schedule grab for period '{period}'");
        
        // Clear existing schedules so we only store fresh schedules from the performances page
        Console.WriteLine($"[SchedulesStoreService] Clearing existing schedules from database...");
        await databaseService.ClearSchedulesAsync().ConfigureAwait(false);
        Console.WriteLine($"[SchedulesStoreService] ✓ Database cleared");

        var (schedules, venues) = await performancesService.GetSchedulesWithVenues(pageUri, period, cancellationToken).ConfigureAwait(false);
        
        Console.WriteLine($"[SchedulesStoreService] Received {schedules.Count} schedule(s) and {venues.Count} venue(s) from scraper");

        // Reuse existing overload to perform storage and deduplication
        var (storedSchedules, failedSchedules, storedVenues, failedVenues) = await StoreSchedulesAndVenuesAsync((IReadOnlyList<Schedule>)schedules, venues, cancellationToken).ConfigureAwait(false);

        return (schedules, storedSchedules, failedSchedules, storedVenues, failedVenues);
    }

    /// <summary>
    /// Stores provided schedules and venues (deduplicating venues by Id).
    /// </summary>
    public async Task<(int StoredSchedules, int FailedSchedules, int StoredVenues, int FailedVenues)> StoreSchedulesAndVenuesAsync(
        IReadOnlyList<Schedule> schedules,
        IReadOnlyList<Venue> venues,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[SchedulesStoreService] Storing {schedules.Count} schedule(s) to database...");
        
        int storedSchedulesCount = 0;
        int failedSchedulesCount = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 10,
            CancellationToken = cancellationToken
        };

        var startTime = DateTime.UtcNow;
        await Parallel.ForEachAsync(schedules, parallelOptions, async (s, ct) =>
        {
            try
            {
                await databaseService.StoreSchedule(s).ConfigureAwait(false);
                var count = Interlocked.Increment(ref storedSchedulesCount);
                if (count % 50 == 0)
                {
                    Console.WriteLine($"[SchedulesStoreService] Progress: {count}/{schedules.Count} schedules stored...");
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failedSchedulesCount);
                Console.WriteLine($"[SchedulesStoreService] ⚠️ Failed to store schedule for movie {s.MovieId} on {s.Date}: {ex.Message}");
            }
        });
        
        var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
        Console.WriteLine($"[SchedulesStoreService] ✓ Stored {storedSchedulesCount} schedule(s) in {elapsed:F1}s");
        if (failedSchedulesCount > 0)
        {
            Console.WriteLine($"[SchedulesStoreService] ⚠️ Failed to store {failedSchedulesCount} schedule(s)");
        }

        var venuesToStore = venues.Where(v => v.Id > 0).GroupBy(v => v.Id).Select(g => g.First()).ToList();

        var storedVenues = 0;
        var failedVenues = 0;

        if (venuesToStore.Count > 0)
        {
            Console.WriteLine($"[SchedulesStoreService] Storing {venuesToStore.Count} venue(s)...");
            try
            {
                await databaseService.StoreVenues(venuesToStore).ConfigureAwait(false);
                storedVenues = venuesToStore.Count;
                Console.WriteLine($"[SchedulesStoreService] ✓ Stored {storedVenues} venue(s)");
            }
            catch (Exception ex)
            {
                failedVenues = venuesToStore.Count;
                Console.WriteLine($"[SchedulesStoreService] ⚠️ Failed to store venues: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"[SchedulesStoreService] No venues to store");
        }

        return (storedSchedulesCount, failedSchedulesCount, storedVenues, failedVenues);
    }
}
