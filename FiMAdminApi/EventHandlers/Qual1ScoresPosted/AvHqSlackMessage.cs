using SlackNet;
using SlackNet.WebApi;

namespace FiMAdminApi.EventHandlers.Qual1ScoresPosted;

public class AvHqSlackMessage(ISlackApiClient slack, IConfiguration config) : IEventHandler<Events.Qual1ScoresPosted>
{
    public async Task Handle(Events.Qual1ScoresPosted evt)
    {
        var channel = config["Slack:AvHqChannel"];

        if (string.IsNullOrEmpty(channel)) return;
        if (!evt.Event.IsOfficial || evt.Event.StartTime > DateTime.UtcNow ||
            evt.Event.EndTime < DateTime.UtcNow) return;

        var message = $":memo: {evt.Event.Code} has posted scores for QM1";

        await slack.Chat.PostMessage(new Message
        {
            Channel = channel,
            Text = message,
            Parse = ParseMode.Full
        });
    }
}