using FiMAdminApi.Models.Enums;

namespace FiMAdminApi.Clients.Models;

public class BaseMatch
{
    public int MatchNumber { get; set; }
}

public class ScheduledMatch : BaseMatch
{
    public int[]? RedAllianceTeams { get; set; }
    public int[]? BlueAllianceTeams { get; set; }
    public DateTime ScheduledStartTime { get; set; }
}

public class MatchResult : BaseMatch
{
    public DateTime? ActualStartTime { get; set; }
    public DateTime? PostResultTime { get; set; }
}

public class PlayoffMatch : BaseMatch
{
    public string? MatchName { get; set; }
    public int[]? RedAllianceTeams { get; set; }
    public int[]? BlueAllianceTeams { get; set; }
    public DateTime? ScheduledStartTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? PostResultTime { get; set; }
    public MatchWinner? Winner { get; set; }
}