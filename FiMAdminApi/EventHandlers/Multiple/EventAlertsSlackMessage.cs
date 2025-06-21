using FiMAdminApi.Clients.Extensions;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Events;
using FiMAdminApi.Services;

namespace FiMAdminApi.EventHandlers.Multiple;

public class EventAlertsSlackMessage(SlackService slackService) :
    IEventHandler<QualSchedulePublished>,
    IEventHandler<QualsComplete>,
    IEventHandler<PlayoffsComplete>
{
    public async Task Handle(QualSchedulePublished evt)
    {
        await SendMessage($"{evt.Event.Name} has published their qualification schedule", "View Schedule",
            evt.Event.GetWebUrl(WebUrlType.QualSchedule));
    }

    public async Task Handle(QualsComplete evt)
    {
        await SendMessage($"{evt.Event.Name} has finished their qualification matches", null, null);
    }

    public async Task Handle(PlayoffsComplete evt)
    {
        await SendMessage($"{evt.Event.Name} has completed their event", null, null);
    }

    private Task SendMessage(string message, string? buttonText, string? buttonUrl)
    {
        return slackService.SendMessage(SlackChannel.EventAlerts, $":arrow_right: {message}", buttonText, buttonUrl);
    }
}