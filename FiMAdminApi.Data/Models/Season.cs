using System.ComponentModel.DataAnnotations;

namespace FiMAdminApi.Data.Models;

public class Season
{
    [Key]
    public int Id { get; set; }
    public required int LevelId { get; set; }
    public Level? Level { get; set; }
    public required string Name { get; set; }
    public required DateTime StartTime { get; set; }
    public required DateTime EndTime { get; set; }
}