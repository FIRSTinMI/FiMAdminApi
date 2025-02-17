using FiMAdminApi.Clients;
using FiMAdminApi.Models.Models;
using FiMAdminApi.Services;

namespace FiMAdminApi.EventSync.Steps;

public class PopulateEventTeams(EventTeamsService teamsService) : EventSyncStep([EventStatus.NotStarted])
{
    public override async Task RunStep(Event evt, IDataClient eventDataClient)
    {
        await teamsService.UpsertEventTeams(evt);
    }
}