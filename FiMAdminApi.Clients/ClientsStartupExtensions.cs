using FiMAdminApi.Models.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FiMAdminApi.Clients;

public static class ClientsStartupExtensions
{
    public static void AddClients(this IServiceCollection services, bool isProduction)
    {
        services.AddHttpClient(nameof(DataSources.FrcEvents), client =>
        {
            // This timeout is absurdly long, but sometimes the FRC API is just. that. slow. (mostly on inactive events)
            client.Timeout = TimeSpan.FromSeconds(isProduction ? 30 : 60);
        });
        services.AddHttpClient(nameof(DataSources.BlueAlliance));
        services.AddHttpClient(nameof(DataSources.FtcEvents));
        services.AddKeyedScoped<IDataClient, FrcEventsDataClient>(DataSources.FrcEvents);
        services.AddKeyedScoped<IDataClient, BlueAllianceDataClient>(DataSources.BlueAlliance);
        services.AddKeyedScoped<IDataClient, FtcEventsDataClient>(DataSources.FtcEvents);
        services.AddScoped<BlueAllianceWriteClient>();
        services.AddScoped<OrangeAllianceDataClient>();
    }

    public static IHealthChecksBuilder AddClientHealthChecks(this IHealthChecksBuilder builder)
    {
        foreach (var source in Enum.GetValues<DataSources>())
        {
            builder.AddTypeActivatedCheck<ClientHealthCheck>(source.ToString(), HealthStatus.Degraded,
                ["DataClient"], source);
        }

        return builder;
    }
}