using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;

namespace FiMAdminApi.Models.Helpers;

public static class PlayoffHelpers
{
    public static MatchWinner? GetHeadToHeadWinner(IEnumerable<Match> matches, int winsRequired = 2)
    {
        var matchesArr = matches as Match[] ?? matches.ToArray();
        if (matchesArr.Length < winsRequired) return null;
        
        var winCounts = matchesArr.GroupBy(m => m.Winner);

        MatchWinner? overallWinner = null;
        foreach (var winCount in winCounts)
        {
            if (winCount.Key is null or MatchWinner.TrueTie) continue;

            if (winCount.Count() >= winsRequired)
            {
                if (overallWinner is not null)
                {
                    throw new ArgumentException("Multiple alliances meet win requirements");
                }

                overallWinner = winCount.Key;
            }
        }

        return overallWinner;
    }

    public static IEnumerable<Match> GetPlayoffFinalsMatches(IEnumerable<Match> matches)
    {
        return matches.Where(m => m.MatchName is not null &&
                                  (m.MatchName.StartsWith("Final", StringComparison.InvariantCultureIgnoreCase) ||
                                   m.MatchName.StartsWith("Overtime", StringComparison.InvariantCultureIgnoreCase)));
    }
}