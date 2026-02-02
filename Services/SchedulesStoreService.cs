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
        // Clear existing schedules so we only store fresh schedules from the performances page
        await databaseService.ClearSchedulesAsync().ConfigureAwait(false);

        var (schedules, venues) = await performancesService.GetSchedulesWithVenues(pageUri, period, cancellationToken).ConfigureAwait(false);

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
        var storedSchedules = 0;
        var failedSchedules = 0;

        foreach (var s in schedules)
        {
            try
            {
                await databaseService.StoreSchedule(s).ConfigureAwait(false);
                storedSchedules++;
            }
            catch
            {
                failedSchedules++;
            }
        }

        var venuesToStore = venues.Where(v => v.Id > 0).GroupBy(v => v.Id).Select(g => g.First()).ToList();

        var storedVenues = 0;
        var failedVenues = 0;

        if (venuesToStore.Count > 0)
        {
            try
            {
                await databaseService.StoreVenues(venuesToStore).ConfigureAwait(false);
                storedVenues = venuesToStore.Count;
            }
            catch
            {
                failedVenues = venuesToStore.Count;
            }
        }

        return (storedSchedules, failedSchedules, storedVenues, failedVenues);
    }
}
