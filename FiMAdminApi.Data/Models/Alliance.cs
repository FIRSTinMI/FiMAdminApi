namespace FiMAdminApi.Data.Models;

public class Alliance
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public required string Name { get; set; }
    public int[]? TeamNumbers { get; set; }
}