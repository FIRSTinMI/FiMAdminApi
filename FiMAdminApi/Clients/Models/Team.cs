namespace FiMAdminApi.Clients.Models;

public class Team
{
    public int TeamNumber { get; set; }
    public required string Nickname { get; set; }
    public required string FullName { get; set; }
    public required string City { get; set; }
    public required string StateProvince { get; set; }
    public required string Country { get; set; }
}