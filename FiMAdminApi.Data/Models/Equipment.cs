using System.ComponentModel.DataAnnotations;

namespace FiMAdminApi.Data.Models;

public class Equipment
{
    [Key]
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public int EquipmentTypeId { get; set; }
    public EquipmentType? EquipmentType { get; set; }
    public int? TruckRouteId { get; set; }
    public TruckRoute? TruckRoute { get; set; }
}