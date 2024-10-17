using Asp.Versioning.Builder;
using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.EventSync;
using FiMAdminApi.Services;
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
        eventsGroup.MapPut("{eventId:guid}/teams", SyncEventTeams)
            .WithDescription("Sync single event");
        eventsGroup.MapPut("current", SyncCurrentEvents)
            .WithDescription("Sync all current events");

        return app;
    }

    private static async Task<Results<Ok<EventSyncResult>, NotFound, BadRequest<string>>> SyncSingleEvent([FromRoute] Guid eventId, [FromServices] DataContext context, [FromServices] EventSyncService service)
    {
        var evt = await context.Events.Include(e => e.Season).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null) return TypedResults.NotFound();

        if (string.IsNullOrEmpty(evt.Code) || evt.SyncSource is null)
            return TypedResults.BadRequest("Event does not have a sync source");

        return TypedResults.Ok(await service.SyncEvent(evt));
    }

    private static async Task SyncCurrentEvents()
    {
        throw new NotImplementedException();
    }

    private static async Task<Results<NotFound, Ok, ProblemHttpResult>> SyncEventTeams([FromRoute] Guid eventId, [FromServices] IServiceProvider services, [FromServices] DataContext context)
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