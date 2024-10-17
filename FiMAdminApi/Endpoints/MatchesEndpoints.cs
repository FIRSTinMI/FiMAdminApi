using System.Security.Claims;
using Asp.Versioning.Builder;
using FiMAdminApi.Auth;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Endpoints;

public static class MatchesEndpoints
{
    public static WebApplication RegisterMatchesEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var matchesGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/matches")
            .WithTags("Matches").WithApiVersionSet(vs).HasApiVersion(1).RequireAuthorization();

        matchesGroup.MapPut("/{id:long:required}/is-discarded", UpdateIsDiscarded);

        return app;
    }

    private static async Task<Results<Ok, BadRequest, ForbidHttpResult>> UpdateIsDiscarded([FromRoute] long id,
        [FromBody] bool isDiscarded, [FromServices] DataContext dataContext,
        [FromServices] IAuthorizationService authSvc, ClaimsPrincipal user)
    {
        var match = await dataContext.Matches.FirstOrDefaultAsync(m => m.Id == id);

        if (match is null) return TypedResults.BadRequest();

        var authResult = await authSvc.AuthorizeAsync(user, match.EventId, new EventAuthorizationRequirement
        {
            NeededEventPermission = EventPermission.Event_ManageTeams,
            NeededGlobalPermission = GlobalPermission.Events_Manage
        });
        if (!authResult.Succeeded) return TypedResults.Forbid();

        match.IsDiscarded = isDiscarded;
        await dataContext.SaveChangesAsync();

        return TypedResults.Ok();
    }
}