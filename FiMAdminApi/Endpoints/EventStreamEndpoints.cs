using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FiMAdminApi.Models.Enums;

namespace FiMAdminApi.Endpoints;

public static class EventStreamEndpoints
{
    public static WebApplication RegisterEventStreamEndpoints(this WebApplication app, Asp.Versioning.Builder.ApiVersionSet vs)
    {
        var group = app.MapGroup("/api/v{apiVersion:apiVersion}/event-streams")
            .WithApiVersionSet(vs).HasApiVersion(1).WithTags("EventStreams")
            .RequireAuthorization(nameof(GlobalPermission.Superuser));

        group.MapPost("", CreateEventStreams)
            .WithDescription("Create or update event streams for a set of events");

        group.MapDelete("/{id:long}", DeleteEventStream)
            .WithDescription("Delete a created event stream (Twitch rename or YouTube deletion)");

        return app;
    }

    private static async Task<Results<Ok, NotFound, ProblemHttpResult>> DeleteEventStream(
        [FromRoute] long id,
        [FromServices] EventStreamService streamService,
        [FromServices] DataContext dbContext)
    {
        if (dbContext.EventStreams == null)
        {
            return TypedResults.Problem("EventStreams DbSet unavailable");
        }

        var exists = await dbContext.EventStreams.AnyAsync(s => s.Id == id);
        if (!exists) return TypedResults.NotFound();

        var success = await streamService.DeleteLiveEventAsync(id);
        if (success) return TypedResults.Ok();

        return TypedResults.Problem("Failed to delete live event. See server logs for details.");
    }

    private static async Task<Results<Ok, NotFound, ForbidHttpResult, ValidationProblem>> CreateEventStreams(
        [FromBody] CreateEventStreamsRequest request,
        [FromServices] DataContext dbContext,
        [FromServices] EventStreamService streamService,
        ClaimsPrincipal user,
        [FromServices] IAuthorizationService authSvc)
    {
        var (isValid, validationErrors) = await MiniValidation.MiniValidator.TryValidateAsync(request);
        if (!isValid) return TypedResults.ValidationProblem(validationErrors);

        if (request.EventIds is null || request.EventIds.Length == 0)
        {
            var errs = new Dictionary<string, string[]> { { "EventIds", new[] { "Required" } } };
            return TypedResults.ValidationProblem(errs);
        }

        var events = await dbContext.Events
            .Where(e => request.EventIds.Contains(e.Id))
            .Include(e => e.TruckRoute)
            .ToArrayAsync();

        if (events.Length == 0) return TypedResults.NotFound();

        await streamService.CreateEventStreams(events);

        return TypedResults.Ok();
    }

    public class CreateEventStreamsRequest
    {
        [Required]
        public required Guid[] EventIds { get; set; }
    }
}
