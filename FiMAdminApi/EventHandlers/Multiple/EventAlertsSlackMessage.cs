using FiMAdminApi.Clients.Extensions;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Events;
using FiMAdminApi.Services;
using Event = FiMAdminApi.Models.Models.Event;

namespace FiMAdminApi.EventHandlers.Multiple;

public class EventAlertsSlackMessage(SlackService slackService) :
    IEventHandler<QualSchedulePublished>,
    IEventHandler<QualsComplete>,
    IEventHandler<PlayoffsComplete>
{
    private bool TmpIgnoreEvents(Event evt)
    {
        return evt.Season?.Level?.Name != "FRC";
    }
    
    public async Task Handle(QualSchedulePublished evt)
    {
        if (TmpIgnoreEvents(evt.Event)) return;
        await SendMessage($"{evt.Event.Name} has published their qualification schedule", "View Schedule",
            evt.Event.GetWebUrl(WebUrlType.QualSchedule));
    }

    public async Task Handle(QualsComplete evt)
    {
        if (TmpIgnoreEvents(evt.Event)) return;
        await SendMessage($"{evt.Event.Name} has finished their qualification matches", null, null);
    }

    public async Task Handle(PlayoffsComplete evt)
    {
        if (TmpIgnoreEvents(evt.Event)) return;
        if (evt.Event.EndTime < DateTime.UtcNow) return;
        await SendMessage($"{evt.Event.Name} has completed their event", null, null);
    }

    private Task SendMessage(string message, string? buttonText, string? buttonUrl)
    {
        return slackService.SendMessage(SlackChannel.EventAlerts, $":arrow_right: {message}", buttonText, buttonUrl);
    }
}