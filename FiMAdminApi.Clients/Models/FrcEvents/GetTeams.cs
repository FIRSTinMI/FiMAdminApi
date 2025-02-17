namespace FiMAdminApi.Clients.Models.FrcEvents;

internal record GetTeams(
    TeamResult[] Teams);

internal record TeamResult(
    int TeamNumber,
    string NameShort,
    string NameFull,
    string City,
    string StateProv,
    string Country);