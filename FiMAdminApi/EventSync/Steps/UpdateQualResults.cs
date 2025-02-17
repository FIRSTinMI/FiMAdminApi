using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.EventSync.Steps;

// Note: this step runs while awaiting alliances to ensure that any replays are picked up
public class UpdateQualResults(DataContext dbContext) : EventSyncStep([EventStatus.QualsInProgress, EventStatus.AwaitingAlliances])
{
    private static readonly TimeSpan MatchStartTolerance = TimeSpan.FromMinutes(1);
    
    public override async Task RunStep(Event evt, IDataClient dataClient)
    {
        // Let the two fetches run in parallel
        var dbMatchesTask = dbContext.Matches
            .Where(m => m.EventId == evt.Id && m.TournamentLevel == TournamentLevel.Qualification).ToListAsync();
        var apiMatchesTask = dataClient.GetQualResultsForEvent(evt);

        var dbMatches = await dbMatchesTask;
        var apiMatches = await apiMatchesTask;

        foreach (var apiMatch in apiMatches)
        {
            var dbMatch = dbMatches.Where(m => m.MatchNumber == apiMatch.MatchNumber).MaxBy(m => m.PlayNumber);
            if (dbMatch is null) continue;

            if (dbMatch.ActualStartTime is not null && !AreDatesWithinTolerance(dbMatch.ActualStartTime.Value, apiMatch.ActualStartTime, MatchStartTolerance))
            {
                // We already have a record of the match being played. Mark the old one as discarded and create new play
                dbMatch.IsDiscarded = true;
                
                var newMatch = new Match
                {
                    EventId = dbMatch.EventId,
                    TournamentLevel = dbMatch.TournamentLevel,
                    MatchNumber = dbMatch.MatchNumber,
                    PlayNumber = dbMatch.PlayNumber + 1,
                    RedAllianceTeams = dbMatch.RedAllianceTeams,
                    BlueAllianceTeams = dbMatch.BlueAllianceTeams,
                    RedAllianceId = dbMatch.RedAllianceId,
                    BlueAllianceId = dbMatch.BlueAllianceId,
                    ScheduledStartTime = null,
                    ActualStartTime = null,
                    PostResultTime = null,
                    IsDiscarded = false
                };
                await dbContext.Matches.AddAsync(newMatch);
                dbMatch = newMatch;
            }

            dbMatch.ActualStartTime = apiMatch.ActualStartTime;
            dbMatch.PostResultTime = apiMatch.PostResultTime;
        }

        await dbContext.SaveChangesAsync();

        if (evt.Status != EventStatus.AwaitingAlliances && await dbContext.Matches.CountAsync(m =>
                m.EventId == evt.Id && m.TournamentLevel == TournamentLevel.Qualification &&
                m.IsDiscarded == false && m.ActualStartTime == null) == 0)
        {
            evt.Status = EventStatus.AwaitingAlliances;
        }
    }

    private static bool AreDatesWithinTolerance(DateTime date1, DateTime date2, TimeSpan tolerance)
    {
        var diff = (date1 - date2).Duration();

        return diff < tolerance;
    }
}