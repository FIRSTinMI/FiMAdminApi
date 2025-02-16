namespace FiMAdminApi.Clients.Models;

public class Event
{
    public required string EventCode { get; set; }
    public required string Name { get; set; }
    public string? DistrictCode { get; set; }
    public required string City { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public required TimeZoneInfo TimeZone { get; set; }
}