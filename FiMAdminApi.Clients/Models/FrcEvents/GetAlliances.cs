namespace FiMAdminApi.Clients.Models.FrcEvents;

internal record GetAlliances(
    AllianceResult[] Alliances);
    
internal record AllianceResult(
    string Name,
    int? Captain,
    int? Round1,
    int? Round2,
    int? Round3,
    int? Backup
    // unclear what the "backupReplaced" property does or its type, leaving it out for now
);