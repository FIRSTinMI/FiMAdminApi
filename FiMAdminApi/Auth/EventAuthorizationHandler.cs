using FiMAdminApi.Data;
using FiMAdminApi.Data.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Auth;

/// <summary>
/// Allow a user to access an event if any of the following are true:
/// a) they are a superuser
/// b) they have a global permission which allows for that action
/// c) they specifically have permission to perform that action on this particular event
/// </summary>
public class EventAuthorizationHandler(DataContext dataContext)
    : AuthorizationHandler<EventAuthorizationRequirement, Guid>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        EventAuthorizationRequirement requirement,
        Guid eventId)
    {
        if (context.User.HasClaim("globalPermission", GlobalPermission.Superuser.ToString()))
        {
            context.Succeed(requirement);
            return;
        }

        if (requirement.NeededGlobalPermission is not null &&
            context.User.HasClaim("globalPermission", requirement.NeededGlobalPermission.ToString()!))
        {
            context.Succeed(requirement);
            return;
        }

        var userId = context.User.Claims.FirstOrDefault(c => c.Type == "id");
        if (userId is null || string.IsNullOrEmpty(userId.Value)) return;

        var staff = await dataContext.EventStaffs.FirstOrDefaultAsync(s =>
            s.EventId == eventId && s.UserId == Guid.Parse(userId.Value));

        if (staff is null) return;

        if (staff.Permissions.Contains(requirement.NeededEventPermission)) context.Succeed(requirement);
    }
}