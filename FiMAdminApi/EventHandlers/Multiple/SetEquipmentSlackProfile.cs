using System.Text.RegularExpressions;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Events;
using FiMAdminApi.Models.Models;
using FiMAdminApi.Services;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.EventHandlers.Multiple;

public class SetEquipmentSlackProfile(DataContext dbContext, SlackService slackService) : IEventHandler<EventStarted>, IEventHandler<EventCompleted>
{
    public async Task Handle(EventStarted evt)
    {
        if (evt.Event.TruckRouteId is null) return;
        var equipment = await dbContext.Equipment
            .Where(e => e.TruckRouteId == evt.Event.TruckRouteId && e.SlackUserId != null).ToListAsync();

        if (equipment.Count == 0) return;

        var name = CleanEventName(evt.Event.Name);
        foreach (var eq in equipment)
        {
            var newName = $"{GetEquipmentName(eq)} [{name}]";
            await slackService.SetEventInformationForUser(eq.SlackUserId!, newName);
        }
    }
    
    public async Task Handle(EventCompleted evt)
    {
        if (evt.Event.TruckRouteId is null) return;
        var equipment = await dbContext.Equipment
            .Where(e => e.TruckRouteId == evt.Event.TruckRouteId && e.SlackUserId != null).ToListAsync();

        if (equipment.Count == 0) return;

        foreach (var eq in equipment)
        {
            var newName = $"{GetEquipmentName(eq)}";
            await slackService.SetEventInformationForUser(eq.SlackUserId!, newName);
        }
    }

    private static string CleanEventName(string name)
    {
        name = name.Replace(" Qualifier", "");

        name = SetEquipmentSlackProfileRegexes.PresentedBy().Replace(name, string.Empty);
        
        var fimDistrictMatch = SetEquipmentSlackProfileRegexes.FimDistrict().Match(name);
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
        var fimChampMatch = SetEquipmentSlackProfileRegexes.FimChampDivision().Match(name);
        if (fimChampMatch.Success)
        {
            name = $"MSC - {fimChampMatch.Groups[1].Value}";
        }

        name = SetEquipmentSlackProfileRegexes.SpecialCharactersRegex().Replace(name, "");

        return name;
    }

    private static string GetEquipmentName(Equipment eq)
    {
        var numberMatch = SetEquipmentSlackProfileRegexes.EquipmentNumber().Match(eq.Name);
        return eq.EquipmentTypeId switch
        {
            1 => $"FIM AV {numberMatch.Groups[1].Value}",
            _ => eq.Name
        };
    }
}

internal partial class SetEquipmentSlackProfileRegexes
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
