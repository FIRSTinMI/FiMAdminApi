using FiMAdminApi.Clients;
using FiMAdminApi.Data.Models;

namespace FiMAdminApi.EventSync.Steps;

public class UpdateQualRankings() : EventSyncStep([EventStatus.QualsInProgress, EventStatus.AwaitingAlliances])
{
    public override Task RunStep(Event evt, IDataClient eventDataClient)
    {
        var rankings = eventDataClient.GetQualRankingsForEvent(evt);

        return Task.CompletedTask;
    }
}