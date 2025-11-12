using System.ComponentModel.DataAnnotations;

namespace FiMAdminApi.Models.Models;

public class Equipment
{
    [Key]
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public int EquipmentTypeId { get; set; }
    public EquipmentType? EquipmentType { get; set; }
    public int? TruckRouteId { get; set; }
    public TruckRoute? TruckRoute { get; set; }
    public string? SlackUserId { get; set; }
}

public class Equipment<TConfig> : Equipment where TConfig : IEquipmentConfiguration
{
    public TConfig? Configuration { get; set; }
}

public interface IEquipmentConfiguration {}

public class AvConfiguration : IEquipmentConfiguration
{
    public ICollection<StreamInformation>? StreamInfo { get; set; }

    public class StreamInformation
    {
        public required int Index { get; set; }
        public required Guid CartId { get; set; }
        public required bool Enabled { get; set; }
        public required string RtmpUrl { get; set; }
        public required string RtmpKey { get; set; }
    }
}

public class AvCartEquipment : Equipment<AvConfiguration> {}