using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Models;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Services;

public class EventTeamsService(DataContext dbContext, IServiceProvider serviceProvider)
{
    public async Task UpsertEventTeams(Event evt)
    {
        var dataClient = serviceProvider.GetKeyedService<IDataClient>(evt.SyncSource);
        
        if (evt.Code is null || dataClient is null)
        {
            throw new ApplicationException("Unable to get data client for event");
        }

        if (evt.Season is null)
        {
            throw new ApplicationException("Season must be included to populate teams");
        }
        
        var existingTeams = await dbContext.EventTeams.Where(t => t.EventId == evt.Id).ToListAsync();
        var apiTeams = await dataClient.GetTeamsForEvent(evt.Season, evt.Code);
        
        // Delete removed teams
        var removedTeamNumbers = existingTeams.Select(et => et.TeamNumber)
            .Except(apiTeams.Select(at => at.TeamNumber));
        await dbContext.EventTeams.Where(t => t.EventId == evt.Id && removedTeamNumbers.Contains(t.TeamNumber))
            .ExecuteUpdateAsync(b => b.SetProperty(t => t.StatusId, KnownEventTeamStatuses.Dropped));
        
        // Insert new teams
        var addedTeamNumbers = apiTeams.ExceptBy(existingTeams.Select(et => et.TeamNumber), at => at.TeamNumber);
        await dbContext.EventTeams.AddRangeAsync(addedTeamNumbers.Select(at => new EventTeam
        {
            EventId = evt.Id,
            TeamNumber = at.TeamNumber,
            LevelId = evt.Season.LevelId,
            Notes = null,
            StatusId = KnownEventTeamStatuses.NotArrived
        }));
        await dbContext.SaveChangesAsync();
    }
}