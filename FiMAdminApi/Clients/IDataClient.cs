using FiMAdminApi.Clients.Models;
using FiMAdminApi.Data.Models;
using Event = FiMAdminApi.Clients.Models.Event;

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
    public Task<string?> CheckHealth();
}