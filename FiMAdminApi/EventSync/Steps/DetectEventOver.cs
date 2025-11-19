using FiMAdminApi.Clients;
using FiMAdminApi.EventHandlers;
using FiMAdminApi.Events;
using FiMAdminApi.Models.Models;

namespace FiMAdminApi.EventSync.Steps;

/// <summary>
/// We can assume that an event is truly over if there is a "*Winner*" or "*Winning*" award populated.
/// Otherwise, if the end time of the event has passed we can just go ahead and complete it.
/// </summary>
public class DetectEventOver(EventPublisher eventPublisher)
    : EventSyncStep([EventStatus.PlayoffsInProgress, EventStatus.WinnerDetermined])
{
    public override bool ShouldRun(Event evt)
    {
        return base.ShouldRun(evt) || evt.EndTime <= DateTime.UtcNow;
    }

    public override async Task RunStep(Event evt, IDataClient dataClient)
    {
        if (evt.EndTime <= DateTime.UtcNow || (await dataClient.GetAwardsForEvent(evt)).Any(a =>
                a.TeamNumber != null && (a.Name.Contains("Winner") || a.Name.Contains("Winning"))))
        {
            evt.Status = EventStatus.Completed;
            await eventPublisher.Publish(new EventCompleted(evt));
        }
    }
}