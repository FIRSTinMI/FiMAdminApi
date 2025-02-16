using System.Text.Json;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Data.Enums;
using Event = FiMAdminApi.Data.Models.Event;

namespace FiMAdminApi.Clients.PlayoffTiebreaks;

public class FrcEvents2025Tiebreak : IPlayoffTiebreak
{
    public FrcEvents2025Tiebreak(FrcEventsDataClient dataClient, Event evt)
    {
        if (evt.Season is null) throw new ApplicationException("Season not provided");
        if (FrcEventsDataClient.GetSeason(evt.Season) != "2025")
            throw new ApplicationException(
                $"Unable to process tiebreaks for an event in the {FrcEventsDataClient.GetSeason(evt.Season)} season");
        _scoreDetails = new Lazy<Task<ScoreDetailResponse?>>(() => dataClient.GetPlayoffScoreDetails(evt).ContinueWith(el =>
            el.Result.Deserialize<ScoreDetailResponse>(JsonSerializerOptions.Web)));
    }
    
    private Lazy<Task<ScoreDetailResponse?>> _scoreDetails;
    
    public async Task<MatchWinner> DetermineMatchWinner(PlayoffMatch match)
    {
        var details = await _scoreDetails.Value;
        var matchDetails = details?.MatchScores.FirstOrDefault(m => m.MatchNumber == match.MatchNumber);
        if (matchDetails is null)
            throw new ApplicationException(
                $"Unable to get score details for match {match.MatchName ?? match.MatchNumber.ToString()}");
        if (matchDetails.WinningAlliance == AllianceType.Blue) return MatchWinner.Blue;
        if (matchDetails.WinningAlliance == AllianceType.Red) return MatchWinner.Red;
        if (matchDetails.Tiebreaker.tiebreakType == PlayoffTiebreakType.TrueTie) return MatchWinner.TrueTie;

        throw new ApplicationException(
            $"Unable to determine winner for {match.MatchName ?? match.MatchNumber.ToString()}");
    }

    private class ScoreDetailResponse
    {
        public required ScoreDetailMatch[] MatchScores { get; set; }
    }

    private class ScoreDetailMatch
    {
        public required int MatchNumber { get; set; }
        public required AllianceType WinningAlliance { get; set; }
        public required (PlayoffTiebreakType tiebreakType, string tiebreakReason) Tiebreaker { get; set; }
    }
}