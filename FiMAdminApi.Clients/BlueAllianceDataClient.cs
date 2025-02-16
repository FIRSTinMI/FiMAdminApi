using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json;
using FiMAdminApi.Clients.Extensions;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Clients.PlayoffTiebreaks;
using FiMAdminApi.Models.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Alliance = FiMAdminApi.Clients.Models.Alliance;
using Event = FiMAdminApi.Clients.Models.Event;

namespace FiMAdminApi.Clients;

public class BlueAllianceDataClient : RestClient, IDataClient
{
    private readonly string _apiKey;
    private readonly Uri _baseUrl;

    public BlueAllianceDataClient(IServiceProvider sp)
        : base(
            sp.GetRequiredService<ILogger<BlueAllianceDataClient>>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("BlueAlliance"))
    {
        var configSection = sp.GetRequiredService<IConfiguration>().GetRequiredSection("Clients:BlueAlliance");
        
        var apiKey = configSection["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ApplicationException("BlueAlliance ApiKey was null but is required");
        _apiKey = apiKey;

        var baseUrl = configSection["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ApplicationException("BlueAlliance BaseUrl was null but is required");
        _baseUrl = new Uri(baseUrl);
    }

    public async Task<Event?> GetEventAsync(Season season, string eventCode)
    {
        var response = await PerformRequest(BuildGetRequest($"event/{GetEventCode(season, eventCode)}"));
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Logger.LogInformation(json.RootElement.GetRawText());

        return ParseEvent(json.RootElement);
    }

    public async Task<List<Event>> GetDistrictEventsAsync(Season season, string districtCode)
    {
        var response =
            await PerformRequest(BuildGetRequest($"district/{GetDistrictKey(season, districtCode)}/events"));
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        return json.RootElement.EnumerateArray().Select(ParseEvent).ToList();
    }

    public Task<List<Team>> GetTeamsByNumbers(Season season, IEnumerable<string> teamNumbers)
    {
        throw new NotImplementedException();
    }

    public Task<List<Team>> GetTeamsForEvent(Season season, string eventCode)
    {
        throw new NotImplementedException();
    }

    public Task<List<ScheduledMatch>> GetQualScheduleForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        throw new NotImplementedException();
    }

    public Task<List<MatchResult>> GetQualResultsForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        throw new NotImplementedException();
    }

    public Task<List<QualRanking>> GetQualRankingsForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        throw new NotImplementedException();
    }

    public Task<List<Alliance>> GetAlliancesForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        throw new NotImplementedException();
    }

    public Task<List<PlayoffMatch>> GetPlayoffResultsForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        throw new NotImplementedException();
    }

    public IPlayoffTiebreak GetPlayoffTiebreak(FiMAdminApi.Models.Models.Event evt)
    {
        throw new NotImplementedException();
    }

    public async Task<string?> CheckHealth()
    {
        var resp = await PerformRequest(BuildGetRequest($"status"));

        if (resp.IsSuccessStatusCode) return null;

        return await resp.Content.ReadAsStringAsync();
    }

    private static string GetDistrictKey(Season season, string districtName)
    {
        return char.IsDigit(districtName[0]) ? districtName : $"{season.StartTime.Year}{districtName}";
    }

    private static string GetEventCode(Season season, string eventCode)
    {
        return char.IsDigit(eventCode[0]) ? eventCode : $"{season.StartTime.Year}{eventCode}";
    }

    private static Event ParseEvent(JsonElement evt)
    {
        var timeZone =
            TimeZoneInfo.FindSystemTimeZoneById(evt.GetProperty("timezone").GetString() ?? TimeZoneInfo.Utc.Id);
        var startTime = DateStringToDate(evt.GetProperty("start_date").GetString());
        var endTime = DateStringToDate(evt.GetProperty("end_date").GetString());
        var district = evt.GetProperty("district");

        return new Event
        {
            EventCode = evt.GetProperty("key").GetString() ?? "(No event code)",
            Name = evt.GetProperty("name").GetString() ?? "(No name)",
            DistrictCode = district.ValueKind == JsonValueKind.Object ? district.GetProperty("key").GetString() : null,
            City = evt.GetProperty("city").GetString() ?? "(No city)",
            StartTime = new DateTimeOffset(startTime, timeZone.GetUtcOffset(startTime)),
            EndTime = new DateTimeOffset(endTime, timeZone.GetUtcOffset(endTime)),
            TimeZone = timeZone
        };
    }

    private static DateTime DateStringToDate(string? input, bool isEndOfDay = false)
    {
        if (!DateTime.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new ApplicationException($"Got a bad date: `{input}`");
        }
        
        if (isEndOfDay)
        {
            date = date.AddDays(1).AddMinutes(-1);
        }

        return date;
    }

    /// <summary>
    /// Creates a request which encodes all user-provided values
    /// </summary>
    private HttpRequestMessage BuildGetRequest(FormattableString endpoint, Dictionary<string, string>? queryParams = default)
    {
        Logger.LogWarning("TBA is not fully supported at this time. Some data may not sync properly.");
        
        var request = new HttpRequestMessage();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        request.Headers.Add("X-TBA-Auth-Key", _apiKey);

        var relativeUri =
            $"{endpoint.EncodeString(Uri.EscapeDataString)}{(queryParams is not null && queryParams.Count > 0 ? QueryString.Create(queryParams) : "")}";

        if (relativeUri.StartsWith('/'))
            throw new ArgumentException("Endpoint must be a relative path", nameof(endpoint));
        
        request.RequestUri = new Uri(_baseUrl, relativeUri);

        return request;
    }
}