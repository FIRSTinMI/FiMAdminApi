using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Models.Models;

[Owned]
public class StreamingConfig
{
    public string? Channel_Id { get; set; }
    public string? Channel_Type { get; set; }
}

public class TruckRoute
{
    [Key]
    public int Id { get; set; }
    
    public required string Name { get; set; }

    public StreamingConfig? StreamingConfig { get; set; }
    
}