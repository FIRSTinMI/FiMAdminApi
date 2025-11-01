using FiMAdminApi.Clients.Models;
using FiMAdminApi.Models.Enums;

namespace FiMAdminApi.Clients.PlayoffTiebreaks;

public class NoopPlayoffTiebreak : IPlayoffTiebreak
{
    public Task<MatchWinner?> DetermineMatchWinner(PlayoffMatch match)
    {
        return Task.FromResult((MatchWinner?)null);
    }
}