using FiMAdminApi.Data.Enums;

namespace FiMAdminApi.Clients;

public static class ClientsStartupExtensions
{
    public static void AddClients(this IServiceCollection services)
    {
        services.AddHttpClient(DataSources.FrcEvents.ToString());
        services.AddHttpClient(DataSources.BlueAlliance.ToString());
        services.AddKeyedScoped<IDataClient, FrcEventsDataClient>(DataSources.FrcEvents);
        services.AddKeyedScoped<IDataClient, BlueAllianceDataClient>(DataSources.BlueAlliance);
    }
}