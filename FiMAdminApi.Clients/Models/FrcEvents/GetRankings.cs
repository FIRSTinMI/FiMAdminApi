namespace FiMAdminApi.Clients.Models.FrcEvents;

internal record GetRankings(
    RankingResult[] Rankings);

internal record RankingResult(
    int Rank,
    int TeamNumber,
    double SortOrder1,
    double SortOrder2,
    double SortOrder3,
    double SortOrder4,
    double SortOrder5,
    double SortOrder6,
    int Wins,
    int Ties,
    int Losses,
    double QualAverage,
    int Dq,
    int MatchesPlayed);