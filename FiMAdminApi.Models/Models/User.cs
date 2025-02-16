using FiMAdminApi.Models.Enums;

namespace FiMAdminApi.Models.Models;

public class User
{
    public Guid? Id { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public List<GlobalPermission>? GlobalPermissions { get; set; }
}