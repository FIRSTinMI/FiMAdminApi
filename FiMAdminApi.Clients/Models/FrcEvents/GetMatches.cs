namespace FiMAdminApi.Clients.Models.FrcEvents;

internal record GetMatches(
    ApiMatchResult[] Matches);
    
internal record ApiMatchResult(
    int MatchNumber,
    string? Description,
    string? MatchVideoLink,
    DateTime? ActualStartTime,
    DateTime? PostResultTime,
    MatchTeam[] Teams,
    int? ScoreRedFinal,
    int? ScoreBlueFinal);