using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Asp.Versioning.Builder;
using FiMAdminApi.Auth;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniValidation;
using Supabase.Gotrue.Interfaces;
using User = Supabase.Gotrue.User;

namespace FiMAdminApi.Endpoints;

public static class EventsEndpoints
{
    public static WebApplication RegisterEventsEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var eventsCreateGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/events")
            .WithApiVersionSet(vs).HasApiVersion(1).WithTags("Events")
            .RequireAuthorization();

        eventsCreateGroup.MapPut("{id:guid}", UpdateBasicInfo)
            .WithSummary("Update basic event info");
        eventsCreateGroup.MapPut("{eventId:guid}/staffs", UpsertEventStaff)
            .WithSummary("Create or update a staff user for an event");
        eventsCreateGroup.MapDelete("{eventId:guid}/staffs/{userId:guid}", DeleteEventStaff)
            .WithSummary("Remove a staff user for an event");
        
        return app;
    }

    private static async Task<Results<Ok<Event>, NotFound, ForbidHttpResult, ValidationProblem>> UpdateBasicInfo(
        [FromRoute] Guid id,
        [FromBody] UpdateBasicInfoRequest request,
        [FromServices] IAuthorizationService authSvc,
        ClaimsPrincipal user,
        [FromServices] DataContext dbContext)
    {
        var (isValid, validationErrors) = await MiniValidator.TryValidateAsync(request);
        if (!isValid) return TypedResults.ValidationProblem(validationErrors);
        
        var authResult = await authSvc.AuthorizeAsync(user, id, new EventAuthorizationRequirement
        {
            NeededGlobalPermission = GlobalPermission.Events_Manage,
            NeededEventPermission = EventPermission.Event_ManageInfo
        });
        if (!authResult.Succeeded) return TypedResults.Forbid();
        
        var evt = await dbContext.Events.FirstOrDefaultAsync(e => e.Id == id);
        if (evt is null) return TypedResults.NotFound();

        evt.Name = request.Name;
        evt.TruckRouteId = request.TruckRouteId;
        evt.StartTime = request.StartTime.UtcDateTime;
        evt.EndTime = request.EndTime.UtcDateTime;
        evt.TimeZone = request.Timezone;
        evt.Status = request.Status;

        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(evt);
    }

    private static async Task<Results<Ok<EventStaff>, ForbidHttpResult, ValidationProblem>> UpsertEventStaff(
        [FromRoute] Guid eventId,
        [FromBody] UpsertEventStaffRequest request,
        [FromServices] DataContext dbContext,
        [FromServices] IGotrueAdminClient<User> adminClient,
        ClaimsPrincipal user,
        [FromServices] IAuthorizationService authSvc)
    {
        var (_, validationErrors) = await MiniValidator.TryValidateAsync(request);
        if (!await dbContext.Events.AnyAsync(e => e.Id == eventId))
            validationErrors.Add("EventId", ["DoesNotExist"]);
        if (await adminClient.GetUserById(request.UserId.ToString()) is not null)
            validationErrors.Add("UserId", ["DoesNotExist"]);
        if (validationErrors.Count != 0) return TypedResults.ValidationProblem(validationErrors);
        
        var isAuthorized = await authSvc.AuthorizeAsync(user, eventId, new EventAuthorizationRequirement
        {
            NeededEventPermission = EventPermission.Event_ManageInfo,
            NeededGlobalPermission = GlobalPermission.Events_Manage
        });
        if (!isAuthorized.Succeeded) return TypedResults.Forbid();
        
        var staffRecord =
            await dbContext.EventStaffs.FirstOrDefaultAsync(s => s.EventId == eventId && s.UserId == request.UserId);
        if (staffRecord is null)
            staffRecord = new EventStaff
            {
                EventId = eventId,
                UserId = request.UserId,
                Permissions = request.Permissions
            };
        else
        {
            staffRecord.Permissions = request.Permissions;
        }

        dbContext.EventStaffs.Update(staffRecord);
        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(staffRecord);
    }
    
    private static async Task<Results<Ok, NotFound, ForbidHttpResult, ValidationProblem>> DeleteEventStaff(
        [FromRoute] Guid eventId,
        [FromRoute] Guid userId,
        [FromServices] DataContext dbContext,
        [FromServices] IGotrueAdminClient<User> adminClient,
        ClaimsPrincipal user,
        [FromServices] IAuthorizationService authSvc)
    {
        var validationErrors = new Dictionary<string, string[]>();
        if (!await dbContext.Events.AnyAsync(e => e.Id == eventId))
            validationErrors.Add("EventId", ["DoesNotExist"]);
        if (await adminClient.GetUserById(userId.ToString()) is not null)
            validationErrors.Add("UserId", ["DoesNotExist"]);
        if (validationErrors.Count != 0) return TypedResults.ValidationProblem(validationErrors);
        
        var isAuthorized = await authSvc.AuthorizeAsync(user, eventId, new EventAuthorizationRequirement
        {
            NeededEventPermission = EventPermission.Event_ManageInfo,
            NeededGlobalPermission = GlobalPermission.Events_Manage
        });
        if (!isAuthorized.Succeeded) return TypedResults.Forbid();

        var staffRecord =
            await dbContext.EventStaffs.FirstOrDefaultAsync(s => s.EventId == eventId && s.UserId == userId);
        if (staffRecord is null)
            return TypedResults.NotFound();

        dbContext.EventStaffs.Remove(staffRecord);
        await dbContext.SaveChangesAsync();

        return TypedResults.Ok();
    }

    public class UpdateBasicInfoRequest
    {
        [Required]
        public required string Name { get; set; }
        
        [Range(1, int.MaxValue)]
        public int? TruckRouteId { get; set; }
        
        [Required]
        public required DateTimeOffset StartTime { get; set; }
        
        [Required]
        public required DateTimeOffset EndTime { get; set; }
        
        [Required]
        [RegularExpression("[A-Za-z_]+/[A-Za-z_]+")]
        public required string Timezone { get; set; }
        
        [Required]
        public required EventStatus Status { get; set; }
    }

    public class UpsertEventStaffRequest
    {
        [Required]
        public Guid UserId { get; set; }

        [Required]
        public ICollection<EventPermission> Permissions { get; set; } = new List<EventPermission>();
    }
}