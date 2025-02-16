using FiMAdminApi.Models.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FiMAdminApi.Clients;

public class ClientHealthCheck(IServiceProvider services, DataSources source) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = new())
    {
        var dataClient = services.GetKeyedService<IDataClient>(source);
        if (dataClient is null) return HealthCheckResult.Degraded("Unable to create instance of client");
        
        var result = await dataClient.CheckHealth();
        return result == null ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded(result);
    }
}