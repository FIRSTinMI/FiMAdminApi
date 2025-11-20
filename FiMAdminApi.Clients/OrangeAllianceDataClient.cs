using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json;
using FiMAdminApi.Clients.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;
using System.Globalization;

namespace FiMAdminApi.Clients;

internal record EventLiveStream(
    string stream_key,
    string event_key,
    string channel_name,
    string stream_name,
    int stream_type,
    bool is_active,
    string url,
    string start_datetime,
    string end_datetime,
    string channel_url
);

public class OrangeAllianceDataClient : RestClient
{
    private readonly string _apiKey;
    private readonly Uri _baseUrl;

    public OrangeAllianceDataClient(IServiceProvider sp)
        : base(
            sp.GetRequiredService<ILogger<OrangeAllianceDataClient>>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("OrangeAlliance"))
    {
        var configSection = sp.GetRequiredService<IConfiguration>().GetRequiredSection("Clients:OrangeAlliance");
        
        var apiKey = configSection["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ApplicationException("OrangeAlliance ApiKey was null but is required");
        _apiKey = apiKey;

        var baseUrl = configSection["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ApplicationException("OrangeAlliance BaseUrl was null but is required");
        _baseUrl = new Uri(baseUrl);
    }

    public async Task<string?> GetEventKeyFromFTCEventsKey(string ftcEventsKey)
    {
        var response = await PerformRequest(BuildGetRequest($"search?q={Uri.EscapeDataString(ftcEventsKey)}"));
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        // response is array of elements containing first_event_code property, find the first one
        // where the first_event_code matches the provided ftcEventsKey and return the event_key property
        var firstEvent = json.RootElement.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("first_event_code").GetString() == ftcEventsKey);

        if (firstEvent.ValueKind == JsonValueKind.Undefined)
            return null;

        return firstEvent.GetProperty("event_key").GetString();
    }

    public async Task<bool> UpdateEventStream(
        string eventKey,
        string channelName,
        string streamName,
        string provider,
        string streamLink,
        string channelLink,
        DateTime startDate,
        DateTime endDate,
        string streamSuffix = "LS1")
    {
        if (string.IsNullOrWhiteSpace(eventKey)) throw new ArgumentNullException(nameof(eventKey));

        var streamKey = eventKey + "-" + streamSuffix;

        var streamType = provider switch
        {
            "twitch" => 1,
            "youtube" => 0,
            _ => -1
        };

        // Build start/end as ISO8601 UTC with milliseconds (three digits) e.g. 2025-05-16T08:00:00.000Z
        string start = startDate.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        string end = endDate.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        var stream = new EventLiveStream(
            stream_key: streamKey,
            event_key: eventKey,
            channel_name: channelName ?? string.Empty,
            stream_name: streamName ?? string.Empty,
            stream_type: streamType,
            is_active: true,
            url: streamLink ?? string.Empty,
            start_datetime: start,
            end_datetime: end,
            channel_url: channelLink ?? string.Empty
        );

        // endpoint path: use a sensible create path for event streams
        var request = BuildRequest<EventLiveStream[]>($"event/{eventKey}/streams", [stream]);
        var response = await PerformRequest(request);
        var content = await response.Content.ReadAsStringAsync();
        Logger.LogInformation("UpdateEventStream response: {Status} {Body}", (int)response.StatusCode, content);
        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Error updating event stream for event {EventKey}. HTTP {StatusCode}. Response body: {Body}", eventKey, (int)response.StatusCode, content);
            return false;
        }
        return true;
    }

    public async Task<bool> DeleteEventStream(string liveStreamKey)
    {
        if (string.IsNullOrWhiteSpace(liveStreamKey)) throw new ArgumentNullException(nameof(liveStreamKey));

        // endpoint path: use a sensible create path for event streams
        var request = BuildDelete($"/streams/{liveStreamKey}");
        var response = await PerformRequest(request);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogError("Error deleting event stream {LiveStreamKey}. HTTP {StatusCode}. Response body: {Body}", liveStreamKey, (int)response.StatusCode, content);
            return false;
        }
        return true;
    }

    public async Task<TOAStream[]> GetEventStreams(string eventKey)
    {
        if (string.IsNullOrWhiteSpace(eventKey)) throw new ArgumentNullException(nameof(eventKey));

        var request = BuildGetRequest($"event/{eventKey}/streams");
        var response = await PerformRequest(request);
        var content = await response.Content.ReadAsStringAsync();
        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Error getting event streams for event {EventKey}. HTTP {StatusCode}. Response body: {Body}", eventKey, (int)response.StatusCode, content);
            return Array.Empty<TOAStream>();
        }

        var jsonTypeInfo = OrangeAllianceJsonSerializer.Default.GetTypeInfo(typeof(TOAStream[]));
        if (jsonTypeInfo is null)
            throw new ApplicationException("Unsupported type to deserialize OrangeAlliance TOAStream[]");
        return JsonSerializer.Deserialize<TOAStream[]>(content, (JsonTypeInfo<TOAStream[]>)jsonTypeInfo)!;
    }

    /// <summary>
    /// Creates a request which encodes all user-provided values
    /// </summary>
    private HttpRequestMessage BuildGetRequest(FormattableString endpoint, Dictionary<string, string>? queryParams = default)
    {        
        var request = new HttpRequestMessage();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        request.Headers.Add("X-TOA-Key", _apiKey);
        request.Headers.Add("x-application-origin", "FiMAdminApi");

        var relativeUri =
            $"{endpoint.EncodeString(Uri.EscapeDataString)}{(queryParams is not null && queryParams.Count > 0 ? QueryString.Create(queryParams) : "")}";

        if (relativeUri.StartsWith('/'))
            throw new ArgumentException("Endpoint must be a relative path", nameof(endpoint));
        
        request.RequestUri = new Uri(_baseUrl, relativeUri);

        return request;
    }

    private HttpRequestMessage BuildRequest<TBody>(FormattableString endpoint, TBody body)
    {
        var jsonTypeInfo = OrangeAllianceJsonSerializer.Default.GetTypeInfo(typeof(TBody));
        if (jsonTypeInfo is null)
            throw new ApplicationException("Unsupported type to serialize body to OrangeAllianceDataClient");

        var serializedBody = JsonSerializer.Serialize(body, (JsonTypeInfo<TBody>) jsonTypeInfo);
        var relativeUri = endpoint.EncodeString(Uri.EscapeDataString);

        var request = new HttpRequestMessage();
        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(_baseUrl, relativeUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        request.Headers.Add("X-TOA-Key", _apiKey);
        request.Headers.Add("x-application-origin", "FiMAdminApi");
        request.Content = new StringContent(serializedBody, new MediaTypeHeaderValue("application/json"));

        return request;
    }

    private HttpRequestMessage BuildDelete(FormattableString endpoint)
    {
        var relativeUri = endpoint.EncodeString(Uri.EscapeDataString);

        var request = new HttpRequestMessage();
        request.Method = HttpMethod.Delete;
        request.RequestUri = new Uri(_baseUrl, relativeUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        request.Headers.Add("X-TOA-Key", _apiKey);
        request.Headers.Add("x-application-origin", "FiMAdminApi");

        return request;
    }
}

public class TOAStream
{
    public string stream_key { get; set; } = string.Empty;
    public string event_key { get; set; } = string.Empty;
    public string url { get; set; } = string.Empty;
}


[JsonSerializable(typeof(EventLiveStream[]))]
[JsonSerializable(typeof(TOAStream[]))]
internal partial class OrangeAllianceJsonSerializer : JsonSerializerContext
{
}