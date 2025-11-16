using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Models;

namespace FiMAdminApi.EventSync;

public class EventSyncService(DataContext dbContext, IServiceProvider services, ILogger<EventSyncService> logger)
{
    /// <summary>
    /// Attempt to run <see cref="EventSyncStep"/>s against an event until the status of the event stabilizes. A given
    /// step will be run at most once in a sync.
    /// </summary>
    /// <remarks>
    /// Nothing in this method should throw. Instead, it should return a sync result reporting not successful and a
    /// message. This ensures that processes which want to sync many events are not short-circuited if one fails.
    /// </remarks>
    public async Task<EventSyncResult> SyncEvent(Event evt)
    {
        dbContext.Events.Attach(evt);
        if (evt.Season?.Level is null)
            return new EventSyncResult(false, "Event season data is missing");
        if (evt.SyncSource is null || evt.Code is null)
            return new EventSyncResult(false, "Event not set up for syncing");

        var dataSource = services.GetRequiredKeyedService<IDataClient>(evt.SyncSource);

        var syncSteps = services.GetServices<EventSyncStep>().ToList();
        var alreadyRunSteps = new List<Type>();

        bool runAgain; // We want to run until we're able to go a full iteration without running any steps.
        do
        {
            runAgain = false;
            foreach (var step in syncSteps.Where(s =>
                         !alreadyRunSteps.Contains(s.GetType()) && s.ShouldRun(evt)))
            {
                logger.LogInformation("Running sync step {stepName} for event code {code}", step.GetType().Name,
                    evt.Code);
                alreadyRunSteps.Add(step.GetType());
                runAgain = true;
                try
                {
                    await step.RunStep(evt, dataSource);
                }
                catch (Exception ex)
                {
                    return new EventSyncResult(false, ex.ToString());
                }
            }
        } while (runAgain);

        await dbContext.SaveChangesAsync();

        return new EventSyncResult(true);
    }

    public async Task<EventSyncResult> ForceEventSyncStep(Event evt, string syncStepName)
    {
        if (evt.Season is null) throw new ArgumentException("Event season data is missing");
        if (evt.SyncSource is null || evt.Code is null) throw new ArgumentException("Event not set up for syncing");

        var dataSource = services.GetRequiredKeyedService<IDataClient>(evt.SyncSource);

        var syncStep = services.GetServices<EventSyncStep>().FirstOrDefault(s => s.GetType().Name == syncStepName);

        if (syncStep is null) return new EventSyncResult(false, "Unable to find sync step");

        try
        {
            await syncStep.RunStep(evt, dataSource);
            await dbContext.SaveChangesAsync();
            return new EventSyncResult(true);
        }
        catch (Exception ex)
        {
            return new EventSyncResult(false, ex.Message);
        }
    }
}

public record EventSyncResult(bool Success, string? Message = null);