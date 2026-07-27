using Asp.Versioning.Builder;
using FiMAdminApi.Clients;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

namespace FiMAdminApi.Endpoints;

public static class TbaWriteEndpoints
{
    public static WebApplication RegisterTbaWriteEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var routeGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/tba-write")
            .WithApiVersionSet(vs).HasApiVersion(1).WithTags("TBA Write")
            .AddEndpointFilter(async (ctx, next) =>
            {
                var authId = ctx.HttpContext.Request.Headers["X-tba-authid"].FirstOrDefault();
                var authSecret = ctx.HttpContext.Request.Headers["X-tba-authsecret"].FirstOrDefault();
                if (!string.IsNullOrEmpty(authId) && !string.IsNullOrEmpty(authSecret))
                {
                    ctx.HttpContext.RequestServices.GetService<BlueAllianceWriteClient>()
                        ?.OverrideAuth(authId, authSecret);
                }
                return await next(ctx);
            }).AddOpenApiOperationTransformer((op, _, _) =>
            {
                op.Parameters ??= [];
                op.Parameters.Add(new OpenApiParameter
                {
                    Name = "X-tba-authid",
                    In = ParameterLocation.Header,
                    Description = "Optional: TBA Write Key Auth ID",
                    AllowEmptyValue = true
                });
                op.Parameters.Add(new OpenApiParameter
                {
                    Name = "X-tba-authsecret",
                    In = ParameterLocation.Header,
                    Description = "Optional: TBA Write Key Auth Secret",
                    AllowEmptyValue = true
                });

                return Task.CompletedTask;
            })
            .RequireAuthorization(nameof(GlobalPermission.Superuser));

        routeGroup.MapPut("{eventId:guid}/videos", AddMatchVideos)
            .WithSummary("Add a video to matches")
            .WithDescription(
                "Add additional videos to the specified matches (key: TBA match key, value: YouTube video ID)");
        routeGroup.MapGet("{eventId:guid}/info", GetEventInfo)
            .WithSummary("Get info");
        routeGroup.MapPost("{eventId:guid}/streams", SetEventStreams)
            .WithSummary("Set streams");
        routeGroup.MapPost("{eventId:guid}/media", AddEventMedia)
            .WithSummary("Add event media");
        
        return app;
    }

    private static async Task<Results<Ok, NotFound>> AddMatchVideos(
        [FromRoute] Guid eventId,
        [FromBody] Dictionary<string, string> request,
        [FromServices] IConfiguration configuration,
        [FromServices] DataContext dbContext,
        [FromServices] BlueAllianceWriteClient writeClient)
    {
        var evt = await dbContext.Events.Include(e => e.Season).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null || string.IsNullOrEmpty(evt.Code)) return TypedResults.NotFound();

        await writeClient.AddMatchVideos(evt.Season!, evt.Code, request);

        return TypedResults.Ok();
    }
    
    private static async Task<Results<Ok<string>, NotFound>> GetEventInfo(
        [FromRoute] Guid eventId,
        [FromServices] DataContext dbContext,
        [FromServices] BlueAllianceWriteClient writeClient)
    {
        var evt = await dbContext.Events.Include(e => e.Season).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null || string.IsNullOrEmpty(evt.Code)) return TypedResults.NotFound();

        var resp = await writeClient.GetEventInfo(evt.Season!, evt.Code);

        return TypedResults.Ok(resp);
    }
    
    private static async Task<Results<Ok, NotFound>> SetEventStreams(
        [FromBody] WebcastInfo[] info,
        [FromRoute] Guid eventId,
        [FromServices] DataContext dbContext,
        [FromServices] BlueAllianceWriteClient writeClient)
    {
        var evt = await dbContext.Events.Include(e => e.Season).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null || string.IsNullOrEmpty(evt.Code)) return TypedResults.NotFound();

        await writeClient.UpdateEventInfo(evt.Season!, evt.Code, info);

        return TypedResults.Ok();
    }
    
    private static async Task<Results<Ok, NotFound>> AddEventMedia(
        [FromBody] string[] videos,
        [FromRoute] Guid eventId,
        [FromQuery] string? eventCode, // Override event code in DB (useful if they don't match)
        [FromServices] DataContext dbContext,
        [FromServices] BlueAllianceWriteClient writeClient)
    {
        var evt = await dbContext.Events.Include(e => e.Season).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null || string.IsNullOrEmpty(evt.Code)) return TypedResults.NotFound();

        await writeClient.AddEventMedia(evt.Season!, string.IsNullOrWhiteSpace(eventCode) ? evt.Code : eventCode, videos);

        return TypedResults.Ok();
    }
}