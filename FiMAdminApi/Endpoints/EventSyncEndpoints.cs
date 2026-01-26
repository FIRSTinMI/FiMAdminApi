using System.Collections.Concurrent;
using Asp.Versioning.Builder;
using FiMAdminApi.Auth;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.EventSync;
using FiMAdminApi.Models.Models;
using FiMAdminApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SlackNet;
using File = System.IO.File;

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
        // eventsGroup.MapPut("daily", RunDailySync);

        return app;
    }

    private static async Task<Results<Ok<EventSyncResult>, NotFound, BadRequest<string>>> SyncSingleEvent(
        [FromRoute] Guid eventId,
        [FromServices] DataContext context,
        [FromServices] EventSyncService service)
    {
        var evt = await context.Events.Include(e => e.Season).ThenInclude(s => s!.Level)
            .FirstOrDefaultAsync(e => e.Id == eventId);
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
        var evt = await context.Events.Include(e => e.Season).ThenInclude(s => s!.Level)
            .FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null) return TypedResults.NotFound();

        if (string.IsNullOrEmpty(evt.Code) || evt.SyncSource is null)
            return TypedResults.BadRequest("Event does not have a sync source");

        return TypedResults.Ok(await service.ForceEventSyncStep(evt, syncStepName));
    }

    private static readonly EventStatus[] InactiveStatuses = [EventStatus.NotStarted, EventStatus.Completed];

    private static async Task<Results<Ok<EventSyncResult>, BadRequest<EventSyncResult>>> SyncCurrentEvents(
        [FromServices] DataContext context,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IServiceProvider serviceProvider)
    {
        var events = await context.Events.Include(e => e.Season).ThenInclude(s => s!.Level).Where(e =>
            e.SyncSource != null && (
                (e.StartTime <= DateTime.UtcNow && e.EndTime >= DateTime.UtcNow) ||
                !InactiveStatuses.Contains(e.Status)
            )).ToListAsync();

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
        
        var eventSyncResult = new EventSyncResult(isSuccess, combinedSyncMessages);

        if (!isSuccess)
            return TypedResults.BadRequest(eventSyncResult);
                
        return TypedResults.Ok(eventSyncResult);
    }

    // private static async Task<Ok> RunDailySync([FromServices] DataContext dbContext, [FromServices] SlackService slackService)
    // {
    //     var currentEvents = await dbContext.Events.Where(e =>
    //             e.TruckRouteId != null && e.StartTime.AddDays(-1) < DateTime.UtcNow && e.EndTime > DateTime.UtcNow)
    //         .ToListAsync();
    // }
}   