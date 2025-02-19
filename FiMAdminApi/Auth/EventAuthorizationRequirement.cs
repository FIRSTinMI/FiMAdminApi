using FiMAdminApi.Models.Enums;
using Microsoft.AspNetCore.Authorization;

namespace FiMAdminApi.Auth;

public class EventAuthorizationRequirement : IAuthorizationRequirement
{
    public EventPermission NeededEventPermission;
    public GlobalPermission? NeededGlobalPermission;
}