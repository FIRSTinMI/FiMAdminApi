using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using FiMAdminApi.Clients.Exceptions;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Clients.PlayoffTiebreaks;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Data.Models;
using FiMAdminApi.Extensions;
using Alliance = FiMAdminApi.Clients.Models.Alliance;
using Event = FiMAdminApi.Clients.Models.Event;

namespace FiMAdminApi.Clients;

public class FrcEventsDataClient : RestClient, IDataClient
{
    private readonly string _apiKey;
    private readonly Uri _baseUrl;

    public FrcEventsDataClient(IServiceProvider sp)
        : base(
            sp.GetRequiredService<ILogger<FrcEventsDataClient>>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("FrcEvents"))
    {
        TrackLastModified = true;
        
        var configSection = sp.GetRequiredService<IConfiguration>().GetRequiredSection("Clients:FrcEvents");
        
        var apiKey = configSection["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ApplicationException("FrcEvents ApiKey was null but is required");
        _apiKey = apiKey;

        var baseUrl = configSection["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ApplicationException("FrcEvents BaseUrl was null but is required");
        _baseUrl = new Uri(baseUrl);
    }

    public async Task<Event?> GetEventAsync(Season season, string eventCode)
    {
        return (await GetAndParseEvents(season, eventCode: eventCode)).SingleOrDefault((Event?)null);
    }

    public async Task<List<Event>> GetDistrictEventsAsync(Season season, string districtCode)
    {
        return (await GetAndParseEvents(season, districtCode: districtCode)).ToList();
    }

    public async Task<List<Team>> GetTeamsForEvent(Season season, string eventCode)
    {
        var resp = await PerformRequest(BuildGetRequest($"{GetSeason(season)}/teams", new()
        {
            { "eventCode", eventCode }
        }));
        resp.EnsureSuccessStatusCode();

        var json = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("teams").EnumerateArray().Select(team => new Team
        {
            TeamNumber = team.GetProperty("teamNumber").GetInt32(),
            Nickname = team.GetProperty("nameShort").GetString() ?? throw new MissingDataException("Nickname"),
            FullName = team.GetProperty("nameFull").GetString() ?? throw new MissingDataException("FullName"),
            City = team.GetProperty("city").GetString() ?? throw new MissingDataException("City"),
            StateProvince = team.GetProperty("stateProv").GetString() ?? throw new MissingDataException("StateProvince"),
            Country = team.GetProperty("country").GetString() ?? throw new MissingDataException("Country")
        }).ToList();
    }

    public async Task<List<ScheduledMatch>> GetQualScheduleForEvent(Data.Models.Event evt)
    {
        var eventTz = TimeZoneInfo.FindSystemTimeZoneById(evt.TimeZone);
        
        var resp = await PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/schedule/{evt.Code}", new()
            {
                { "tournamentLevel", "Qualification" }
            }));
        resp.EnsureSuccessStatusCode();

        var json = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("Schedule").EnumerateArray().Select(match =>
        {
            var (red, blue) = ApiTeamsToFimTeams(match.GetProperty("teams"));
            var utcScheduledStart =
                TimeZoneInfo.ConvertTimeToUtc(match.GetProperty("startTime").GetDateTime(), eventTz);
            return new ScheduledMatch
            {
                MatchNumber = match.GetProperty("matchNumber").GetInt32(),
                RedAllianceTeams = red,
                BlueAllianceTeams = blue,
                ScheduledStartTime = utcScheduledStart
            };
        }).ToList();
    }

