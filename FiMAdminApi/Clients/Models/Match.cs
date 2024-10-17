namespace FiMAdminApi.Clients.Models;

public class ScheduledMatch
{
    public int MatchNumber { get; set; }
    public int[]? RedAllianceTeams { get; set; }
    public int[]? BlueAllianceTeams { get; set; }
    public DateTime ScheduledStartTime { get; set; }
}

public class MatchResult
{
    public int MatchNumber { get; set; }
    public DateTime ActualStartTime { get; set; }
    public DateTime PostResultTime { get; set; }
}