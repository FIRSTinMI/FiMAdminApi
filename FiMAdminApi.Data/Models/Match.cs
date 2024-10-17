using FiMAdminApi.Data.Enums;

namespace FiMAdminApi.Data.Models;

public class Match
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public TournamentLevel TournamentLevel { get; set; }
    public int MatchNumber { get; set; }
    public int? PlayNumber { get; set; }
    public int[]? RedAllianceTeams { get; set; }
    public int[]? BlueAllianceTeams { get; set; }
    
    // Used in playoffs
    public int? RedAllianceId { get; set; }
    public int? BlueAllianceId { get; set; }
    
    // UTC
    public DateTime ScheduledStartTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? PostResultTime { get; set; }

    public bool IsDiscarded { get; set; } = false;
}