    public async Task<List<MatchResult>> GetQualResultsForEvent(Data.Models.Event evt)
    {
        var eventTz = TimeZoneInfo.FindSystemTimeZoneById(evt.TimeZone);
        
        var resp = await PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/matches/{evt.Code}", new()
            {
                { "tournamentLevel", "Qualification" }
            }));
        resp.EnsureSuccessStatusCode();

        var json = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("Matches").EnumerateArray().Select(match =>
        {
            var utcActualStart =
                TimeZoneInfo.ConvertTimeToUtc(match.GetProperty("actualStartTime").GetDateTime(), eventTz);
            var utcPostResult =
                TimeZoneInfo.ConvertTimeToUtc(match.GetProperty("postResultTime").GetDateTime(), eventTz);
            return new MatchResult
            {
                MatchNumber = match.GetProperty("matchNumber").GetInt32(),
                ActualStartTime = utcActualStart,
                PostResultTime = utcPostResult,
            };
        }).ToList();
    }

    public async Task<List<QualRanking>> GetQualRankingsForEvent(Data.Models.Event evt)
    {
        Debug.Assert(evt.Season is not null, "evt.Season is null, ensure it's included in DB fetches");

        var resp = await PerformRequest(BuildGetRequest($"{GetSeason(evt.Season)}/rankings/{evt.Code}"));
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();

        return json.GetProperty("Rankings").EnumerateArray().Select(r => new QualRanking
        {
            Rank = r.GetProperty("rank").GetInt32(),
            TeamNumber = r.GetProperty("teamNumber").GetInt32(),
            SortOrder1 = r.GetProperty("sortOrder1").GetDouble(),
            SortOrder2 = r.GetProperty("sortOrder2").GetDouble(),
            SortOrder3 = r.GetProperty("sortOrder3").GetDouble(),
            SortOrder4 = r.GetProperty("sortOrder4").GetDouble(),
            SortOrder5 = r.GetProperty("sortOrder5").GetDouble(),
            SortOrder6 = r.GetProperty("sortOrder6").GetDouble(),
            Wins = r.GetProperty("wins").GetInt32(),
            Ties = r.GetProperty("ties").GetInt32(),
            Losses = r.GetProperty("losses").GetInt32(),
            QualAverage = r.GetProperty("qualAverage").GetDouble(),
            Disqualifications = r.GetProperty("dq").GetInt32(),
            MatchesPlayed = r.GetProperty("matchesPlayed").GetInt32()
        }).ToList();
    }

    public async Task<List<Alliance>> GetAlliancesForEvent(Data.Models.Event evt)
    {
        var resp = await PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/alliances/{evt.Code}"));
        resp.EnsureSuccessStatusCode();
        
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("Alliances").EnumerateArray().Select(a => new Alliance
        {
            Name = a.GetProperty("name").GetString() ?? throw new MissingDataException("name"),
            TeamNumbers = new List<int?> {
                GetNullableInt(a.GetProperty("captain")),
                GetNullableInt(a.GetProperty("round1")),
                GetNullableInt(a.GetProperty("round2")),
                GetNullableInt(a.GetProperty("round3")),
                GetNullableInt(a.GetProperty("backup"))
                // unclear what the "backupReplaced" property does or its type, leaving it out for now
            }.Where(t => t is not null).Select(t => t!.Value).ToList()
        }).ToList();
    }

    public async Task<List<PlayoffMatch>> GetPlayoffResultsForEvent(Data.Models.Event evt)
    {
        //var tiebreakResolver = new FrcEvents2025Tiebreak(evt);
        var eventTz = TimeZoneInfo.FindSystemTimeZoneById(evt.TimeZone);
        
        var resultsTask = PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/matches/{evt.Code}", new()
            {
                { "tournamentLevel", "Playoff" }
            }));
        //{
        // "Matches" : [ {
        //   "isReplay" : false,
        //   "matchVideoLink" : "https://www.youtube.com/watch?v=ehnqux_ddbU",
        //   "description" : "Match 1 (R1)",
        //   "matchNumber" : 1,
        //   "scoreRedFinal" : 101,
        //   "scoreRedFoul" : 25,
        //   "scoreRedAuto" : 29,
        //   "scoreBlueFinal" : 16,
        //   "scoreBlueFoul" : 2,
        //   "scoreBlueAuto" : 7,
        //   "autoStartTime" : "2024-03-09T14:17:45.127",
        //   "actualStartTime" : "2024-03-09T14:17:45.127",
        //   "tournamentLevel" : "Playoff",
        //   "postResultTime" : "2024-03-09T14:22:13.153",
        //   "teams" : [ {
        //     "teamNumber" : 2054,
        //     "station" : "Red1",
        //     "dq" : false
        //   }, {
        //     "teamNumber" : 5610,
        //     "station" : "Red2",
        //     "dq" : false
        //   }, {
        //     "teamNumber" : 2767,
        //     "station" : "Red3",
        //     "dq" : false
        //   }, {
        //     "teamNumber" : 9228,
        //     "station" : "Blue1",
        //     "dq" : false
        //   }, {
        //     "teamNumber" : 4776,
        //     "station" : "Blue2",
        //     "dq" : false
        //   }, {
        //     "teamNumber" : 4325,
        //     "station" : "Blue3",
        //     "dq" : false
        //   } ]
        // }
        
        var scheduleTask = PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/schedule/{evt.Code}", new()
            {
                { "tournamentLevel", "Playoff" }
            }));
        
        // {
        // "Schedule" : [ {
        //   "description" : "Match 1 (R1)",
        //   "startTime" : "2024-03-09T14:00:00",
        //   "matchNumber" : 1,
        //   "field" : "Primary",
        //   "tournamentLevel" : "Playoff",
        //   "teams" : [ {
        //     "teamNumber" : 2054,
        //     "station" : "Red1",
        //     "surrogate" : false
        //   }, {
        //     "teamNumber" : 5610,
        //     "station" : "Red2",
        //     "surrogate" : false
        //   }, {
        //     "teamNumber" : 2767,
        //     "station" : "Red3",
        //     "surrogate" : false
        //   }, {
        //     "teamNumber" : 9228,
        //     "station" : "Blue1",
        //     "surrogate" : false
        //   }, {
        //     "teamNumber" : 4776,
        //     "station" : "Blue2",
        //     "surrogate" : false
        //   }, {
        //     "teamNumber" : 4325,
        //     "station" : "Blue3",
        //     "surrogate" : false
        //   } ]
        // } ]
        // }

        var scheduleResp = await scheduleTask;
        scheduleResp.EnsureSuccessStatusCode();
        var scheduleJson = await scheduleResp.Content.ReadFromJsonAsync<JsonElement>();
        var schedule = scheduleJson.GetProperty("Schedule").EnumerateArray().Select(s => new
        {
            MatchNumber = s.GetProperty("matchNumber").GetInt32(),
            ScheduledStartTime = GetNullableDateTime(s.GetProperty("startTime"))
        }).ToList();

        var results = await resultsTask;
        results.EnsureSuccessStatusCode();
        var resultsJson = await results.Content.ReadFromJsonAsync<JsonElement>();
        
        return resultsJson.GetProperty("Matches").EnumerateArray().Select(match =>
        {
            var matchNumber = match.GetProperty("matchNumber").GetInt32();
            var schMatch = schedule.FirstOrDefault(s => s.MatchNumber == matchNumber);
            var utcScheduledStart = schMatch?.ScheduledStartTime is not null
                ? TimeZoneInfo.ConvertTimeToUtc(schMatch.ScheduledStartTime.Value, eventTz)
                : (DateTime?)null;
            var actualStart = GetNullableDateTime(match.GetProperty("actualStartTime"));
            var utcActualStart =
                actualStart is not null ? TimeZoneInfo.ConvertTimeToUtc(actualStart.Value, eventTz) : (DateTime?)null;
            var postResult = GetNullableDateTime(match.GetProperty("postResultTime"));
            var utcPostResult =
                postResult is not null ? TimeZoneInfo.ConvertTimeToUtc(postResult.Value, eventTz) : (DateTime?)null;
            var (redTeams, blueTeams) = ApiTeamsToFimTeams(match.GetProperty("teams"));
            var redScore = GetNullableInt(match.GetProperty("scoreRedFinal"));
            var blueScore = GetNullableInt(match.GetProperty("scoreRedFinal"));
            var winner = (MatchWinner?)null;
            if (utcPostResult is not null && redScore is not null && blueScore is not null)
            {
                if (redScore > blueScore) winner = MatchWinner.Red;
                if (blueScore > redScore) winner = MatchWinner.Blue;
            }
            return new PlayoffMatch
            {
                MatchNumber = matchNumber,
                MatchName = match.GetProperty("description").GetString(),
                ScheduledStartTime = utcScheduledStart,
                ActualStartTime = utcActualStart,
                PostResultTime = utcPostResult,
                RedAllianceTeams = redTeams,
                BlueAllianceTeams = blueTeams,
                Winner = winner
            };
        }).ToList();
    }

    public IPlayoffTiebreak GetPlayoffTiebreak(Data.Models.Event evt)
    {
        return new FrcEvents2025Tiebreak(this, evt);
    }

    public async Task<string?> CheckHealth()
    {
        var resp = await PerformRequest(BuildGetRequest($""));
        
        if (resp.IsSuccessStatusCode) return null;

        return await resp.Content.ReadAsStringAsync();
    }

    public async Task<JsonElement> GetPlayoffScoreDetails(Data.Models.Event evt)
    {
        var scoreResp = await PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/scores/{evt.Code}/playoff"));
        scoreResp.EnsureSuccessStatusCode();
        return await scoreResp.Content.ReadFromJsonAsync<JsonElement>();
    }
    
    public static string GetSeason(Season season)
    {
        return season.StartTime.Year.ToString();
    }
    
    private static int? GetNullableInt(JsonElement el)
    {
        return el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;
    }
    
    private static DateTime? GetNullableDateTime(JsonElement el)
    {
        return el.ValueKind == JsonValueKind.String ? el.GetDateTime() : null;
    }

    private async Task<IEnumerable<Event>> GetAndParseEvents(Season season, string? eventCode = null, string? districtCode = null)
    {
        var queryParams = new Dictionary<string, string>();
        if (eventCode is not null) queryParams.Add("eventCode", eventCode);
        if (districtCode is not null) queryParams.Add("districtCode", districtCode);
        var resp = await PerformRequest(BuildGetRequest($"{GetSeason(season)}/events", queryParams));
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

    private static (int[] redAllianceTeams, int[] blueAllianceTeams) ApiTeamsToFimTeams(JsonElement jsonTeams)
    {
        var apiDict = jsonTeams.EnumerateArray().ToDictionary(t => t.GetProperty("station").GetString()!,
            t => t.GetProperty("teamNumber").GetInt32());
        var red = new[] { "Red1", "Red2", "Red3" }.Select(s => apiDict[s]).ToArray();
        var blue = new[] { "Blue1", "Blue2", "Blue3" }.Select(s => apiDict[s]).ToArray();

        return (red, blue);
    }

    /// <summary>
    /// Creates a request which encodes all user-provided values
    /// </summary>
    private HttpRequestMessage BuildGetRequest(FormattableString endpoint, Dictionary<string, string>? queryParams = null)
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