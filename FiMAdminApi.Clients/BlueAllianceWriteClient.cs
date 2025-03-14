using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FiMAdminApi.Clients.Extensions;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FiMAdminApi.Clients;

public class BlueAllianceWriteClient : RestClient
{
    private readonly string _authId;
    private readonly string _authSecret;
    private readonly Uri _baseUrl;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public BlueAllianceWriteClient(IServiceProvider sp)
        : base(
            sp.GetRequiredService<ILogger<BlueAllianceWriteClient>>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(DataSources.BlueAlliance.ToString()))
    {
        var configSection = sp.GetRequiredService<IConfiguration>().GetRequiredSection("Clients:BlueAllianceWrite");
        
        var authId = configSection["AuthId"];
        if (string.IsNullOrWhiteSpace(authId))
            throw new ApplicationException("BlueAllianceWrite ApiKey was null but is required");
        _authId = authId;
        
        var authSecret = configSection["AuthSecret"];
        if (string.IsNullOrWhiteSpace(authSecret))
            throw new ApplicationException("BlueAllianceWrite AuthSecret was null but is required");
        _authSecret = authSecret;

        var baseUrl = configSection["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ApplicationException("BlueAllianceWrite BaseUrl was null but is required");
        _baseUrl = new Uri(baseUrl);
        
        _httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("BlueAlliance");
    }

    public async Task UpdateEventInfo(Season season, string eventCode, WebcastInfo[] webcastInfo)
    {
        var request = BuildRequest($"event/{GetEventCode(season, eventCode)}/info/update",
            new EventInfoRequest(webcastInfo
                .Select(w => new EventInfoWebcastInfo(w.Url, w.Date is not null ? $"{w.Date:O}" : null)).ToArray()));

        var response = await PerformRequest(request);
    }

    public async Task AddMatchVideos(Season season, string eventCode, Dictionary<string, string> videos)
    {
        var request = BuildRequest($"event/{GetEventCode(season, eventCode)}/match_videos/add", videos);

        var response = await PerformRequest(request);
        Logger.LogInformation("{}", await response.Content.ReadAsStringAsync());

        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage BuildRequest<TBody>(FormattableString endpoint, TBody body)
    {
        var jsonTypeInfo = BlueAllianceWriteJsonSerializer.Default.GetTypeInfo(typeof(TBody));
        if (jsonTypeInfo is null)
            throw new ApplicationException("Unsupported type to serialize body to BlueAllianceWriteClient");
        
        var serializedBody = JsonSerializer.Serialize(body, (JsonTypeInfo<TBody>) jsonTypeInfo);
        var relativeUri = endpoint.EncodeString(Uri.EscapeDataString);

        var request = new HttpRequestMessage();
        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(_baseUrl, relativeUri);
        request.Headers.Add("X-Tba-Auth-Id", _authId);
        request.Content = new StringContent(serializedBody, new MediaTypeHeaderValue("application/json"));

        var signature =
            MD5.HashData(Encoding.UTF8.GetBytes(_authSecret + request.RequestUri.AbsolutePath + serializedBody));
        request.Headers.Add("X-Tba-Auth-Sig", Convert.ToHexStringLower(signature));

        return request;
    }
    
    private static string GetEventCode(Season season, string eventCode)
    {
        return char.IsDigit(eventCode[0]) ? eventCode : $"{season.StartTime.Year}{eventCode}".ToLower();
    }
}

internal record EventInfoRequest(EventInfoWebcastInfo[] Webcasts);

internal record EventInfoWebcastInfo(string Url, string? Date);

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EventInfoRequest))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class BlueAllianceWriteJsonSerializer : JsonSerializerContext
{
}