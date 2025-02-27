using System.ComponentModel;

namespace FiMAdminApi.Clients.PlayoffTiebreaks;

// source: uhhhhhhhhhh it fell off the back of a truck

internal enum AllianceType
{
    None = 0,
    Red = 1,
    Blue = 2,
}

internal enum PlayoffTiebreakType
{
    // This is also the tiebreak type when there was no tie to break
    [Description("Unknown")] Unknown = -1, // 0xFFFFFFFF
    [Description("True Tie")] TrueTie = 0,
    [Description("Tiebreak Sort Order 1")] TieBreakSortOrder1 = 1,
    [Description("Tiebreak Sort Order 2")] TieBreakSortOrder2 = 2,
    [Description("Tiebreak Sort Order 3")] TieBreakSortOrder3 = 3,
    [Description("Tiebreak Sort Order 4")] TieBreakSortOrder4 = 4,
    [Description("Tiebreak Sort Order 5")] TieBreakSortOrder5 = 5,
    [Description("Tiebreak Sort Order 6")] TieBreakSortOrder6 = 6,
}