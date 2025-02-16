using FiMAdminApi.Clients.Models;
using FiMAdminApi.Clients.PlayoffTiebreaks;
using Alliance = FiMAdminApi.Clients.Models.Alliance;
using Event = FiMAdminApi.Clients.Models.Event;
using Season = FiMAdminApi.Data.Models.Season;

namespace FiMAdminApi.Clients;

/// <summary>
/// A generic client which can provide data for FIRST events at any level
/// </summary>
public interface IDataClient
{
    public Task<Event?> GetEventAsync(Season season, string eventCode);
    public Task<List<Event>> GetDistrictEventsAsync(Season season, string districtCode);
    public Task<List<Team>> GetTeamsForEvent(Season season, string eventCode);
    public Task<List<ScheduledMatch>> GetQualScheduleForEvent(Data.Models.Event evt);
    public Task<List<MatchResult>> GetQualResultsForEvent(Data.Models.Event evt);
    public Task<List<QualRanking>> GetQualRankingsForEvent(Data.Models.Event evt);
    public Task<List<Alliance>> GetAlliancesForEvent(Data.Models.Event evt);
    public Task<List<PlayoffMatch>> GetPlayoffResultsForEvent(Data.Models.Event evt);
    public IPlayoffTiebreak GetPlayoffTiebreak(Data.Models.Event evt);
    public Task<string?> CheckHealth();
}