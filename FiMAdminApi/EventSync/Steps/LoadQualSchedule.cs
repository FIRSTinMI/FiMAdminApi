using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.EventSync.Steps;

public class LoadQualSchedule(DataContext dbContext) : EventSyncStep([EventStatus.AwaitingQuals])
{
    public override async Task RunStep(Event evt, IDataClient dataClient)
    {
        var schedule = await dataClient.GetQualScheduleForEvent(evt);
        if (schedule.Count > 0)
        {
            // Clear out the existing matches and load in a new set
            await dbContext.Matches
                .Where(m => m.EventId == evt.Id && m.TournamentLevel == TournamentLevel.Qualification)
                .ExecuteDeleteAsync();

            await dbContext.Matches.AddRangeAsync(schedule.Select(m => new Match
            {
                EventId = evt.Id,
                TournamentLevel = TournamentLevel.Qualification,
                MatchNumber = m.MatchNumber,
                PlayNumber = 1,
                RedAllianceTeams = m.RedAllianceTeams,
                BlueAllianceTeams = m.BlueAllianceTeams,
                RedAllianceId = null,
                BlueAllianceId = null,
                ScheduledStartTime = m.ScheduledStartTime,
                ActualStartTime = null,
                PostResultTime = null,
                IsDiscarded = false
            }));

            evt.Status = EventStatus.QualsInProgress;
        }
    }
}