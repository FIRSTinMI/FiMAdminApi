using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Data.Firebase;
using FiMAdminApi.Models.Models;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.EventSync.Steps;

public class UpdateQualRankings(DataContext dbContext, FrcFirebaseRepository firebaseRepository)
    : EventSyncStep([EventStatus.QualsInProgress, EventStatus.AwaitingAlliances])
{
    public override async Task RunStep(Event evt, IDataClient eventDataClient)
    {
        var rankings = await eventDataClient.GetQualRankingsForEvent(evt);

        if (rankings.Count == 0) return;

        var dbRankings = await dbContext.EventRankings.Where(r => r.EventId == evt.Id).ToDictionaryAsync(r => r.TeamNumber);

        // This should never happen, but just in case
        var removedRankings = dbRankings.ExceptBy(rankings.Select(r => r.TeamNumber), r => r.Key);
        foreach (var (_, removedRanking) in removedRankings)
        {
            dbContext.EventRankings.Remove(removedRanking);
        }

        foreach (var ranking in rankings)
        {
            // If there's already a ranking in the DB for the team, reuse it. Otherwise create a new one
            var dbRanking = dbRankings.GetValueOrDefault(ranking.TeamNumber, new EventRanking
            {
                EventId = evt.Id,
                TeamNumber = ranking.TeamNumber,
                Rank = ranking.Rank
            });

            dbRanking.Rank = ranking.Rank;
            dbRanking.SortOrders =
            [
                ranking.SortOrder1, ranking.SortOrder2, ranking.SortOrder3, ranking.SortOrder4, ranking.SortOrder5,
                ranking.SortOrder6
            ];
            dbRanking.Wins = ranking.Wins;
            dbRanking.Ties = ranking.Ties;
            dbRanking.Losses = ranking.Losses;
            dbRanking.QualAverage = ranking.QualAverage;
            dbRanking.Disqualifications = ranking.Disqualifications;
            dbRanking.MatchesPlayed = ranking.MatchesPlayed;

            if (dbRanking.Id == 0)
            {
                dbContext.EventRankings.Add(dbRanking);
            }
        }

        // TODO: is this the best option? We could more closely keep track of the full list in this sync step
        await firebaseRepository.UpdateEventRankings(evt, dbContext.EventRankings.Local.ToList());

        await dbContext.SaveChangesAsync();
    }
}