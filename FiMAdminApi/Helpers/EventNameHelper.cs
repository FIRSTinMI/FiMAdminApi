using System.Text.RegularExpressions;

namespace FiMAdminApi.Helpers;

public static class EventNameHelper
{
    public static string CleanEventName(string name)
    {
        name = name.Replace(" Qualifier", "");

        name = EventNameHelperRegexes.PresentedBy().Replace(name, string.Empty);
        
        var fimDistrictMatch = EventNameHelperRegexes.FimDistrict().Match(name);
        if (fimDistrictMatch.Success)
        {
            name = fimDistrictMatch.Groups[1].Value;
            if (fimDistrictMatch.Groups[2].Success && !string.IsNullOrWhiteSpace(fimDistrictMatch.Groups[2].Value))
            {
                // E.g., the "#2" in "...Troy Event #2 presented by..."
                name += $" {fimDistrictMatch.Groups[2].Value}";
                name = name.Trim();
            }
        }
        var fimChampMatch = EventNameHelperRegexes.FimChampDivision().Match(name);
        if (fimChampMatch.Success)
        {
            name = $"MSC - {fimChampMatch.Groups[1].Value}";
        }

        name = EventNameHelperRegexes.SpecialCharactersRegex().Replace(name, "");

        return name;
    }
}

internal partial class EventNameHelperRegexes
{
    [GeneratedRegex("presented by (?:.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex PresentedBy();
    
    [GeneratedRegex("^FIM District (.*) Event(.*)?")]
    public static partial Regex FimDistrict();
    
    [GeneratedRegex("^(?:FIRST in )?Michigan State Championship ?- ?(.*) Division$")]
    public static partial Regex FimChampDivision();

    [GeneratedRegex(@"[^a-zA-Z0-9\s\(\)\-]")]
    public static partial Regex SpecialCharactersRegex();

    [GeneratedRegex("(\\d+)$")]
    public static partial Regex EquipmentNumber();
}