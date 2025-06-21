namespace FiMAdminApi.Clients.Models.FrcEvents;

internal record GetAwards(
    AwardResult[] Awards);
    
internal record AwardResult(
    string Name,
    int? TeamNumber);