using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FiMAdminApi.Models.Models;

[Table("levels")]
public class Level
{
    [Key]
    public int Id { get; set; }
    public required string Name { get; set; }
}