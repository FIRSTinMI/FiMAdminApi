using FiMAdminApi.Clients;
using FiMAdminApi.Data.Models;

namespace FiMAdminApi.EventSync.Steps;

public class InitialSync() : EventSyncStep([EventStatus.NotStarted])
{
    public override Task RunStep(Event evt, IDataClient _)
    {
        evt.Status = EventStatus.AwaitingQuals;
        return Task.CompletedTask;
    }
}