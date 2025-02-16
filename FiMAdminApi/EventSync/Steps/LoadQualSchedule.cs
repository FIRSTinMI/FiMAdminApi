using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;
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
            await dbContext.ScheduleDeviations.Where(d =>
                    d.EventId == evt.Id && d.AfterMatch != null &&
                    d.AfterMatch.TournamentLevel == TournamentLevel.Qualification)
                .ExecuteDeleteAsync();
            
            await dbContext.Matches
                .Where(m => m.EventId == evt.Id && m.TournamentLevel == TournamentLevel.Qualification)
                .ExecuteDeleteAsync();

            var dbMatches = schedule.Select(m => new Match
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
            }).ToList();
            
            await dbContext.Matches.AddRangeAsync(dbMatches);

            await dbContext.SaveChangesAsync();

            // Add in common schedule deviations
            for (var i = 0; i < dbMatches.Count; i++)
            {
                var currentMatch = dbMatches[i];
                var nextMatch = dbMatches.ElementAtOrDefault(i + 1);

                if (nextMatch is null) continue;
                if (currentMatch.ScheduledStartTime is null || nextMatch.ScheduledStartTime is null) continue;

                var startDifference = nextMatch.ScheduledStartTime - currentMatch.ScheduledStartTime;
                if (startDifference.Value.TotalHours > 8)
                {
                    dbContext.ScheduleDeviations.Add(new ScheduleDeviation
                    {
                        EventId = evt.Id,
                        Description = "End of Day",
                        AfterMatchId = currentMatch.Id,
                        AssociatedMatchId = null
                    });
                } else if (startDifference.Value.TotalHours > 0.7)
                {
                    dbContext.ScheduleDeviations.Add(new ScheduleDeviation
                    {
                        EventId = evt.Id,
                        Description = "Break",
                        AfterMatchId = currentMatch.Id,
                        AssociatedMatchId = null
                    });
                }
            }

            evt.Status = EventStatus.QualsInProgress;

            await dbContext.SaveChangesAsync();
        }
    }
}