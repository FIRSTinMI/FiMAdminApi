using FiMAdminApi.Clients.Models;
using FiMAdminApi.Data.Enums;

namespace FiMAdminApi.Clients.PlayoffTiebreaks;

public interface IPlayoffTiebreak
{
    public Task<MatchWinner> DetermineMatchWinner(PlayoffMatch match);
}