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
        var dataTask = eventDataClient.GetAlliancesForEvent(evt);
        var dbAlliances = await dbContext.Alliances.Where(a => a.EventId == evt.Id).ToListAsync();
        var alliances = await dataTask;

        if (alliances.Count == 0) return;

        // Delete alliances that don't exist in the new data
        dbContext.RemoveRange(dbAlliances.ExceptBy(alliances.Select(a => a.Name), a => a.Name));
        
        // Add brand-new alliances
        await dbContext.Alliances.AddRangeAsync(alliances.ExceptBy(dbAlliances.Select(a => a.Name), a => a.Name).Select(
            a => new Alliance
            {
                EventId = evt.Id,
                Name = a.Name,
                TeamNumbers = a.TeamNumbers.ToArray()
            }));

        // Update anything that's left
        foreach (var dbAlliance in dbAlliances.IntersectBy(alliances.Select(a => a.Name), a => a.Name))
        {
            var apiAlliance = alliances.FirstOrDefault(a => a.Name == dbAlliance.Name);
            dbAlliance.TeamNumbers = apiAlliance?.TeamNumbers.ToArray();
        }
        
        evt.Status = EventStatus.AwaitingPlayoffs;
        await dbContext.SaveChangesAsync();
    }
}