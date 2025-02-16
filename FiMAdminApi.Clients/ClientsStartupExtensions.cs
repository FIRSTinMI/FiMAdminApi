using FiMAdminApi.Models.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FiMAdminApi.Clients;

public static class ClientsStartupExtensions
{
    public static void AddClients(this IServiceCollection services, bool isProduction)
    {
        services.AddHttpClient(DataSources.FrcEvents.ToString(), client =>
        {
            // This timeout is absurdly long, but sometimes the FRC API is just. that. slow. (mostly on inactive events)
            client.Timeout = TimeSpan.FromSeconds(isProduction ? 30 : 60);
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