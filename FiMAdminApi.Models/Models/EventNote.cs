using System.ComponentModel.DataAnnotations;

namespace FiMAdminApi.Models.Models;

public class EventNote
{
    [Key]
    public int Id { get; set; }
    public required Guid EventId { get; set; }
    public required string Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public required Guid CreatedBy { get; set; }
}