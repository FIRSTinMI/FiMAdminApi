using System.Security.Claims;
using Asp.Versioning.Builder;
using FiMAdminApi.Auth;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Data.Firebase;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;
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
        matchesGroup.MapPost("/{eventId:guid:required}/deviations", AddScheduleDeviation);

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
    
    private static async Task<Results<Ok<ScheduleDeviation>, BadRequest, ForbidHttpResult, NotFound>> AddScheduleDeviation([FromRoute] Guid eventId,
        [FromBody] AddScheduleDeviationRequest request, [FromServices] DataContext dataContext,
        [FromServices] IAuthorizationService authSvc, [FromServices] FrcFirebaseRepository firebase, ClaimsPrincipal user)
    {
        var authResult = await authSvc.AuthorizeAsync(user, eventId, new EventAuthorizationRequirement
        {
            NeededEventPermission = EventPermission.Event_ManageTeams,
            NeededGlobalPermission = GlobalPermission.Events_Manage
        });
        if (!authResult.Succeeded) return TypedResults.Forbid();
        
        var afterMatch = await dataContext.Matches.FirstOrDefaultAsync(m => m.EventId == eventId && m.Id == request.AfterMatchId);
        if (afterMatch is null) return TypedResults.NotFound();
        var associatedMatch = (Match?)null;
        if (request.PreviousMatchId is not null)
        {
            var previousMatch =
                await dataContext.Matches.FirstOrDefaultAsync(m =>
                    m.EventId == eventId && m.Id == request.PreviousMatchId);
            if (previousMatch is null) return TypedResults.NotFound();

            var maxPlay = await dataContext.Matches.Where(m =>
                m.EventId == eventId && m.TournamentLevel == previousMatch.TournamentLevel &&
                m.MatchNumber == previousMatch.MatchNumber).MaxAsync(m => m.PlayNumber);
            
            associatedMatch = new Match
            {
                EventId = previousMatch.EventId,
                TournamentLevel = previousMatch.TournamentLevel,
                MatchNumber = previousMatch.MatchNumber,
                PlayNumber = (maxPlay ?? 0) + 1,
                RedAllianceTeams = previousMatch.RedAllianceTeams,
                BlueAllianceTeams = previousMatch.BlueAllianceTeams,
                RedAllianceId = previousMatch.RedAllianceId,
                BlueAllianceId = previousMatch.BlueAllianceId,
                ScheduledStartTime = null,
                ActualStartTime = null,
                PostResultTime = null,
                IsDiscarded = false
            };
            dataContext.Matches.Add(associatedMatch);
        }

        var newDeviation = new ScheduleDeviation
        {
            EventId = eventId,
            Description = request.Description,
            AfterMatchId = afterMatch.Id,
            AssociatedMatch = associatedMatch
        };
        dataContext.ScheduleDeviations.Add(newDeviation);

        await dataContext.SaveChangesAsync();

        var evt = await dataContext.Events.Include(e => e.Season).ThenInclude(s => s.Level)
            .FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null) return TypedResults.Ok(newDeviation);

        if (afterMatch.TournamentLevel == TournamentLevel.Qualification)
        {
            var qualMatches = await dataContext.Matches
                .Where(m => m.EventId == eventId && m.TournamentLevel == TournamentLevel.Qualification).ToListAsync();
            var deviations = await dataContext.ScheduleDeviations.Where(d => d.EventId == eventId).ToListAsync();
            await firebase.UpdateEventQualMatches(evt, qualMatches, deviations);
        }

        return TypedResults.Ok(newDeviation);
    }

    public record AddScheduleDeviationRequest(int AfterMatchId, string Description, int? PreviousMatchId);
}