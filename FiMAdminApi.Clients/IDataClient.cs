using FiMAdminApi.Clients.Models;
using FiMAdminApi.Clients.PlayoffTiebreaks;
using Alliance = FiMAdminApi.Clients.Models.Alliance;
using Event = FiMAdminApi.Clients.Models.Event;
using Season = FiMAdminApi.Models.Models.Season;

namespace FiMAdminApi.Clients;

/// <summary>
/// A generic client which can provide data for FIRST events at any level
/// </summary>
public interface IDataClient
{
    public Task<Event?> GetEventAsync(Season season, string eventCode);
    public Task<List<Event>> GetDistrictEventsAsync(Season season, string districtCode);
    public Task<List<Team>> GetTeamsForEvent(Season season, string eventCode);
    public Task<List<ScheduledMatch>> GetQualScheduleForEvent(FiMAdminApi.Models.Models.Event evt);
    public Task<List<MatchResult>> GetQualResultsForEvent(FiMAdminApi.Models.Models.Event evt);
    public Task<List<QualRanking>> GetQualRankingsForEvent(FiMAdminApi.Models.Models.Event evt);
    public Task<List<Alliance>> GetAlliancesForEvent(FiMAdminApi.Models.Models.Event evt);
    public Task<List<PlayoffMatch>> GetPlayoffResultsForEvent(FiMAdminApi.Models.Models.Event evt);
    public IPlayoffTiebreak GetPlayoffTiebreak(FiMAdminApi.Models.Models.Event evt);
    public Task<List<Award>> GetAwardsForEvent(FiMAdminApi.Models.Models.Event evt);
    public Task<string?> CheckHealth();
}