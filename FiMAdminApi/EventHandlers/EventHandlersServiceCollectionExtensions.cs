using FiMAdminApi.EventHandlers.Multiple;
using FiMAdminApi.EventHandlers.Qual1ScoresPosted;

namespace FiMAdminApi.EventHandlers;

public static class EventHandlersServiceCollectionExtensions
{
    public static IServiceCollection AddEventHandlers(this IServiceCollection services)
    {
        services.AddTransient<EventPublisher>();
        
        services.AddTransient<IEventHandler<Events.Qual1ScoresPosted>, AvHqSlackMessage>();
        services.AddTransient<IEventHandler<Events.QualSchedulePublished>, EventAlertsSlackMessage>();
        services.AddTransient<IEventHandler<Events.QualsComplete>, EventAlertsSlackMessage>();
        services.AddTransient<IEventHandler<Events.PlayoffsComplete>, EventAlertsSlackMessage>();

        return services;
    }
}