namespace FiMAdminApi.Clients.Models;

public class Alliance
{
    public required string Name { get; set; }
    public required List<int> TeamNumbers { get; set; }
}