using System.Diagnostics;
using FiMAdminApi.Services;

namespace FiMAdminApi.EventHandlers.Qual1ScoresPosted;

/// <summary>
/// For official FRC events, post a message in the Slack channel dedicated to communications between FiM AV and HQ.
/// </summary>
public class AvHqSlackMessage(SlackService slackService) : IEventHandler<Events.Qual1ScoresPosted>
{
    public async Task Handle(Events.Qual1ScoresPosted evt)
    {
        Debug.Assert(evt.Event.Season?.Level is not null);
        
        if (!evt.Event.IsOfficial ||
            evt.Event.Season.Level.Name != "FRC" ||
            evt.Event.StartTime > DateTime.UtcNow ||
            evt.Event.EndTime < DateTime.UtcNow)
            return;

        var message = $":memo: {evt.Event.Code} has posted scores for QM1";

        await slackService.SendMessage(SlackChannel.AvHqChannel, message);
    }
}
