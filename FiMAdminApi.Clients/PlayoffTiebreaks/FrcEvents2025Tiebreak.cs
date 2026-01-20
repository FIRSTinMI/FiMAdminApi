using FiMAdminApi.Clients.Models;
using FiMAdminApi.Models.Enums;
using Event = FiMAdminApi.Models.Models.Event;

namespace FiMAdminApi.Clients.PlayoffTiebreaks;

internal class FrcEvents2025Tiebreak : IPlayoffTiebreak
{
    private static readonly string[] ValidSeasons = ["2025", "2026"];
    public FrcEvents2025Tiebreak(FrcEventsDataClient dataClient, Event evt)
    {
        if (evt.Season is null) throw new ApplicationException("Season not provided");
        if (!ValidSeasons.Contains(FrcEventsDataClient.GetSeason(evt.Season)))
            throw new ApplicationException(
                $"Unable to process tiebreaks for an event in the {FrcEventsDataClient.GetSeason(evt.Season)} season");
        _scoreDetails =
            new Lazy<Task<ScoreDetailResponse?>>(() => dataClient.GetPlayoffScoreDetails<ScoreDetailResponse>(evt)!);
    }
    
    private readonly Lazy<Task<ScoreDetailResponse?>> _scoreDetails;
    
    public async Task<MatchWinner?> DetermineMatchWinner(PlayoffMatch match)
    {
        var details = await _scoreDetails.Value;
        var matchDetails = details?.MatchScores.FirstOrDefault(m => m.MatchNumber == match.MatchNumber);
        if (matchDetails is null)
            return null;
        if (matchDetails.WinningAlliance == AllianceType.Blue) return MatchWinner.Blue;
        if (matchDetails.WinningAlliance == AllianceType.Red) return MatchWinner.Red;
        if (matchDetails.Tiebreaker.tiebreakType == PlayoffTiebreakType.TrueTie) return MatchWinner.TrueTie;

        throw new ApplicationException(
            $"Unable to determine winner for {match.MatchName ?? match.MatchNumber.ToString()}");
    }

    internal class ScoreDetailResponse
    {
        public required ScoreDetailMatch[] MatchScores { get; set; }
    }

    internal class ScoreDetailMatch
    {
        public required int MatchNumber { get; set; }
        public AllianceType? WinningAlliance { get; set; }
        public required (
            PlayoffTiebreakType tiebreakType /* called item1 in api */,
            string tiebreakReason /* called item2 in api */) Tiebreaker { get; set; }
    }
}