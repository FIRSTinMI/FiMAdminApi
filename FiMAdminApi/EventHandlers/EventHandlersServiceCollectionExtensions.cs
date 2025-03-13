using FiMAdminApi.EventHandlers.Qual1ScoresPosted;

namespace FiMAdminApi.EventHandlers;

public static class EventHandlersServiceCollectionExtensions
{
    public static IServiceCollection AddEventHandlers(this IServiceCollection services)
    {
        services.AddTransient<EventPublisher>();
        
        services.AddTransient<IEventHandler<Events.Qual1ScoresPosted>, AvHqSlackMessage>();

        return services;
    }
}