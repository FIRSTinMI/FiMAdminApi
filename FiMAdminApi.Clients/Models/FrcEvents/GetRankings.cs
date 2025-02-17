namespace FiMAdminApi.Clients.Models.FrcEvents;

internal record GetRankings(
    RankingResult[] Rankings);

internal record RankingResult(
    int Rank,
    int TeamNumber,
    int SortOrder1,
    int SortOrder2,
    int SortOrder3,
    int SortOrder4,
    int SortOrder5,
    int SortOrder6,
    int Wins,
    int Ties,
    int Losses,
    double QualAverage,
    int Dq,
    int MatchesPlayed);