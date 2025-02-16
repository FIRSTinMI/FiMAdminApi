using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FiMAdminApi.Clients;

public abstract class RestClient(ILogger<RestClient> logger, HttpClient httpClient)
{
    protected readonly ILogger<RestClient> Logger = logger;
    protected bool TrackLastModified { get; init; } = false; 
    
    protected Task<HttpResponseMessage> PerformRequest(HttpRequestMessage request)
    {
        return PerformRequest(request, CancellationToken.None);
    }

    private async Task<HttpResponseMessage> PerformRequest(HttpRequestMessage request, CancellationToken ct)
    {
        var timer = new Stopwatch();
        timer.Start();
        var response = await httpClient.SendAsync(request, ct);
        timer.Stop();

        Logger.LogInformation("Request: url({url}) elapsed({ms}ms) status({status})", request.RequestUri,
            timer.ElapsedMilliseconds, (int)response.StatusCode);

        // if (TrackLastModified && response.Headers.TryGetValues("Last-Modified", out var modifiedValues))
        // {
        //     // todo
        // }

        return response;
    }
}