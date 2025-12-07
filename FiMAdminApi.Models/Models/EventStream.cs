using System.ComponentModel.DataAnnotations;
using FiMAdminApi.Models.Enums;

namespace FiMAdminApi.Models.Models;

public class EventStream
{
    [Key]
    public long Id { get; set; }

    [Required]
    public Guid EventId { get; set; }

    public string? Title { get; set; }
    public bool Primary { get; set; } = true;

    [Required]
    public StreamPlatform Platform { get; set; }

    [Required]
    public string Channel { get; set; } = null!;

    public string? Url { get; set; }

    public string? InternalId { get; set; }

    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}