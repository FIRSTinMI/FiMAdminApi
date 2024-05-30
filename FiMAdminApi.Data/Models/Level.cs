using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace FiMAdminApi.Data.Models;

[Table("levels")]
public class Level : BaseModel
{
    [PrimaryKey("id")]
    public int Id { get; set; }
    [Column("name")]
    public string Name { get; set; }
}