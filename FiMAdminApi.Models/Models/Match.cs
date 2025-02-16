using System.ComponentModel.DataAnnotations;
using FiMAdminApi.Models.Enums;

namespace FiMAdminApi.Models.Models;

public class Match
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public TournamentLevel TournamentLevel { get; set; }
    [StringLength(50)] public string? MatchName { get; set; }
    public int MatchNumber { get; set; }
    public int? PlayNumber { get; set; }
    public int[]? RedAllianceTeams { get; set; }
    public int[]? BlueAllianceTeams { get; set; }
    
    // Used in playoffs
    public long? RedAllianceId { get; set; }
    public long? BlueAllianceId { get; set; }
    public MatchWinner? Winner { get; set; }
    
    // UTC
    public DateTime? ScheduledStartTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? PostResultTime { get; set; }

    public bool IsDiscarded { get; set; } = false;
}