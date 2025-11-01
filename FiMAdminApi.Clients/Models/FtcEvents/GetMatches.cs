namespace FiMAdminApi.Clients.Models.FtcEvents;

internal record GetMatches(
    ApiMatchResult[] Matches);
    
internal record ApiMatchResult(
    int Series,
    int MatchNumber,
    string? Description,
    DateTime? ActualStartTime,
    DateTime? PostResultTime,
    MatchTeam[] Teams,
    int? ScoreRedFinal,
    int? ScoreBlueFinal);