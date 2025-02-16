using System.Collections.Concurrent;
using System.Text.Json;
using Asp.Versioning.Builder;
using FiMAdminApi.Auth;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Data.Models;
using FiMAdminApi.EventSync;
using Firebase.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Endpoints;

public static class EventSyncEndpoints
{
    public static WebApplication RegisterEventSyncEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var eventsGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/event-sync")
            .WithApiVersionSet(vs).HasApiVersion(1).WithTags("Event Sync")
            .RequireAuthorization(EventSyncAuthHandler.EventSyncAuthScheme);

        eventsGroup.MapPut("{eventId:guid}", SyncSingleEvent)
            .WithDescription("Sync single event");
        eventsGroup.MapPut("{eventId:guid}/force/{syncStepName}", ForceEventSyncStep);
        eventsGroup.MapPut("current", SyncCurrentEvents)
            .WithDescription("Sync all current events");
        eventsGroup.MapPost("firebase-dev", SyncEventsToFirebase);

        return app;
    }

    private static async Task<Results<Ok<EventSyncResult>, NotFound, BadRequest<string>>> SyncSingleEvent(
        [FromRoute] Guid eventId,
        [FromServices] DataContext context,
        [FromServices] EventSyncService service)
    {
        var evt = await context.Events.Include(e => e.Season).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null) return TypedResults.NotFound();

        if (string.IsNullOrEmpty(evt.Code) || evt.SyncSource is null)
            return TypedResults.BadRequest("Event does not have a sync source");

        return TypedResults.Ok(await service.SyncEvent(evt));
    }

    private static async Task<Results<Ok<EventSyncResult>, NotFound, BadRequest<string>>> ForceEventSyncStep(
        [FromRoute] Guid eventId,
        [FromRoute] string syncStepName,
        [FromServices] DataContext context,
        [FromServices] EventSyncService service)
    {
        var evt = await context.Events.Include(e => e.Season).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null) return TypedResults.NotFound();

        if (string.IsNullOrEmpty(evt.Code) || evt.SyncSource is null)
            return TypedResults.BadRequest("Event does not have a sync source");

        return TypedResults.Ok(await service.ForceEventSyncStep(evt, syncStepName));
    }

    private static async Task<Ok<EventSyncResult>> SyncCurrentEvents(
        [FromServices] DataContext context,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IServiceProvider serviceProvider)
    {
        var events = await context.Events.Include(e => e.Season).Where(e =>
            e.SyncSource != null && e.StartTime <= DateTime.UtcNow && e.EndTime >= DateTime.UtcNow).ToListAsync();

        var isSuccess = true;
        var successLock = new Lock();
        var syncMessages = new ConcurrentDictionary<string, string>();
        await Parallel.ForEachAsync(events, new ParallelOptions
        {
            MaxDegreeOfParallelism = 5
        }, async (e, _) =>
        {
            // Put each event in its own scope to avoid using a DB context in multiple threads
            using var scope = serviceProvider.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<EventSyncService>();
            var individualResult = await syncService.SyncEvent(e);
            if (!individualResult.Success)
            {
                loggerFactory.CreateLogger(typeof(EventSyncEndpoints)).LogWarning(
                    "Sync for event {EventCode} failed: {Message}", e.Code ?? e.Id.ToString(),
                    individualResult.Message ?? "No message provided");
                if (individualResult.Message is not null)
                    syncMessages[e.Id.ToString()] = individualResult.Message;
                lock (successLock)
                {
                    isSuccess = false;
                }
            }
        });

        var combinedSyncMessages = syncMessages.Any()
                ? string.Join(Environment.NewLine, syncMessages.Select(kvp => $"{kvp.Key} - {kvp.Value}"))
                : null;
        return TypedResults.Ok(new EventSyncResult(isSuccess, combinedSyncMessages));
    }

    private static async Task<Ok> SyncEventsToFirebase([FromServices] DataContext context, [FromServices] FirebaseClient firebase)
    {
        var events = await context.Events
            .Where(e => e.Season != null && e.Season.Level != null && e.Season.Level.Name == "FRC" &&
                        e.StartTime.Year == DateTime.Now.Year).ToListAsync();

        var carts = await context.Equipment.Where(e => e.EquipmentType != null && e.EquipmentType.Name == "AV Cart")
            .ToListAsync();

        foreach (var evt in events)
        {
            var eventTimezone = TimeZoneInfo.FindSystemTimeZoneById(evt.TimeZone);
            await firebase.Child($"/seasons/{evt.StartTime.Year}/events/{evt.Key}").PutAsync(JsonSerializer.Serialize(
                new
                {
                    eventCode = evt.Code,
                    startMs = new DateTimeOffset(evt.StartTime).ToUnixTimeMilliseconds(),
                    endMs = new DateTimeOffset(evt.EndTime).ToUnixTimeMilliseconds(),
                    start = new DateTimeOffset(TimeZoneInfo.ConvertTimeFromUtc(evt.StartTime, eventTimezone),
                        eventTimezone.GetUtcOffset(evt.StartTime)),
                    end = new DateTimeOffset(TimeZoneInfo.ConvertTimeFromUtc(evt.EndTime, eventTimezone),
                        eventTimezone.GetUtcOffset(evt.EndTime)),
                    state = GetFirebaseStatus(evt.Status),
                    name = evt.Name,
                    streamEmbedLink = (string?)null,
                    dataSource = evt.SyncSource switch
                    {
                        DataSources.FrcEvents => "frcEvents",
                        DataSources.BlueAlliance => "blueAlliance",
                        _ => null
                    },
                    cartId = evt.TruckRouteId is not null
                        ? carts.FirstOrDefault(c => c.TruckRouteId == evt.TruckRouteId)?.Id
                        : null
                }));
        }

        return TypedResults.Ok();

        string GetFirebaseStatus(EventStatus dbStatus)
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
    }
}