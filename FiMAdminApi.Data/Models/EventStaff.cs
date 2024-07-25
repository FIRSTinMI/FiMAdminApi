using FiMAdminApi.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Data.Models;

[PrimaryKey(nameof(EventId), nameof(UserId))]
public class EventStaff
{
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }
    public ICollection<EventPermission> Permissions { get; set; } = null!;
}