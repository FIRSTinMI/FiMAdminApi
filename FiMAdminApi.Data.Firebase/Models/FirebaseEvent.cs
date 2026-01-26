namespace FiMAdminApi.Data.Firebase.Models;

public class FirebaseEvent
{
    public Guid? CartId { get; set; }
    public int? CurrentMatchNumber { get; set; }
    public string? DataSource { get; set; }
    public DateTimeOffset End { get; set; }
    public long EndMs { get; set; }
    public required string EventCode { get; set; }
    public bool HasQualSchedule { get; set; }
    public int? LastModifiedMs { get; set; }
    public string Mode { get; set; } = "automatic";
    public required string Name { get; set; }
    public int? NumQualMatches { get; set; }
    public DateTimeOffset Start { get; set; }
    public long StartMs { get; set; }
    public FirebaseEventState State { get; set; }
    public string? StreamEmbedLink { get; set; }
}

public enum FirebaseEventState
{
    Pending,
    AwaitingQualSchedule,
    QualsInProgress,
    AwaitingAlliances,
    PlayoffsInProgress,
    EventOver
}