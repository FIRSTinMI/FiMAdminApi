using System.Diagnostics;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Services;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.EventHandlers.Playoff1ScoresPosted;

public class AvReportSlackMessage(SlackService slackService, DataContext dbContext, IConfiguration configuration) : IEventHandler<Events.Playoff1ScoresPosted>
{
    public async Task Handle(Events.Playoff1ScoresPosted evt)
    {
        Debug.Assert(evt.Event.Season?.Level is not null);

        var reportUrl = configuration["AvReportUrl"];
        
        if (string.IsNullOrEmpty(reportUrl) ||
            !evt.Event.IsOfficial ||
            evt.Event.Season.Level.Name != "FRC" ||
            evt.Event.StartTime > DateTime.UtcNow ||
            evt.Event.EndTime < DateTime.UtcNow)
            return;
        
        if (evt.Event.TruckRouteId is null) return;
        var equipment = await dbContext.Equipment
            .Where(e => e.TruckRouteId == evt.Event.TruckRouteId && e.SlackUserId != null).ToListAsync();

        if (equipment.Count == 0) return;

        var message = $":memo: Welcome to playoffs {string.Join(" ", equipment.Select(e => $"<@{e.SlackUserId}>"))}! Please make sure to submit an AV report for your event before you leave for the day, thanks again for volunteering with FIM!";

        await slackService.SendMessage(SlackChannel.AvPrivate, message, "AV Report", reportUrl);
    }
}