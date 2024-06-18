using System.ComponentModel;

namespace FiMAdminApi.Data.Models;

public class Event
{
    public Guid Id { get; set; }
    public required int SeasonId { get; set; }
    public required string Key { get; set; }
    public string? Code { get; set; }
    public required string Name { get; set; }
    public string? SyncSource { get; set; }
    public required bool IsOfficial { get; set; }
    public int? TruckRouteId { get; set; }
    public TruckRoute? TruckRoute { get; set; }
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    public DateTimeOffset? SyncAsOf { get; set; }
    public required string Status { get; set; } = "NotStarted";
    
    // Relations
    [Description("Note: This object may not be populated in some endpoints.")]
    public Season? Season { get; set; }
}