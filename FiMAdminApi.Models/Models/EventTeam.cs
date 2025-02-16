namespace FiMAdminApi.Models.Models;

public class EventTeam
{
    public int Id { get; set; }
    public required Guid EventId { get; set; }
    public virtual Event? Event { get; set; }
    public required int TeamNumber { get; set; }
    public required int LevelId { get; set; }
    public virtual Level? Level { get; set; }
    public string? Notes { get; set; }
    public required string StatusId { get; set; }
    public virtual EventTeamStatus? Status { get; set; }
}