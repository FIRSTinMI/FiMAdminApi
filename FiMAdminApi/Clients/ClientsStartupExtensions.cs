namespace FiMAdminApi.Clients;

public static class ClientsStartupExtensions
{
    public static void AddClients(this IServiceCollection services)
    {
        services.AddHttpClient("FrcEvents");
        services.AddKeyedScoped<IDataClient, FrcEventsDataClient>("FrcEvents");
    }
}