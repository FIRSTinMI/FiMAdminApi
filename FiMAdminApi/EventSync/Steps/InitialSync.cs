using FiMAdminApi.Clients;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.EventHandlers;
using FiMAdminApi.Events;
using FiMAdminApi.Models.Models;
using FiMAdminApi.Services;

namespace FiMAdminApi.EventSync.Steps;

public class InitialSync(EventPublisher eventPublisher, DataContext dbContext, EventStreamService streamService) : EventSyncStep([EventStatus.NotStarted])
{
    public override async Task RunStep(Event evt, IDataClient dataClient)
    {
        evt.Status = EventStatus.AwaitingQuals;

        await streamService.SyncEventStreamsFromDataSource(evt);
        await dbContext.SaveChangesAsync();
        
        if (evt.EndTime > DateTime.UtcNow)
        {
            await eventPublisher.Publish(new EventStarted(evt));
        }
    }
}