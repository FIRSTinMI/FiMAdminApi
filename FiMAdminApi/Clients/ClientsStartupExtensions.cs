using FiMAdminApi.Data.Enums;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FiMAdminApi.Clients;

public static class ClientsStartupExtensions
{
    public static void AddClients(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddHttpClient(DataSources.FrcEvents.ToString(), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(environment.IsProduction() ? 10 : 30);
        });
        services.AddHttpClient(DataSources.BlueAlliance.ToString());
        services.AddHttpClient(DataSources.FtcEvents.ToString());
        services.AddKeyedScoped<IDataClient, FrcEventsDataClient>(DataSources.FrcEvents);
        services.AddKeyedScoped<IDataClient, BlueAllianceDataClient>(DataSources.BlueAlliance);
        services.AddKeyedScoped<IDataClient, FtcEventsDataClient>(DataSources.FtcEvents);
        services.AddScoped<BlueAllianceWriteClient>();
    }

    public static IHealthChecksBuilder AddClientHealthChecks(this IHealthChecksBuilder builder)
    {
        foreach (var source in Enum.GetValues<DataSources>())
        {
            builder.AddTypeActivatedCheck<ClientHealthCheck>(source.ToString(), HealthStatus.Degraded,
                new[] { "DataClient" }, source);
        }

        return builder;
    }
}