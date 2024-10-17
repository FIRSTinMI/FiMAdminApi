using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.EventSync;

public class EventSyncService(DataContext dbContext, IServiceProvider services, ILogger<EventSyncService> logger)
{
    /// <summary>
    /// Attempt to run <see cref="EventSyncStep"/>s against an event until the status of the event stabilizes. A given
    /// step will be run at most once in a sync.
    /// </summary>
    public async Task<EventSyncResult> SyncEvent(Event evt)
    {
        if (evt.Season is null) throw new ArgumentException("Event season data is missing");
        if (evt.SyncSource is null || evt.Code is null) throw new ArgumentException("Event not set up for syncing");

        var dataSource = services.GetRequiredKeyedService<IDataClient>(evt.SyncSource);

        var syncSteps = services.GetServices<EventSyncStep>().ToList();
        var alreadyRunSteps = new List<Type>();

        var runAgain = true; // We want to run until we're able to go a full iteration without running any steps.
        while (runAgain)
        {
            runAgain = false;
            foreach (var step in syncSteps.Where(s =>
                         !alreadyRunSteps.Contains(s.GetType()) && s.ShouldRun(evt.Status)))
            {
                logger.LogInformation("Running sync step {stepName} for event code {code}", step.GetType().Name,
                    evt.Code);
                alreadyRunSteps.Add(step.GetType());
                runAgain = true;
                await step.RunStep(evt, dataSource);
            }
        }

        if (evt.Status is EventStatus.QualsInProgress or EventStatus.AwaitingAlliances)
        {
            // Update matches that have already happened
            var existingQualMatches = await dbContext.Matches.Where(m =>
                m.EventId == evt.Id && m.TournamentLevel == TournamentLevel.Qualification).ToListAsync();


            // any matches that aren't already "done" but are finished according to the API should get their actual and post times set.
            // matches that are done should be checked for data matching, then discard and create a new record if they don't
        }

        await dbContext.SaveChangesAsync();

        return new EventSyncResult(true);
    }
}

public record EventSyncResult(bool Success);