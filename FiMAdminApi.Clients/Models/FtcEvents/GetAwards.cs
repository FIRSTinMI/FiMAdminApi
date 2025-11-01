namespace FiMAdminApi.Clients.Models.FtcEvents;

internal record GetAwards(
    AwardResult[] Awards);
    
internal record AwardResult(
    string Name,
    int? TeamNumber);