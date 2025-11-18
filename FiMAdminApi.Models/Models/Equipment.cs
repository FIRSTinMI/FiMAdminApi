using System.ComponentModel.DataAnnotations;
using static FiMAdminApi.Models.Models.AvConfiguration;

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

public interface IEquipmentConfiguration { }

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

public class AvCartEquipment : Equipment<AvConfiguration>
{
    public void SetFirstStreamInfo(string rtmpUrl, string streamKey)
    {
        if (Configuration == null)
        {
            Configuration = new AvConfiguration
            {
                StreamInfo = new List<StreamInformation>()
            };
        }
        // verify a streaming config exists
        if (Configuration.StreamInfo == null)
        {
            Configuration.StreamInfo = new List<StreamInformation>();
        }

        // set the first entry to the new stream info
        if (Configuration.StreamInfo.Count == 0)
        {
            Configuration.StreamInfo.Add(new StreamInformation
            {
                Index = 0,
                CartId = Id,
                Enabled = true,
                RtmpUrl = rtmpUrl,
                RtmpKey = streamKey
            });
        }
        else
        {
            var si = Configuration.StreamInfo.First();
            si.RtmpUrl = rtmpUrl;
            si.RtmpKey = streamKey;
            si.Enabled = true;
        }
    }
}