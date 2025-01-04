using System.ComponentModel.DataAnnotations;

namespace FiMAdminApi.Data.Models;

public class ScheduleDeviation
{
    [Key]
    public int Id { get; set; }
    
    public required Guid EventId { get; set; }
    public string? Description { get; set; }
    public required long AfterMatchId { get; set; }
    public virtual Match? AfterMatch { get; set; }
    public long? AssociatedMatchId { get; set; }
    public virtual Match? AssociatedMatch { get; set; }
}