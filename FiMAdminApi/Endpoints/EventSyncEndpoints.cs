using Asp.Versioning.Builder;
using FiMAdminApi.Auth;
using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.EventSync;
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
        eventsGroup.MapPut("{eventId:guid}/teams", SyncEventTeams)
            .WithDescription("Sync single event");
        eventsGroup.MapPut("current", SyncCurrentEvents)
            .WithDescription("Sync all current events");

        return app;
    }

    private static async Task<Results<Ok<EventSyncResult>, NotFound, BadRequest<string>>> SyncSingleEvent(
        [FromRoute] Guid eventId, [FromServices] DataContext context, [FromServices] EventSyncService service)
    {
        var evt = await context.Events.Include(e => e.Season).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null) return TypedResults.NotFound();

        if (string.IsNullOrEmpty(evt.Code) || evt.SyncSource is null)
            return TypedResults.BadRequest("Event does not have a sync source");

        return TypedResults.Ok(await service.SyncEvent(evt));
    }
    
    private static async Task<Results<Ok<EventSyncResult>, NotFound, BadRequest<string>>> ForceEventSyncStep(
        [FromRoute] Guid eventId, [FromRoute] string syncStepName, [FromServices] DataContext context, [FromServices] EventSyncService service)
    {
        var evt = await context.Events.Include(e => e.Season).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null) return TypedResults.NotFound();

        if (string.IsNullOrEmpty(evt.Code) || evt.SyncSource is null)
            return TypedResults.BadRequest("Event does not have a sync source");

        return TypedResults.Ok(await service.SyncEvent(evt));
    }

    private static async Task<Ok<EventSyncResult>> SyncCurrentEvents([FromServices] DataContext context,
        [FromServices] EventSyncService syncService, [FromServices] ILoggerFactory loggerFactory)
    {
        var events = context.Events.Include(e => e.Season).Where(e =>
            e.SyncSource != null && e.StartTime <= DateTime.UtcNow && e.EndTime >= DateTime.UtcNow).AsAsyncEnumerable();

        var isSuccess = true;
        var successLock = new Lock();
        await Parallel.ForEachAsync(events, new ParallelOptions
        {
            MaxDegreeOfParallelism = 5
        }, async (e, _) =>
        {
            var individualResult = await syncService.SyncEvent(e);
            if (!individualResult.Success)
            {
                loggerFactory.CreateLogger(typeof(EventSyncEndpoints)).LogWarning(
                    "Sync for event {EventCode} failed: {Message}", e.Code ?? e.Id.ToString(),
                    individualResult.Message ?? "No message provided");
                lock (successLock)
                {
                    isSuccess = false;
                }
            }
        });

        return TypedResults.Ok(new EventSyncResult(isSuccess));
    }

    private static async Task<Results<NotFound, Ok, ProblemHttpResult>> SyncEventTeams([FromRoute] Guid eventId,
        [FromServices] IServiceProvider services, [FromServices] DataContext context)
    {
        var evt = await context.Events.Include(e => e.Season).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null) return TypedResults.NotFound();

        if (string.IsNullOrEmpty(evt.Code) || evt.SyncSource is null)
            return TypedResults.Problem("Event does not have a sync source",
                statusCode: StatusCodes.Status400BadRequest);

        var dataClient = services.GetRequiredKeyedService<IDataClient>(evt.SyncSource);

        // TODO: Do something with this data
        await dataClient.GetTeamsForEvent(evt.Season!, evt.Code);

        return TypedResults.Ok();
    }
}