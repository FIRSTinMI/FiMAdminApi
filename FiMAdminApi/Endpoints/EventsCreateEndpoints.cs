using Asp.Versioning.Builder;
using FiMAdminApi.Clients;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Data.Models;
using FiMAdminApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FiMAdminApi.Endpoints;

public static class EventsCreateEndpoints
{
    public static WebApplication RegisterEventsCreateEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var eventsCreateGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/events-create")
            .WithApiVersionSet(vs).HasApiVersion(1).WithTags("Events - Create")
            .RequireAuthorization(nameof(GlobalPermission.Events_Create));

        eventsCreateGroup.MapPost("sync-source", SyncSource)
            .WithSummary("Create from Sync Source")
            .WithDescription(
                "Will return OK if operation is fully successful, or BadRequest if it contains any errors");
        
        return app;
    }

    private static async Task<Results<Ok<UpsertEventsService.UpsertEventsResponse>, BadRequest<UpsertEventsService.UpsertEventsResponse>>> SyncSource(
        [FromBody] UpsertEventsService.UpsertFromDataSourceRequest request,
        [FromServices] UpsertEventsService service)
    {
        var resp = await service.UpsertFromDataSource(request);

        return resp.Errors.Count == 0 ? TypedResults.Ok(resp) : TypedResults.BadRequest(resp);
    }
}