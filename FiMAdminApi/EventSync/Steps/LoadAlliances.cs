using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.EventSync.Steps;

public class LoadAlliances(DataContext dbContext)
    : EventSyncStep([EventStatus.AwaitingAlliances])
{
    public override async Task RunStep(Event evt, IDataClient eventDataClient)
    {
        //var alliances = await eventDataClient.(evt);

        //if (alliances.Count == 0) return;

        await dbContext.SaveChangesAsync();
    }
}