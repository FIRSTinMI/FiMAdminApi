using System.ComponentModel.DataAnnotations;

namespace FiMAdminApi.Models.Models;

public class EventTeamStatus
{
    /// <summary>
    /// Unique identifier for the status, but still understandable by humans
    /// </summary>
    [Key]
    public required string Id { get; set; }
    
    public required string Name { get; set; }
    
    /// <summary>
    /// Higher numbers are teams closer to ready for matches
    /// </summary>
    public required int Ordinal { get; set; }
}

public static class KnownEventTeamStatuses
{
    public const string Dropped = "Dropped";
    public const string NotArrived = "NotArrived";
}