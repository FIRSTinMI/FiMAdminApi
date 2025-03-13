namespace FiMAdminApi.EventHandlers;

public class EventPublisher(IServiceProvider services, ILogger<EventPublisher> logger)
{
    public async Task Publish<T>(T evt)
    {
        logger.LogInformation("Publishing {Type} event: {@Event}", typeof(T).FullName, evt);
        var handlers = services.GetServices<IEventHandler<T>>();

        await Parallel.ForEachAsync(handlers, async (handler, _) =>
        {
            try
            {
                await handler.Handle(evt);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to handle {Event} with handler {Handler}", typeof(T).FullName,
                    handler.GetType().FullName);
            }
        });
    }
}