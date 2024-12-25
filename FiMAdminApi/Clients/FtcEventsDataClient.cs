using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Data.Models;
using FiMAdminApi.Extensions;
using Event = FiMAdminApi.Clients.Models.Event;

namespace FiMAdminApi.Clients;

public class FtcEventsDataClient : RestClient, IDataClient
{
    private readonly string _apiKey;
    private readonly Uri _baseUrl;
    
    public FtcEventsDataClient(IServiceProvider sp)
        : base(
            sp.GetRequiredService<ILogger<FtcEventsDataClient>>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("FrcEvents"))
    {
        TrackLastModified = true;
        
        var configSection = sp.GetRequiredService<IConfiguration>().GetRequiredSection("Clients:FtcEvents");
        
        var apiKey = configSection["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ApplicationException("FrcEvents ApiKey was null but is required");
        _apiKey = apiKey;

        var baseUrl = configSection["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ApplicationException("FrcEvents BaseUrl was null but is required");
        _baseUrl = new Uri(baseUrl);
    }
    
    public Task<Event?> GetEventAsync(Season season, string eventCode)
    {
        throw new NotImplementedException();
    }

    public async Task<List<Event>> GetDistrictEventsAsync(Season season, string districtCode)
    {
        var response = await PerformRequest(BuildGetRequest($"{GetSeason(season)}/events"));
        response.EnsureSuccessStatusCode();
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        var events = json.RootElement.GetProperty("events").EnumerateArray()
            .Where(e => e.GetProperty("regionCode").ValueEquals(districtCode));

        return events.Select(e => new Event
        {
            EventCode = e.GetProperty("code").GetString() ?? throw new ArgumentException("Code missing from event"),
            Name = e.GetProperty("name").GetString() ?? throw new ArgumentException("Name missing from event"),
            DistrictCode = e.GetProperty("regionCode").GetString(),
            City = e.GetProperty("city").GetString() ?? "(No city)",
            StartTime = e.GetProperty("dateStart").GetDateTimeOffset().UtcDateTime,
            EndTime = e.GetProperty("dateEnd").GetDateTimeOffset().UtcDateTime,
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(e.GetProperty("timezone").GetString() ?? throw new ArgumentException("No time zone for event"))
        }).ToList();
    }

    public Task<List<Team>> GetTeamsForEvent(Season season, string eventCode)
    {
        throw new NotImplementedException();
    }

    public Task<List<ScheduledMatch>> GetQualScheduleForEvent(Data.Models.Event evt)
    {
        throw new NotImplementedException();
    }

    public Task<List<MatchResult>> GetQualResultsForEvent(Data.Models.Event evt)
    {
        throw new NotImplementedException();
    }

    public Task<List<QualRanking>> GetQualRankingsForEvent(Data.Models.Event evt)
    {
        throw new NotImplementedException();
    }
    
    public async Task<string?> CheckHealth()
    {
        var resp = await PerformRequest(BuildGetRequest($""));
        
        if (resp.IsSuccessStatusCode) return null;

        return await resp.Content.ReadAsStringAsync();
    }

    private static string GetSeason(Season season)
    {
        return season.StartTime.Year.ToString();
    }
    
    /// <summary>
    /// Creates a request which encodes all user-provided values
    /// </summary>
    private HttpRequestMessage BuildGetRequest(FormattableString endpoint, Dictionary<string, string>? queryParams = default)
    {
        var request = new HttpRequestMessage();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(_apiKey.ToLower())));

        var relativeUri =
            $"{endpoint.EncodeString(Uri.EscapeDataString)}{(queryParams is not null && queryParams.Count > 0 ? QueryString.Create(queryParams!) : "")}";

        if (relativeUri.StartsWith('/'))
            throw new ArgumentException("Endpoint must be a relative path", nameof(endpoint));
        
        request.RequestUri = new Uri(_baseUrl, relativeUri);

        return request;
    }
}