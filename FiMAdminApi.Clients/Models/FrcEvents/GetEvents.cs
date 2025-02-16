namespace FiMAdminApi.Clients.Models.FrcEvents;

internal record GetEvents(
    EventResult[] Events);
    
internal record EventResult(
    DateTime DateStart,
    DateTime DateEnd,
    string Timezone,
    string Code,
    string Name,
    string? DistrictCode,
    string City);