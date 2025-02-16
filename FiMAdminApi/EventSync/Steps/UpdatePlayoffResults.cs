using FiMAdminApi.Clients;
using FiMAdminApi.Clients.PlayoffTiebreaks;
using FiMAdminApi.Data;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.EventSync.Steps;

public class UpdatePlayoffResults(DataContext dbContext, ILogger<UpdatePlayoffResults> logger)
    : EventSyncStep([EventStatus.AwaitingPlayoffs, EventStatus.PlayoffsInProgress])
{
    private static readonly TimeSpan MatchStartTolerance = TimeSpan.FromMinutes(1);

    public override async Task RunStep(Event evt, IDataClient dataClient)
    {
        var apiMatchesTask = dataClient.GetPlayoffResultsForEvent(evt);
        var dbMatches = await dbContext.Matches
            .Where(m => m.EventId == evt.Id && m.TournamentLevel == TournamentLevel.Playoff)
            .ToListAsync();
        var alliances = await dbContext.Alliances.Where(a => a.EventId == evt.Id).ToListAsync();

        var apiMatches = await apiMatchesTask;

        if (evt.Status != EventStatus.PlayoffsInProgress && apiMatches.Count > 0)
        {
            evt.Status = EventStatus.PlayoffsInProgress;
        }

        IPlayoffTiebreak? tiebreak = null;
        foreach (var apiMatch in apiMatches)
        {
            var dbMatch = dbMatches.Where(m => m.MatchNumber == apiMatch.MatchNumber).MaxBy(m => m.PlayNumber)
                          ?? new Match
                                {
                                    EventId = evt.Id,
                                    TournamentLevel = TournamentLevel.Playoff,
                                    MatchName = apiMatch.MatchName,
                                    MatchNumber = apiMatch.MatchNumber,
                                    PlayNumber = 1,
                                    RedAllianceTeams = apiMatch.RedAllianceTeams,
                                    BlueAllianceTeams = apiMatch.BlueAllianceTeams,
                                    IsDiscarded = false,
                                };
            dbContext.Matches.Attach(dbMatch);

            if (apiMatch.ActualStartTime is not null &&
                dbMatch.ActualStartTime is not null &&
                !AreDatesWithinTolerance(dbMatch.ActualStartTime.Value, apiMatch.ActualStartTime.Value, MatchStartTolerance))
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

            dbMatch.ScheduledStartTime = apiMatch.ScheduledStartTime;
            dbMatch.ActualStartTime = apiMatch.ActualStartTime;
            dbMatch.PostResultTime = apiMatch.PostResultTime;
            dbMatch.RedAllianceTeams = apiMatch.RedAllianceTeams;
            dbMatch.BlueAllianceTeams = apiMatch.BlueAllianceTeams;

            // Figure out which alliance played based on which team numbers overlap
            if (dbMatch.RedAllianceId is null && apiMatch.RedAllianceTeams is not null && apiMatch.RedAllianceTeams.Length > 0)
            {
                dbMatch.RedAllianceId = alliances
                        .FirstOrDefault(a => a.TeamNumbers?.Intersect(apiMatch.RedAllianceTeams).Any() ?? false)
                        ?.Id;
            }
            if (dbMatch.BlueAllianceId is null && apiMatch.BlueAllianceTeams is not null && apiMatch.BlueAllianceTeams.Length > 0)
            {
                dbMatch.BlueAllianceId = alliances
                    .FirstOrDefault(a => a.TeamNumbers?.Intersect(apiMatch.BlueAllianceTeams).Any() ?? false)
                    ?.Id;
            }

            if (dbMatch.Winner is null && apiMatch.PostResultTime is not null)
            {
                if (apiMatch.Winner is not null)
                {
                    dbMatch.Winner = apiMatch.Winner;
                }
                else
                {
                    try
                    {
                        tiebreak ??= dataClient.GetPlayoffTiebreak(evt);
                        dbMatch.Winner = await tiebreak.DetermineMatchWinner(apiMatch);
                    }
                    catch (ApplicationException ex)
                    {
                        logger.LogError(ex, "Failed to get winner for tied playoff match in event {eventCode}",
                            evt.Code);
                    }
                }
            }
        }

        await dbContext.SaveChangesAsync();

        // If an alliance has won 2 finals matches, they're the winner of the event
        if (await dbContext.Matches.Where(m =>
                m.EventId == evt.Id && m.TournamentLevel == TournamentLevel.Playoff && m.MatchName != null &&
                m.MatchName.StartsWith("Final")).GroupBy(m => m.Winner).CountAsync(g =>
                (g.Key == MatchWinner.Red || g.Key == MatchWinner.Blue) && g.Count() == 2) > 0)
        {
            evt.Status = EventStatus.WinnerDetermined;
        }
    }

    private static bool AreDatesWithinTolerance(DateTime date1, DateTime date2, TimeSpan tolerance)
    {
        var diff = (date1 - date2).Duration();

        return diff < tolerance;
    }
}