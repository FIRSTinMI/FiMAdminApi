using FiMAdminApi.Data.Enums;

namespace FiMAdminApi.Clients;

public static class ClientsStartupExtensions
{
    public static void AddClients(this IServiceCollection services)
    {
        services.AddHttpClient(DataSources.FrcEvents.ToString(), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddHttpClient(DataSources.BlueAlliance.ToString());
        services.AddHttpClient(DataSources.FtcEvents.ToString());
        services.AddKeyedScoped<IDataClient, FrcEventsDataClient>(DataSources.FrcEvents);
        services.AddKeyedScoped<IDataClient, BlueAllianceDataClient>(DataSources.BlueAlliance);
        services.AddKeyedScoped<IDataClient, FtcEventsDataClient>(DataSources.FtcEvents);
        services.AddScoped<BlueAllianceWriteClient>();
    }
}