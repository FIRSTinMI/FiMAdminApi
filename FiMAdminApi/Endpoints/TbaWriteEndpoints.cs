using Asp.Versioning.Builder;
using FiMAdminApi.Clients;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Endpoints;

public static class TbaWriteEndpoints
{
    public static WebApplication RegisterTbaWriteEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var routeGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/tba-write")
            .WithApiVersionSet(vs).HasApiVersion(1).WithTags("TBA Write")
            .RequireAuthorization(nameof(GlobalPermission.Superuser));

        routeGroup.MapPut("{eventId:guid}/videos", AddMatchVideos)
            .WithSummary("Add a video to matches")
            .WithDescription(
                "Add additional videos to the specified matches (key: TBA match key, value: YouTube video ID)");
        
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
}