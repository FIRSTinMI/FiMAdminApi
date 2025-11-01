namespace FiMAdminApi.Clients.Models.FtcEvents;

internal record GetAlliances(
    AllianceResult[] Alliances);
    
internal record AllianceResult(
    string Name,
    int? Captain,
    int? Round1,
    int? Round2,
    int? Round3,
    int? Backup,
    int? BackupReplaced
);