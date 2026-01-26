using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Data.Firebase.Models;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Helpers;
using FiMAdminApi.Models.Models;
using Firebase.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Match = FiMAdminApi.Models.Models.Match;

namespace FiMAdminApi.Data.Firebase;

// TODO: All calls to this should probably be via the EventRepo for better handling of Firebase not being configured
public class FrcFirebaseRepository(DataContext dataContext, IConfiguration config, FirebaseClient? firebaseClient = null)
{
    private static readonly JsonSerializerOptions JsonOptionsWebNoNull =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
            
        };
    
    private static readonly JsonSerializerOptions JsonOptionsWeb =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
    
    public async Task UpdateEvent(Event evt)
    {
        if (firebaseClient is null || evt.Season?.Level?.Name != "FRC") return;

        if (string.IsNullOrEmpty(evt.Key)) return;

        var eventTz = TimeZoneInfo.FindSystemTimeZoneById(evt.TimeZone);
        var localStart = TimeZoneInfo.ConvertTime(new DateTimeOffset(evt.StartTime, TimeSpan.Zero), eventTz);
        var localEnd = TimeZoneInfo.ConvertTime(new DateTimeOffset(evt.EndTime, TimeSpan.Zero), eventTz);
        
        var cartId = evt.TruckRouteId != null
            ? (await dataContext.Equipment.FirstOrDefaultAsync(e =>
                e.EquipmentType!.Name == "AV Cart" && e.TruckRouteId == evt.TruckRouteId))?.Id
            : null;

        string? streamUrl = null;
        var urlTemplate = config["EventStream:TwitchEmbedTemplate"];
        if (!string.IsNullOrEmpty(urlTemplate) && evt.TruckRouteId is not null)
        {
            var routeName = await dataContext.TruckRoutes.Where(r => r.Id == evt.TruckRouteId).Select(r => r.Name)
                .FirstOrDefaultAsync();
            if (routeName is not null)
            {
                var routeNumberMatch = FirebaseRepoRegexes.RouteNumberRegex.Match(routeName);
                if (routeNumberMatch.Success)
                {
                    streamUrl = string.Format(urlTemplate, routeNumberMatch.Value);
                }
            }
        }
        
        var json = JsonSerializer.Serialize(new FirebaseEvent
        {
            CartId = cartId,
            DataSource = evt.SyncSource switch
            {
                DataSources.FrcEvents => "frcEvents",
                DataSources.BlueAlliance => "blueAlliance",
                _ => null
            },
            EventCode = evt.Code ?? string.Empty,
            LastModifiedMs = null,
            Mode = "automatic",
            Name = evt.Name,
            Start = localStart,
            StartMs = localStart.ToUnixTimeMilliseconds(),
            End = localEnd,
            EndMs = localEnd.ToUnixTimeMilliseconds(),
            State = evt.Status switch
            {
                EventStatus.NotStarted => FirebaseEventState.Pending,
                EventStatus.AwaitingQuals => FirebaseEventState.AwaitingQualSchedule,
                EventStatus.QualsInProgress => FirebaseEventState.QualsInProgress,
                EventStatus.AwaitingAlliances => FirebaseEventState.AwaitingAlliances,
                EventStatus.AwaitingPlayoffs => FirebaseEventState.PlayoffsInProgress,
                EventStatus.PlayoffsInProgress => FirebaseEventState.PlayoffsInProgress,
                EventStatus.WinnerDetermined => FirebaseEventState.EventOver,
                EventStatus.Completed => FirebaseEventState.EventOver,
                _ => throw new ArgumentOutOfRangeException()
            },
            StreamEmbedLink = streamUrl
        }, JsonOptionsWebNoNull);
        await firebaseClient.Child($"seasons/{evt.Season.StartTime.Year}/events/{evt.Key}").PatchAsync(json);
    }

    public async Task UpdateEventQualCurrentMatchNumber(Event evt, int currentMatchNumber)
    {
        if (firebaseClient is null || evt.Season?.Level?.Name != "FRC") return;

        await firebaseClient.Child($"seasons/{evt.Season.StartTime.Year}/events/{evt.Key}/currentMatchNumber")
            .PutAsync(currentMatchNumber.ToString());
    }

    public async Task UpdateEventQualMatches(Event evt, List<Match> qualMatches, List<ScheduleDeviation> deviations)
    {
        if (firebaseClient is null || evt.Season?.Level?.Name != "FRC") return;
        
        var returnList = new List<FirebaseMatchOrBreak>();
        var firstPlays = qualMatches.Where(m => m.TournamentLevel == TournamentLevel.Qualification)
            .GroupBy(m => m.MatchNumber).Select(mg => mg.OrderBy(m => m.PlayNumber).First()).ToList();
        returnList.AddRange(firstPlays.Select(m => DbMatchToFbMatch(m)));

        foreach (var deviation in deviations)
        {
            var idx = returnList.FindIndex(mb => mb is FirebaseMatch m && m.Id == deviation.AfterMatchId);
            if (idx == -1) continue;

            if (deviation.AssociatedMatchId is not null)
            {
                var associatedMatch = qualMatches.FirstOrDefault(m => m.Id == deviation.AssociatedMatchId);
                if (associatedMatch is null) continue;
                returnList.Insert(idx + 1, DbMatchToFbMatch(associatedMatch));
            }
            else
            {
                returnList.Insert(idx + 1, new FirebaseBreak
                {
                    Description = deviation.Description
                });
            }
        }

        var json = JsonSerializer.Serialize(returnList, JsonOptionsWebNoNull);
        
        await firebaseClient.Child($"seasons/{evt.Season.StartTime.Year}/qual/{evt.Key}").PutAsync(json);
        
        // TODO: How to handle replays gracefully, given most events will not schedule them. How much should be
        // calculated on the FE? Should play 2+ be included in the `quals` array? Maybe don't include play 2+ unless a
        // schedule deviation exists for it?
    }
    
    public async Task UpdateEventRankings(Event evt, List<EventRanking> rankings)
    {
        if (firebaseClient is null || evt.Season?.Level?.Name != "FRC") return;
        
        // TODO: instead of being an array, do an object keyed on team number so we can do more minimal writes
        var json = JsonSerializer.Serialize(rankings.Select(r =>
        {
            var so = r.SortOrders?.ToArray();
            var soLen = so?.Length ?? 0;
            return new FirebaseRanking
            {
                TeamNumber = r.TeamNumber,
                Rank = r.Rank,
                Wins = r.Wins,
                Ties = r.Ties,
                Losses = r.Losses,
                RankingPoints = soLen > 0 ? so![0] : null,
                SortOrder2 = soLen > 1 ? so![1] : null,
                SortOrder3 = soLen > 2 ? so![2] : null,
                SortOrder4 = soLen > 3 ? so![3] : null,
                SortOrder5 = soLen > 4 ? so![4] : null
            };
        }), JsonOptionsWeb);
        
        await firebaseClient.Child($"seasons/{evt.Season.StartTime.Year}/rankings/{evt.Key}").PutAsync(json);
    }
    
    public async Task UpdateEventAlliances(Event evt, List<Alliance> alliances)
    {
        if (firebaseClient is null || evt.Season?.Level?.Name != "FRC") return;

        var fbAlliances = new List<FirebaseAlliance>();

        foreach (var alliance in alliances)
        {
            var allianceNumber = int.TryParse(alliance.Name.Split(' ')[^1], out var aNum) ? aNum : (int?)null;

            if (allianceNumber is null) continue;
            
            fbAlliances.Add(new FirebaseAlliance
            {
                Number = allianceNumber.Value,
                ShortName = allianceNumber.ToString() ?? string.Empty,
                Teams = alliance.TeamNumbers ?? []
            });
        }
        
        var json = JsonSerializer.Serialize(fbAlliances, JsonOptionsWeb);
        
        await firebaseClient.Child($"seasons/{evt.Season.StartTime.Year}/alliances/{evt.Key}").PutAsync(json);
    }
    
    public async Task UpdateEventPlayoffMatches(Event evt, List<Match> playoffMatches, List<Alliance> alliances)
    {
        if (firebaseClient is null || evt.Season?.Level?.Name != "FRC") return;

        // TODO: playoff breaks? or is this calculated in the FE
        var finals = PlayoffHelpers.GetPlayoffFinalsMatches(playoffMatches).ToList();
        var nonFinals = playoffMatches.Except(finals);

        var allianceDict = alliances.ToDictionary(a => a.Id,
            a => int.TryParse(a.Name.Split()[^1], out var aNum) ? aNum : (int?)null);

        var fbMatches = nonFinals.ToDictionary(m => m.MatchNumber.ToString(), m => DbMatchToFbMatch(m, allianceDict));
        if (finals.Count > 0)
        {
            
            fbMatches["F"] = DbMatchToFbMatch(finals[0], allianceDict, PlayoffHelpers.GetHeadToHeadWinner(finals));
        }
        
        // NOTE: can't use non-null serialization options, we want null winners to pass through
        var json = JsonSerializer.Serialize(fbMatches, JsonOptionsWeb);
        
        await firebaseClient.Child($"seasons/{evt.Season.StartTime.Year}/bracket/{evt.Key}").PutAsync(json);
    }
    
    private static FirebaseMatch DbMatchToFbMatch(Match match, Dictionary<long, int?>? alliances = null, MatchWinner? overrideWinner = null)
    {
        return new FirebaseMatch
        {
            Id = match.Id,
            Number = match.MatchNumber,
            Participants = new Dictionary<string, int>(
            [
                ..match.RedAllianceTeams?.Index().Select(i => new KeyValuePair<string, int>($"Red{i.Index + 1}", i.Item)) ?? [],
                ..match.BlueAllianceTeams?.Index().Select(i => new KeyValuePair<string, int>($"Blue{i.Index + 1}", i.Item)) ?? []
            ]),
            RedAlliance = alliances?.GetValueOrDefault(match.RedAllianceId ?? -1), // lazy shortcut, -1 shouldn't happen
            BlueAlliance = alliances?.GetValueOrDefault(match.BlueAllianceId ?? -1),
            Winner = (overrideWinner ?? match.Winner) switch
            {
                MatchWinner.Red => "red",
                MatchWinner.Blue => "blue",
                MatchWinner.TrueTie or null => null,
                _ => throw new ArgumentOutOfRangeException()
            }
        };
    }
}

public static partial class FirebaseRepoRegexes
{
    [GeneratedRegex(@"(?<num>\d+)$")]
    public static partial Regex RouteNumberRegex { get; }
}