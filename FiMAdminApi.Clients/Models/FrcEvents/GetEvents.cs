namespace FiMAdminApi.Clients.Models.FrcEvents;

internal record GetEvents(
    EventResult[] Events);

internal record EventResultWebcast(
    string Link,
    string Provider,
    string Channel,
    string? Slug,
    bool IsFirstWebcastUnit,
    DateOnly? Date);

internal record EventResult(
    DateTime DateStart,
    DateTime DateEnd,
    string Timezone,
    string Code,
    string Name,
    string? DistrictCode,
    string City,
    EventResultWebcast[] Webcasts);