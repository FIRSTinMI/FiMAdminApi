using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Models;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.EventSync.Steps;

public class UpdateQualRankings(DataContext dbContext)
    : EventSyncStep([EventStatus.QualsInProgress, EventStatus.AwaitingAlliances])
{
    public override async Task RunStep(Event evt, IDataClient eventDataClient)
    {
        var rankings = await eventDataClient.GetQualRankingsForEvent(evt);

        if (rankings.Count == 0) return;

        await dbContext.EventRankings.Where(r => r.EventId == evt.Id).ExecuteDeleteAsync();
        
        dbContext.EventRankings.AddRange(rankings.Select(r => new EventRanking
        {
            EventId = evt.Id,
            Rank = r.Rank,
            TeamNumber = r.TeamNumber,
            SortOrders = [r.SortOrder1, r.SortOrder2, r.SortOrder3, r.SortOrder4, r.SortOrder5, r.SortOrder6],
            Wins = r.Wins,
            Ties = r.Ties,
            Losses = r.Losses,
            QualAverage = r.QualAverage,
            Disqualifications = r.Disqualifications,
            MatchesPlayed = r.MatchesPlayed
        }));

        await dbContext.SaveChangesAsync();
    }
}