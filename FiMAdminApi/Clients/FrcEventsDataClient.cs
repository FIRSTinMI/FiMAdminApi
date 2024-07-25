using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using FiMAdminApi.Data.Models;
using FiMAdminApi.Extensions;
using Event = FiMAdminApi.Clients.Models.Event;

namespace FiMAdminApi.Clients;

public class FrcEventsDataClient : IDataClient
{
    private readonly string _apiKey;
    private readonly Uri _baseUrl;
    private readonly HttpClient _httpClient;

    public FrcEventsDataClient(IServiceProvider sp)
    {
        var configSection = sp.GetRequiredService<IConfiguration>().GetRequiredSection("Clients:FrcEvents");
        
        var apiKey = configSection["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ApplicationException("FrcEvents ApiKey was null but is required");
        _apiKey = apiKey;

        var baseUrl = configSection["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ApplicationException("FrcEvents BaseUrl was null but is required");
        _baseUrl = new Uri(baseUrl);
        
        _httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("FrcEvents");
    }

    public async Task<Event?> GetEventAsync(Season season, string eventCode)
    {
        return (await GetAndParseEvents(season, eventCode: eventCode)).SingleOrDefault((Event?)null);
    }

    public async Task<List<Event>> GetDistrictEventsAsync(Season season, string districtCode)
    {
        return (await GetAndParseEvents(season, districtCode: districtCode)).ToList();
    }

    private async Task<IEnumerable<Event>> GetAndParseEvents(Season season, string? eventCode = null, string? districtCode = null)
    {
        var queryParams = new Dictionary<string, string>();
        if (eventCode is not null) queryParams.Add("eventCode", eventCode);
        if (districtCode is not null) queryParams.Add("districtCode", districtCode);
        var resp = await _httpClient.SendAsync(BuildGetRequest($"{GetSeason(season)}/events", queryParams));
        resp.EnsureSuccessStatusCode();

        var json = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("Events").EnumerateArray().Select(evt =>
        {
            var timeZone = NormalizeTimeZone(evt.GetProperty("timezone").GetString());
            var startTime = evt.GetProperty("dateStart").GetDateTime();
            var endTime = evt.GetProperty("dateEnd").GetDateTime();

            return new Event
            {
                EventCode = evt.GetProperty("code").GetString() ?? "(No event code)",
                Name = evt.GetProperty("name").GetString() ?? "(No name)",
                DistrictCode = evt.GetProperty("districtCode").GetString(),
                City = evt.GetProperty("city").GetString() ?? "(No city)",
                StartTime = new DateTimeOffset(startTime, timeZone.GetUtcOffset(startTime)),
                EndTime = new DateTimeOffset(endTime, timeZone.GetUtcOffset(endTime)),
                TimeZone = timeZone
            };
        });
    }

    private static string GetSeason(Season season)
    {
        return season.StartTime.Year.ToString();
    }

    private static TimeZoneInfo NormalizeTimeZone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return TimeZoneInfo.Utc;
        }
        
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(input, out var ianaId))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        
        if (TimeZoneInfo.TryFindSystemTimeZoneById(input, out var rawTz))
        {
            return rawTz;
        }
        
        return TimeZoneInfo.Utc;
    }

    /// <summary>
    /// Creates a request which encodes all user-provided values
    /// </summary>
    private HttpRequestMessage BuildGetRequest(FormattableString endpoint, Dictionary<string, string>? queryParams = default)
    {
        var request = new HttpRequestMessage();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(_apiKey)));

        var relativeUri =
            $"{endpoint.EncodeString(Uri.EscapeDataString)}{(queryParams is not null && queryParams.Count > 0 ? QueryString.Create(queryParams!) : "")}";

        if (relativeUri.StartsWith('/'))
            throw new ArgumentException("Endpoint must be a relative path", nameof(endpoint));
        
        request.RequestUri = new Uri(_baseUrl, relativeUri);

        return request;
    }
}