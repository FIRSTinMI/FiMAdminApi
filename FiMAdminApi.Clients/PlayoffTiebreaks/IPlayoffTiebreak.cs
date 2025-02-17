using FiMAdminApi.Clients.Models;
using FiMAdminApi.Models.Enums;

namespace FiMAdminApi.Clients.PlayoffTiebreaks;

public interface IPlayoffTiebreak
{
    public Task<MatchWinner> DetermineMatchWinner(PlayoffMatch match);
}