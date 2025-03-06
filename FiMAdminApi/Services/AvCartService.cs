using softaware.Authentication.Hmac.Client;

namespace FiMAdminApi.Services;

public class AvCartService(IHttpClientFactory httpClientFactory, ILogger<AvCartService> logger)
{
    public async Task StartStream(Guid equipmentId, int? streamNum)
    {
        logger.LogInformation("Starting {stream} for cart {equipmentId}",
            streamNum is null ? "all streams" : $"stream {streamNum}", equipmentId);
        var httpClient = httpClientFactory.CreateClient("AvCartHttpClient");

        var resp = await httpClient.PutAsync($"Assistant/StartStream/{equipmentId.ToString()}?streamNum={streamNum}",
            null);

        resp.EnsureSuccessStatusCode();
    }
    
    public async Task StopStream(Guid equipmentId, int? streamNum)
    {
        logger.LogInformation("Starting {stream} for cart {equipmentId}",
            streamNum is null ? "all streams" : $"stream {streamNum}", equipmentId);
        
        var httpClient = httpClientFactory.CreateClient("AvCartHttpClient");

        var resp = await httpClient.PutAsync($"Assistant/StopStream/{equipmentId.ToString()}?streamNum={streamNum}",
            null);

        resp.EnsureSuccessStatusCode();
    }
    
    public async Task PushStreamKeys(Guid equipmentId)
    {
        logger.LogInformation("Pushing stream keys to cart {equipmentId}", equipmentId);
        
        var httpClient = httpClientFactory.CreateClient("AvCartHttpClient");

        var resp = await httpClient.PutAsync($"Assistant/PushStreamKeys/{equipmentId.ToString()}", null);

        resp.EnsureSuccessStatusCode();
    }
}

public static class AvCartServiceExtensions
{
    public static IServiceCollection AddAvCartService(this IServiceCollection services)
    {
        services.AddTransient(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>()
                .GetRequiredSection("ServiceToService:FimQueueingAdmin");
            return new ApiKeyDelegatingHandler(config["AppId"], config["ApiKey"]);
        });
        services.AddHttpClient("AvCartHttpClient", (sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()
                                             .GetRequiredSection("ServiceToService:FimQueueingAdmin")["BaseUrl"] ??
                                         throw new ApplicationException(
                                             "Unable to set base URL for fim queueing admin"));
        }).AddHttpMessageHandler<ApiKeyDelegatingHandler>();
        services.AddScoped<AvCartService>();
        return services;
    }
}