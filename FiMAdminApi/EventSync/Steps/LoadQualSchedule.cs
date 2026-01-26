using FiMAdminApi.Clients;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Data.Firebase;
using FiMAdminApi.EventHandlers;
using FiMAdminApi.Events;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;
using FiMAdminApi.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.EventSync.Steps;

public class LoadQualSchedule(DataContext dbContext, EventPublisher eventPublisher, FrcFirebaseRepository firebaseRepo) : EventSyncStep([EventStatus.AwaitingQuals])
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
            var dbDeviations = new List<ScheduleDeviation>();
            
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
                    dbDeviations.Add(new ScheduleDeviation
                    {
                        EventId = evt.Id,
                        Description = "End of Day",
                        AfterMatchId = currentMatch.Id,
                        AssociatedMatchId = null
                    });
                } else if (startDifference.Value.TotalHours > 0.7)
                {
                    var timeZone = TimeZoneInfo.FindSystemTimeZoneById(evt.TimeZone);
                    var isAroundMidday =
                        TimeZoneInfo.ConvertTimeFromUtc(currentMatch.ScheduledStartTime.Value, timeZone).Hour is >= 11
                            and <= 13;
                    
                    dbDeviations.Add(new ScheduleDeviation
                    {
                        EventId = evt.Id,
                        Description = isAroundMidday ? "Lunch" : "Break",
                        AfterMatchId = currentMatch.Id,
                        AssociatedMatchId = null
                    });
                }
            }
            
            dbContext.ScheduleDeviations.AddRange(dbDeviations);

            await firebaseRepo.UpdateEventQualMatches(evt, dbMatches, dbDeviations);

            if (evt.Status == EventStatus.AwaitingQuals)
            {
                evt.Status = EventStatus.QualsInProgress;
                await eventPublisher.Publish(new QualSchedulePublished(evt));
            }

            await dbContext.SaveChangesAsync();
        }
    }
}