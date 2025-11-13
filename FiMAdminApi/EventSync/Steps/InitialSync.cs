using FiMAdminApi.Clients;
using FiMAdminApi.EventHandlers;
using FiMAdminApi.Events;
using FiMAdminApi.Models.Models;

namespace FiMAdminApi.EventSync.Steps;

public class InitialSync(EventPublisher eventPublisher) : EventSyncStep([EventStatus.NotStarted])
{
    public override async Task RunStep(Event evt, IDataClient _)
    {
        evt.Status = EventStatus.AwaitingQuals;
        
        if (evt.EndTime > DateTime.UtcNow)
        {
            await eventPublisher.Publish(new EventStarted(evt));
        }
    }
}