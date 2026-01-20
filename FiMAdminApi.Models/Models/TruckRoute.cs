using System.ComponentModel.DataAnnotations;
using FiMAdminApi.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Models.Models;

[Owned]
public class StreamingConfig
{
    // ReSharper disable InconsistentNaming
    public string? Channel_Id { get; set; }
    public StreamPlatform Channel_Type { get; set; }
    // ReSharper restore InconsistentNaming
}

public class TruckRoute
{
    [Key]
    public int Id { get; set; }
    
    public required string Name { get; set; }

    public StreamingConfig? StreamingConfig { get; set; }
    
}