using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using FiMAdminApi.Clients.Exceptions;
using FiMAdminApi.Clients.Extensions;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Clients.Models.FtcEvents;
using FiMAdminApi.Clients.PlayoffTiebreaks;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Alliance = FiMAdminApi.Clients.Models.Alliance;
using Event = FiMAdminApi.Clients.Models.Event;

namespace FiMAdminApi.Clients;

public class FtcEventsDataClient : RestClient, IDataClient
{
    private readonly string _apiKey;
    private readonly Uri _baseUrl;
    
    private static readonly string[] RedTeamStations = ["Red1", "Red2"];
    private static readonly string[] BlueTeamStations = ["Blue1", "Blue2"];
    
    public FtcEventsDataClient(IServiceProvider sp)
        : base(
            sp.GetRequiredService<ILogger<FtcEventsDataClient>>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("FrcEvents"))
    {
        TrackLastModified = true;
        
        var configSection = sp.GetRequiredService<IConfiguration>().GetRequiredSection("Clients:FtcEvents");
        
        var apiKey = configSection["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ApplicationException("FtcEvents ApiKey was null but is required");
        _apiKey = apiKey;

        var baseUrl = configSection["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ApplicationException("FtcEvents BaseUrl was null but is required");
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
        var response = await PerformRequest(BuildGetRequest($"{GetSeason(season)}/teams?eventCode={eventCode}"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync(FtcEventsJsonSerializerContext.Default.GetTeams);

        return json!.Teams.Select(t => new Team
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

        var response =
            await PerformRequest(BuildGetRequest($"{GetSeason(evt.Season!)}/schedule/{evt.Code}?tournamentLevel=qual"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync(FtcEventsJsonSerializerContext.Default.GetSchedule);

        return json!.Schedule.Select(m =>
        {
            var (red, blue) = ApiTeamsToFimTeams(m.Teams);
            var utcScheduledStart = m.StartTime is not null
                ? TimeZoneInfo.ConvertTimeToUtc(m.StartTime.Value, eventTz)
                : throw new MissingDataException("Expected all scheduled qual matches to have a start time but didn't");
            return new ScheduledMatch
            {
                MatchNumber = m.MatchNumber,
                RedAllianceTeams = red,
                BlueAllianceTeams = blue,
                ScheduledStartTime = utcScheduledStart
            };
        }).ToList();
    }

    public async Task<List<MatchResult>> GetQualResultsForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        var eventTz = TimeZoneInfo.FindSystemTimeZoneById(evt.TimeZone);

        var response =
            await PerformRequest(BuildGetRequest($"{GetSeason(evt.Season!)}/matches/{evt.Code}?tournamentLevel=qual"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync(FtcEventsJsonSerializerContext.Default.GetMatches);

        return json!.Matches.Select(m =>
        {
            var utcActualStart = m.ActualStartTime is not null
                ? TimeZoneInfo.ConvertTimeToUtc(m.ActualStartTime.Value, eventTz) : (DateTime?)null;
            var utcPostResult = m.PostResultTime is not null
                ? TimeZoneInfo.ConvertTimeToUtc(m.PostResultTime.Value, eventTz) : (DateTime?)null;
            return new MatchResult
            {
                MatchNumber = m.MatchNumber,
                ActualStartTime = utcActualStart,
                PostResultTime = utcPostResult,
                MatchVideoLink = null
            };
        }).ToList();
    }

    public async Task<List<QualRanking>> GetQualRankingsForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        var response =
            await PerformRequest(BuildGetRequest($"{GetSeason(evt.Season!)}/rankings/{evt.Code}"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync(FtcEventsJsonSerializerContext.Default.GetRankings) ??
                   throw new MissingDataException("Unable to parse FtcEvents matches response");

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
        var response =
            await PerformRequest(BuildGetRequest($"{GetSeason(evt.Season!)}/alliances/{evt.Code}"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync(FtcEventsJsonSerializerContext.Default.GetAlliances) ??
                   throw new MissingDataException("Unable to parse FtcEvents alliances response");
        
        return json.Alliances.Select(a => new Alliance
        {
            Name = a.Name,
            TeamNumbers = new List<int?> {
                a.Captain,
                a.Round1,
                a.Round2,
                a.Round3,
                a.Backup,
                a.BackupReplaced
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
        var scheduleJson = await scheduleResp.Content.ReadFromJsonAsync(FtcEventsJsonSerializerContext.Default.GetSchedule) ??
                           throw new MissingDataException("Unable to parse FtcEvents schedule response");
        var schedule = scheduleJson.Schedule.Select(s => new
        {
            s.Series,
            ScheduledStartTime = s.StartTime
        }).ToList();

        var results = await resultsTask;
        results.EnsureSuccessStatusCode();
        var resultsJson = await results.Content.ReadFromJsonAsync(FtcEventsJsonSerializerContext.Default.GetMatches) ??
                          throw new MissingDataException("Unable to parse FtcEvents matches response");
        
        return resultsJson.Matches.Select(match =>
        {
            var schMatch = schedule.FirstOrDefault(s => s.Series == match.Series);
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
                MatchNumber = match.Series,
                MatchName = match.Description,
                ScheduledStartTime = utcScheduledStart,
                ActualStartTime = utcActualStart,
                PostResultTime = utcPostResult,
                RedAllianceTeams = redTeams.Length > 0 ? redTeams : null,
                BlueAllianceTeams = blueTeams.Length > 0 ? blueTeams : null,
                Winner = winner,
                MatchVideoLink = null
            };
        }).ToList();
    }

    public IPlayoffTiebreak GetPlayoffTiebreak(FiMAdminApi.Models.Models.Event evt)
    {
        // TODO: Implement later
        return new NoopPlayoffTiebreak();
    }

    public async Task<List<Award>> GetAwardsForEvent(FiMAdminApi.Models.Models.Event evt)
    {
        var response = await PerformRequest(BuildGetRequest($"{GetSeason(evt.Season!)}/awards/{evt.Code}"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync(FtcEventsJsonSerializerContext.Default.GetAwards);

        return json!.Awards.Select(a => new Award
        {
            Name = a.Name,
            TeamNumber = a.TeamNumber
        }).ToList();
    }

    public static string GetWebUrl(WebUrlType type, FiMAdminApi.Models.Models.Event evt)
    {
        var baseUrl =
            ((FormattableString)$"https://ftc-events.firstinspires.org/{GetSeason(evt.Season!)}/{evt.Code}")
            .EncodeString(Uri.EscapeDataString);
        return type switch
        {
            WebUrlType.Home => baseUrl,
            WebUrlType.QualSchedule => $"{baseUrl}/qualifications",
            WebUrlType.PlayoffSchedule => $"{baseUrl}/playoffs",
            WebUrlType.ShortLink => baseUrl.Replace("https://ftc-events.firstinspires.org", "ftc.events"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }
    
    private async Task<IEnumerable<Event>> GetAndParseEvents(Season season, string? eventCode = null, string? districtCode = null)
    {
        var queryParams = new Dictionary<string, string>();
        if (eventCode is not null) queryParams.Add("eventCode", eventCode);
        var resp = await PerformRequest(BuildGetRequest($"{GetSeason(season)}/events", queryParams));
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync(FtcEventsJsonSerializerContext.Default.GetEvents) ??
                   throw new MissingDataException("Unable to parse FrcEvents events response");

        var events = json.Events;

        if (districtCode is not null)
        {
            events = events.Where(e => e.RegionCode == districtCode).ToArray();
        }
        
        return events.Select(evt =>
        {
            var timeZone = NormalizeTimeZone(evt.Timezone);
            
            // The FTC event says it ends at midnight when it actually runs through 23:59
            var realEndTime = new DateTimeOffset(evt.DateEnd, timeZone.GetUtcOffset(evt.DateEnd))
                .AddDays(1)
                .AddMinutes(-1);

            return new Event
            {
                EventCode = evt.Code,
                Name = evt.Name,
                DistrictCode = evt.RegionCode,
                City = evt.City,
                StartTime = new DateTimeOffset(evt.DateStart, timeZone.GetUtcOffset(evt.DateStart)),
                EndTime = realEndTime,
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
    private HttpRequestMessage BuildGetRequest(FormattableString endpoint, Dictionary<string, string>? queryParams = null)
    {
        var request = new HttpRequestMessage();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(_apiKey.ToLower())));

        var relativeUri =
            $"{endpoint.EncodeString(Uri.EscapeDataString)}{(queryParams is not null && queryParams.Count > 0 ? QueryString.Create(queryParams) : "")}";

        if (relativeUri.StartsWith('/'))
            throw new ArgumentException("Endpoint must be a relative path", nameof(endpoint));
        
        request.RequestUri = new Uri(_baseUrl, relativeUri);

        return request;
    }
}