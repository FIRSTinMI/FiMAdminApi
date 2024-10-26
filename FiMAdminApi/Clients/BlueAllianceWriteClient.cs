using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Data.Models;
using FiMAdminApi.Extensions;

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

    private HttpRequestMessage BuildRequest(FormattableString endpoint, object body)
    {
        var serializedBody = JsonSerializer.Serialize(body, JsonSerializerOptions);
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
        return char.IsDigit(eventCode[0]) ? eventCode : $"{season.StartTime.Year}{eventCode}";
    }

    private record EventInfoRequest(EventInfoWebcastInfo[] Webcasts);

    private record EventInfoWebcastInfo(string Url, string? Date);
}