using System.ComponentModel;
using FiMAdminApi.Models.Enums;

namespace FiMAdminApi.Models.Models;

public class Event
{
    public Guid Id { get; set; }
    public required int SeasonId { get; set; }
    public required string Key { get; set; }
    public string? Code { get; set; }
    public required string Name { get; set; }
    public DataSources? SyncSource { get; set; }
    public required bool IsOfficial { get; set; }
    public int? TruckRouteId { get; set; }
    public TruckRoute? TruckRoute { get; set; }
    public required DateTime StartTime { get; set; }
    public required DateTime EndTime { get; set; }
    public required string TimeZone { get; set; }
    public DateTimeOffset? SyncAsOf { get; set; }
    public required EventStatus Status { get; set; } = EventStatus.NotStarted;
    
    // Relations
    [Description("Note: This object may not be populated in some endpoints.")]
    public Season? Season { get; set; }
}

public enum EventStatus
{
    NotStarted,
    AwaitingQuals,
    QualsInProgress,
    AwaitingAlliances,
    AwaitingPlayoffs,
    PlayoffsInProgress,
    WinnerDetermined,
    Completed
}