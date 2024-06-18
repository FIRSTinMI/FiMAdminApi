namespace FiMAdminApi.Clients.Models;

public class Event
{
    public string EventCode { get; set; }
    public string Name { get; set; }
    public string? DistrictCode { get; set; }
    public string City { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}