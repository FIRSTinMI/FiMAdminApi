namespace FiMAdminApi.Clients.Models.FtcEvents;

internal record GetAlliances(
    AllianceResult[] Alliances);
    
internal record AllianceResult(
    string Name,
    AllianceTeam? Captain,
    AllianceTeam? Round1,
    AllianceTeam? Round2,
    AllianceTeam? Round3,
    AllianceTeam? Backup,
    int? BackupReplaced
);

internal record AllianceTeam(
    int TeamNumber,
    string DisplayTeamNumber,
    string TeamName);