using System.ComponentModel;
using Asp.Versioning.Builder;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;
using Supabase.Gotrue.Interfaces;
using User = Supabase.Gotrue.User;

namespace FiMAdminApi.Endpoints;

public static class UsersEndpoints
{
    public static WebApplication RegisterUsersEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var usersGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/users")
            .WithApiVersionSet(vs).HasApiVersion(1).WithTags("Users")
            .RequireAuthorization(nameof(GlobalPermission.Superuser));

        usersGroup.MapGet("", SearchUsers).WithSummary("Search Users");
        usersGroup.MapGet("{id:guid:required}", GetUser).WithSummary("Get User by ID");
        usersGroup.MapPut("{id:guid:required}", UpdateUser).WithSummary("Update User");

        return app;
    }

    private static async Task<Ok<Data.Models.User[]>> SearchUsers(
        [FromQuery] [Description("A free-text search to filter the returned users")]
        string? searchTerm,
        [FromServices] IGotrueAdminClient<User> adminClient,
        [FromServices] DataContext dbContext)
    {
        var users = await adminClient.ListUsers(searchTerm, perPage: 20);
        if (users is null) return TypedResults.Ok(Array.Empty<Data.Models.User>());

        var selectedUsers = users.Users.Select(u =>
        {
            IEnumerable<GlobalPermission> permissions = Array.Empty<GlobalPermission>();
            u.AppMetadata.TryGetValue("globalPermissions", out var jsonPermissions);
            if (jsonPermissions is JArray permissionsArray)
            {
                permissions = permissionsArray.Select<JToken, GlobalPermission?>(t =>
                {
                    var value = t.Value<string>();
                    return Enum.TryParse<GlobalPermission>(value, true, out var permission) ? permission : null;
                }).Where(r => r is not null).Select(r => r!.Value);
            }

            return new Data.Models.User
            {
                Id = Guid.Parse(u.Id!),
                Email = u.Email,
                Name = null,
                GlobalPermissions = permissions.ToList()
            };
        });

        var profiles = await dbContext.Profiles.Where(p => selectedUsers.Select(u => u.Id).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        return TypedResults.Ok(selectedUsers.Select(user =>
        {
            if (user.Id is not null && profiles.TryGetValue(user.Id.Value, out var profile) &&
                !string.IsNullOrWhiteSpace(profile.Name))
            {
                user.Name = profile.Name;
            }
            else
            {
                user.Name = user.Email;
            }

            return user;
        }).ToArray());
    }

    private static async Task<Results<Ok<Data.Models.User>, NotFound>> GetUser(
        [FromRoute] [Description("The user's ID")]
        Guid id,
        [FromServices] IGotrueAdminClient<User> adminClient,
        [FromServices] DataContext dbContext)
    {
        User user;
        try
        {
            user = await adminClient.GetUserById(id.ToString()) ?? throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
            return TypedResults.NotFound();
        }
        catch (GotrueException ex)
        {
            if (ex.StatusCode != StatusCodes.Status404NotFound) throw;
            return TypedResults.NotFound();
        }

        IEnumerable<GlobalPermission> permissions = Array.Empty<GlobalPermission>();
        user.AppMetadata.TryGetValue("globalPermissions", out var jsonPermissions);
        if (jsonPermissions is JArray permissionsArray)
        {
            permissions = permissionsArray.Select<JToken, GlobalPermission?>(t =>
            {
                var value = t.Value<string>();
                return Enum.TryParse<GlobalPermission>(value, true, out var permission) ? permission : null;
            }).Where(r => r is not null).Select(r => r!.Value);
        }

        var userModel = new Data.Models.User
        {
            Id = Guid.Parse(user.Id!),
            Email = user.Email,
            Name = null,
            GlobalPermissions = permissions.ToList()
        };

        var profile = await dbContext.Profiles.SingleOrDefaultAsync(p => p.Id == userModel.Id);

        if (user.Id is not null && profile is not null &&
            !string.IsNullOrWhiteSpace(profile.Name))
        {
            userModel.Name = profile.Name;
        }
        else
        {
            userModel.Name = user.Email;
        }

        return TypedResults.Ok(userModel);
    }

    private static async Task<Ok> UpdateUser(
        [FromRoute] Guid id,
        [FromBody] UpdateUserRequest request,
        [FromServices] DataContext dbContext,
        [FromServices] IGotrueAdminClient<User> adminClient)
    {
        var update = new FixedAdminUserAttributes();
        if (request.NewPermissions is not null)
        {
            update.AppMetadata = new Dictionary<string, object>
            {
                { "globalPermissions", request.NewPermissions.Select(s => s.ToString()) }
            };

            // This handles a special case, we want superusers to have access to literally everything
            update.Role = request.NewPermissions.Contains(GlobalPermission.Superuser) ? "service_role" : "authenticated";
        }

        if (request.Name is not null)
        {
            update.UserMetadata = new Dictionary<string, object>
            {
                { "name", request.Name }
            };

            var profile = await dbContext.Profiles.FindAsync(id);
            if (profile is null)
            {
                profile = new Profile
                {
                    Id = id
                };
                await dbContext.Profiles.AddAsync(profile);
            }

            profile.Name = request.Name;

            await dbContext.SaveChangesAsync();
        }

        await adminClient.UpdateUserById(id.ToString(), update);

        return TypedResults.Ok();
    }

    public class UpdateUserRequest
    {
        public string? Name { get; set; }
        public IEnumerable<GlobalPermission>? NewPermissions { get; set; }
    }

    /// <summary>
    /// For some reason certain things aren't in the SDK, like the ability to override the user's postgres role
    /// </summary>
    private class FixedAdminUserAttributes : AdminUserAttributes
    {
        [JsonProperty("role")] public string? Role { get; set; }
    }
}