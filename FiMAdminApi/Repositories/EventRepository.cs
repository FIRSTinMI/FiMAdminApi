using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Models;
using Firebase.Database;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Repositories;

public partial class EventRepository(DataContext dbContext, FirebaseClient firebaseClient, IConfiguration config)
{
    public async Task<Event> UpdateEvent(Event evt, bool saveChanges = true)
    {
        dbContext.Update(evt);

        Debug.Assert(evt.Season?.Level is not null, "EventRepository.UpdateEvent must be called with Level populated");

        if (evt.Season?.Level?.Name == "FRC")
        {
            // We need to update FRC events in Firebase too
            var fbRef = firebaseClient.Child($"/seasons/{GetFirebaseSeason(evt)}/events/{evt.Key}");
            
            var eventTimezone = TimeZoneInfo.FindSystemTimeZoneById(evt.TimeZone);
            var cart = evt.TruckRouteId is not null
                ? dbContext.Equipment
                    .FirstOrDefault(e => e.TruckRouteId == evt.TruckRouteId && e.EquipmentType!.Name == "AV Cart")
                : null;
            string? streamUrl = null;

            var urlTemplate = config["EventStream:TwitchEmbedTemplate"];
            if (!string.IsNullOrEmpty(urlTemplate) && evt.TruckRouteId is not null)
            {
                var routeName = await dbContext.TruckRoutes.Where(r => r.Id == evt.TruckRouteId).Select(r => r.Name)
                    .FirstOrDefaultAsync();
                if (routeName is not null)
                {
                    var routeNumberMatch = RouteNumberRegex.Match(routeName);
                    if (routeNumberMatch.Success)
                    {
                        streamUrl = string.Format(urlTemplate, routeNumberMatch.Value);
                    }
                }
            }

            await fbRef.PatchAsync(JsonSerializer.Serialize(new
            {
                startMs = new DateTimeOffset(evt.StartTime).ToUnixTimeMilliseconds(),
                endMs = new DateTimeOffset(evt.EndTime).ToUnixTimeMilliseconds(),
                start = new DateTimeOffset(TimeZoneInfo.ConvertTimeFromUtc(evt.StartTime, eventTimezone),
                    eventTimezone.GetUtcOffset(evt.StartTime)),
                end = new DateTimeOffset(TimeZoneInfo.ConvertTimeFromUtc(evt.EndTime, eventTimezone),
                    eventTimezone.GetUtcOffset(evt.EndTime)),
                state = GetFirebaseStatus(evt.Status),
                name = evt.Name,
                cartId = cart?.Id,
                streamEmbedLink = streamUrl
            }));
        }

        if (saveChanges) await dbContext.SaveChangesAsync();
        
        return evt;
    }

    private static string GetFirebaseSeason(Event evt)
    {
        Debug.Assert(evt.Season is not null);
        return evt.Season.StartTime.Year.ToString();
    }
    
    private static string GetFirebaseStatus(EventStatus dbStatus)
    {
        return dbStatus switch
        {
            EventStatus.NotStarted => "Pending",
            EventStatus.AwaitingQuals => "AwaitingQualSchedule",
            EventStatus.QualsInProgress => "QualsInProgress",
            EventStatus.AwaitingAlliances => "AwaitingAlliances",
            EventStatus.AwaitingPlayoffs => "PlayoffsInProgress",
            EventStatus.PlayoffsInProgress => "PlayoffsInProgress",
            EventStatus.WinnerDetermined => "EventOver",
            EventStatus.Completed => "EventOver",
            _ => throw new ArgumentOutOfRangeException(nameof(dbStatus), dbStatus, null)
        };
    }

    [GeneratedRegex(@"(?<num>\d+)$")]
    private partial Regex RouteNumberRegex { get; }
}