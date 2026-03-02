using System.Text.Json.Serialization;

namespace FiMAdminApi.Models.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LogoAndTitleSponsor), typeDiscriminator: nameof(SponsorType.LogoAndTitle))]
[JsonDerivedType(typeof(FullscreenMediaSponsor), typeDiscriminator: nameof(SponsorType.FullscreenMedia))]
[JsonDerivedType(typeof(LocalEventSponsorsSponsor), typeDiscriminator: nameof(SponsorType.LocalEventSponsors))]
public class BaseSponsor
{
    public string Name { get; set; }
    public Guid Uuid { get; set; }
    [JsonIgnore]
    public SponsorType Type { get; set; }
    public bool IsEnabled { get; set; }
}

public class LogoAndTitleSponsor : BaseSponsor {
    public string Title { get; set; }
    public string LogoUrl { get; set; }
}

public class FullscreenMediaSponsor : BaseSponsor
{
    public string MediaUrl { get; set; }
}

public class LocalEventSponsorsSponsor : BaseSponsor
{
}

public enum SponsorType
{
    LogoAndTitle,
    FullscreenMedia,
    LocalEventSponsors
}

public class EventSponsor
{
    public Guid EventId { get; set; }
    public BaseSponsor[] Sponsors { get; set; }
}

public class SeasonSponsor
{
    public int SeasonId { get; set; }
    public BaseSponsor[] Sponsors { get; set; }
}