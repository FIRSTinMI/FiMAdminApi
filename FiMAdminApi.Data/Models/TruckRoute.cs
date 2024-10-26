using System.ComponentModel.DataAnnotations;

namespace FiMAdminApi.Data.Models;

public class TruckRoute
{
    [Key]
    public int Id { get; set; }
    
    public required string Name { get; set; }
}