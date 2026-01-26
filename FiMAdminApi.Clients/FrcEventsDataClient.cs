using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using FiMAdminApi.Clients.Exceptions;
using FiMAdminApi.Clients.Extensions;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Clients.Models.FrcEvents;
using FiMAdminApi.Clients.PlayoffTiebreaks;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Alliance = FiMAdminApi.Clients.Models.Alliance;
using Event = FiMAdminApi.Clients.Models.Event;

namespace FiMAdminApi.Clients;

public class FrcEventsDataClient : RestClient, IDataClient
{
    private readonly string _apiKey;
    private readonly Uri _baseUrl;
    private static readonly string[] RedTeamStations = ["Red1", "Red2", "Red3"];
    private static readonly string[] BlueTeamStations = ["Blue1", "Blue2", "Blue3"];

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
        var respJson = await resp.Content.ReadFromJsonAsync(FrcEventsJsonSerializerContext.Default.GetTeams) ??
                       throw new MissingDataException("Unable to parse FrcEvents teams response");
        return respJson.Teams.Select(t => new Team
        {
            TeamNumber = t.TeamNumber,
            Nickname = t.NameShort,
            FullName = t.NameFull,
            City = t.City,
            StateProvince = t.StateProv,
            Country = t.Country
        }).ToList();
    }

    public async Task<List<ScheduledMatch>> GetQualScheduleForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        var eventTz = TimeZoneInfo.FindSystemTimeZoneById(evt.TimeZone);
        
        var resp = await PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/schedule/{evt.Code}", new()
            {
                { "tournamentLevel", "Qualification" }
            }));
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync(FrcEventsJsonSerializerContext.Default.GetSchedule) ??
                   throw new MissingDataException("Unable to parse FrcEvents schedule response");

        return json.Schedule.Select(match =>
        {
            var (red, blue) = ApiTeamsToFimTeams(match.Teams);
            var utcScheduledStart = match.StartTime is not null
                ? TimeZoneInfo.ConvertTimeToUtc(match.StartTime.Value, eventTz)
                : throw new MissingDataException("Expected all scheduled qual matches to have a start time but didn't");
            return new ScheduledMatch
            {
                MatchNumber = match.MatchNumber,
                RedAllianceTeams = red,
                BlueAllianceTeams = blue,
                ScheduledStartTime = utcScheduledStart
            };
        }).ToList();
    }

    public async Task<List<MatchResult>> GetQualResultsForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        var eventTz = TimeZoneInfo.FindSystemTimeZoneById(evt.TimeZone);
        
        var resp = await PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/matches/{evt.Code}", new()
            {
                { "tournamentLevel", "Qualification" }
            }));
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync(FrcEventsJsonSerializerContext.Default.GetMatches) ??
                   throw new MissingDataException("Unable to parse FrcEvents matches response");

        return json.Matches.Select(match =>
        {
            var utcActualStart = match.ActualStartTime is not null
                ? TimeZoneInfo.ConvertTimeToUtc(match.ActualStartTime.Value, eventTz) : (DateTime?)null;
            var utcPostResult = match.PostResultTime is not null
                ? TimeZoneInfo.ConvertTimeToUtc(match.PostResultTime.Value, eventTz) : (DateTime?)null;
            return new MatchResult
            {
                MatchNumber = match.MatchNumber,
                ActualStartTime = utcActualStart,
                PostResultTime = utcPostResult,
                MatchVideoLink = match.MatchVideoLink
            };
        }).ToList();
    }

    public async Task<List<QualRanking>> GetQualRankingsForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        Debug.Assert(evt.Season is not null, "evt.Season is null, ensure it's included in DB fetches");

        var resp = await PerformRequest(BuildGetRequest($"{GetSeason(evt.Season)}/rankings/{evt.Code}"));
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync(FrcEventsJsonSerializerContext.Default.GetRankings) ??
                   throw new MissingDataException("Unable to parse FrcEvents matches response");

        return json.Rankings.Select(r => new QualRanking
        {
            Rank = r.Rank,
            TeamNumber = r.TeamNumber,
            SortOrder1 = r.SortOrder1,
            SortOrder2 = r.SortOrder2,
            SortOrder3 = r.SortOrder3,
            SortOrder4 = r.SortOrder4,
            SortOrder5 = r.SortOrder5,
            SortOrder6 = r.SortOrder6,
            Wins = r.Wins,
            Ties = r.Ties,
            Losses = r.Losses,
            QualAverage = r.QualAverage,
            Disqualifications = r.Dq,
            MatchesPlayed = r.MatchesPlayed
        }).ToList();
    }

    public async Task<List<Alliance>> GetAlliancesForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        var resp = await PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/alliances/{evt.Code}"));
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync(FrcEventsJsonSerializerContext.Default.GetAlliances) ??
                   throw new MissingDataException("Unable to parse FrcEvents alliances response");
        
        return json.Alliances.Select(a => new Alliance
        {
            Name = a.Name,
            TeamNumbers = new List<int?> {
                a.Captain,
                a.Round1,
                a.Round2,
                a.Round3,
                a.Backup
            }.Where(t => t is not null).Select(t => t!.Value).ToList()
        }).ToList();
    }

    public async Task<List<PlayoffMatch>> GetPlayoffResultsForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        var eventTz = TimeZoneInfo.FindSystemTimeZoneById(evt.TimeZone);
        
        var resultsTask = PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/matches/{evt.Code}", new()
            {
                { "tournamentLevel", "Playoff" }
            }));
        
        var scheduleTask = PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/schedule/{evt.Code}", new()
            {
                { "tournamentLevel", "Playoff" }
            }));
        
        var scheduleResp = await scheduleTask;
        scheduleResp.EnsureSuccessStatusCode();
        var scheduleJson = await scheduleResp.Content.ReadFromJsonAsync(FrcEventsJsonSerializerContext.Default.GetSchedule) ??
                           throw new MissingDataException("Unable to parse FrcEvents schedule response");
        var schedule = scheduleJson.Schedule.Select(s => new
        {
            s.MatchNumber,
            ScheduledStartTime = s.StartTime
        }).ToList();

        var results = await resultsTask;
        results.EnsureSuccessStatusCode();
        var resultsJson = await results.Content.ReadFromJsonAsync(FrcEventsJsonSerializerContext.Default.GetMatches) ??
                          throw new MissingDataException("Unable to parse FrcEvents matches response");
        
        return resultsJson.Matches.Select(match =>
        {
            var matchNumber = match.MatchNumber;
            var schMatch = schedule.FirstOrDefault(s => s.MatchNumber == matchNumber);
            var utcScheduledStart = schMatch?.ScheduledStartTime is not null
                ? TimeZoneInfo.ConvertTimeToUtc(schMatch.ScheduledStartTime.Value, eventTz)
                : (DateTime?)null;
            var actualStart = match.ActualStartTime;
            var utcActualStart =
                actualStart is not null ? TimeZoneInfo.ConvertTimeToUtc(actualStart.Value, eventTz) : (DateTime?)null;
            var postResult = match.PostResultTime;
            var utcPostResult =
                postResult is not null ? TimeZoneInfo.ConvertTimeToUtc(postResult.Value, eventTz) : (DateTime?)null;
            var (redTeams, blueTeams) = ApiTeamsToFimTeams(match.Teams);
            var redScore = match.ScoreRedFinal;
            var blueScore = match.ScoreBlueFinal;
            var winner = (MatchWinner?)null;
            if (utcPostResult is not null && redScore is not null && blueScore is not null)
            {
                if (redScore > blueScore) winner = MatchWinner.Red;
                if (blueScore > redScore) winner = MatchWinner.Blue;
            }
            return new PlayoffMatch
            {
                MatchNumber = matchNumber,
                MatchName = match.Description,
                ScheduledStartTime = utcScheduledStart,
                ActualStartTime = utcActualStart,
                PostResultTime = utcPostResult,
                RedAllianceTeams = redTeams.Length > 0 ? redTeams : null,
                BlueAllianceTeams = blueTeams.Length > 0 ? blueTeams : null,
                Winner = winner,
                MatchVideoLink = match.MatchVideoLink
            };
        }).ToList();
    }

    public IPlayoffTiebreak GetPlayoffTiebreak(FiMAdminApi.Models.Models.Event evt)
    {
        return new FrcEvents2025Tiebreak(this, evt);
    }

    public async Task<List<Award>> GetAwardsForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        var resp = await PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/awards/event/{evt.Code}"));
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync(FrcEventsJsonSerializerContext.Default.GetAwards) ??
                   throw new MissingDataException("Unable to parse FrcEvents awards response");

        return json.Awards.Select(a => new Award
        {
            Name = a.Name,
            TeamNumber = a.TeamNumber
        }).ToList();
    }

    public static string GetWebUrl(WebUrlType type, FiMAdminApi.Models.Models.Event evt)
    {
        var baseUrl =
            ((FormattableString)$"https://frc-events.firstinspires.org/{GetSeason(evt.Season!)}/{evt.Code}")
            .EncodeString(Uri.EscapeDataString);
        return type switch
        {
            WebUrlType.Home => baseUrl,
            WebUrlType.QualSchedule => $"{baseUrl}/qualifications",
            WebUrlType.PlayoffSchedule => $"{baseUrl}/playoffs",
            WebUrlType.ShortLink => baseUrl.Replace("https://frc-events.firstinspires.org", "frc.events"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    public async Task<string?> CheckHealth()
    {
        var resp = await PerformRequest(BuildGetRequest($""));
        
        if (resp.IsSuccessStatusCode) return null;

        return await resp.Content.ReadAsStringAsync();
    }

    public async Task<TScoreDetailResponse> GetPlayoffScoreDetails<TScoreDetailResponse>(FiMAdminApi.Models.Models.Event evt)
    {
        var scoreResp = await PerformRequest(
            BuildGetRequest($"{GetSeason(evt.Season!)}/scores/{evt.Code}/playoff"));
        scoreResp.EnsureSuccessStatusCode();
        var jsonTypeInfo = FrcEventsJsonSerializerContext.Default.GetTypeInfo(typeof(TScoreDetailResponse));
        if (jsonTypeInfo is null)
            throw new ApplicationException("Unsupported type to deserialize scores response from FrcEvents");

        return await scoreResp.Content.ReadFromJsonAsync((JsonTypeInfo<TScoreDetailResponse>) jsonTypeInfo) ??
               throw new MissingDataException("Unable to parse FrcEvents scores response");
    }
    
    public static string GetSeason(Season season)
    {
        return season.StartTime.Year.ToString();
    }
    
    private async Task<IEnumerable<Event>> GetAndParseEvents(Season season, string? eventCode = null, string? districtCode = null)
    {
        var queryParams = new Dictionary<string, string>();
        if (eventCode is not null) queryParams.Add("eventCode", eventCode);
        if (districtCode is not null) queryParams.Add("districtCode", districtCode);
        var resp = await PerformRequest(BuildGetRequest($"{GetSeason(season)}/events", queryParams));
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync(FrcEventsJsonSerializerContext.Default.GetEvents) ??
                   throw new MissingDataException("Unable to parse FrcEvents events response");
        
        return json.Events.Select(evt =>
        {
            var timeZone = NormalizeTimeZone(evt.Timezone);

            return new Event
            {
                EventCode = evt.Code,
                Name = evt.Name,
                DistrictCode = evt.DistrictCode,
                City = evt.City,
                StartTime = new DateTimeOffset(evt.DateStart, timeZone.GetUtcOffset(evt.DateStart)),
                EndTime = new DateTimeOffset(evt.DateEnd, timeZone.GetUtcOffset(evt.DateEnd)),
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

    private static (int[] redAllianceTeams, int[] blueAllianceTeams) ApiTeamsToFimTeams(MatchTeam[] apiTeams)
    {
        var apiDict = apiTeams.ToDictionary(t => t.Station, t => t.TeamNumber);
        var red = RedTeamStations.Select(s => apiDict.GetValueOrDefault(s)).Where(IsRealTeamNumber).Select(t => t!.Value).ToArray();
        var blue = BlueTeamStations.Select(s => apiDict.GetValueOrDefault(s)).Where(IsRealTeamNumber).Select(t => t!.Value).ToArray();

        return (red, blue);

        bool IsRealTeamNumber(int? teamNumber)
        {
            return teamNumber is not null && teamNumber != 0;
        }
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
            $"{endpoint.EncodeString(Uri.EscapeDataString)}{(queryParams is not null && queryParams.Count > 0 ? QueryString.Create(queryParams) : "")}";

        if (relativeUri.StartsWith('/'))
            throw new ArgumentException("Endpoint must be a relative path", nameof(endpoint));
        
        request.RequestUri = new Uri(_baseUrl, relativeUri);

        return request;
    }
}