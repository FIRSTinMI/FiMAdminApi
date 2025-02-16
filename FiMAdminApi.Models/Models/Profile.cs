using System.ComponentModel.DataAnnotations.Schema;

namespace FiMAdminApi.Models.Models;

[Table("profiles")]
public class Profile
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
}