using FiMAdminApi.Clients;
using FiMAdminApi.Models.Models;

namespace FiMAdminApi.EventSync.Steps;

/// <summary>
/// We can assume that an event is truly over if there is a "*Winner*" or "*Winning*" award populated
/// </summary>
public class DetectEventOver() : EventSyncStep([EventStatus.PlayoffsInProgress, EventStatus.WinnerDetermined])
{
    public override async Task RunStep(Event evt, IDataClient dataClient)
    {
        var awards = await dataClient.GetAwardsForEvent(evt);

        if (awards.Any(a => a.TeamNumber != null && (a.Name.Contains("Winner") || a.Name.Contains("Winning"))))
        {
            evt.Status = EventStatus.Completed;
        }
    }
}