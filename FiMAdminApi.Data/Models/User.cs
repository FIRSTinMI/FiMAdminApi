namespace FiMAdminApi.Data.Models;

public class User
{
    public Guid? Id { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public List<GlobalRole>? GlobalRoles { get; set; }
}