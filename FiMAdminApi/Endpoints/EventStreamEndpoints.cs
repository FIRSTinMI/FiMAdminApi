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

        return app;
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
