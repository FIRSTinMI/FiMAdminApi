namespace FiMAdminApi.Clients.Models.BlueAlliance;

internal record GetMatches(CompLevel CompLevel, int SetNumber, int MatchNumber, long? ActualTime, long? PostResultTime, MatchVideo[] Videos);

internal enum CompLevel
{
    Qm,
    Ef,
    Qf,
    Sf,
    F
}

internal record MatchVideo(MatchVideoType Type, string Key);

internal enum MatchVideoType
{
    Youtube,
    Tba
}