using FiMAdminApi.EventSync.Steps;

namespace FiMAdminApi.EventSync;

public static class EventSyncServiceExtensions
{
    public static void AddEventSyncSteps(this IServiceCollection services)
    {
        var steps = new[]
        {
            typeof(InitialSync),
            typeof(LoadQualSchedule),
            typeof(UpdateQualResults)
        };

        foreach (var step in steps)
        {
            services.AddScoped(typeof(EventSyncStep), step);
        }
    }
}