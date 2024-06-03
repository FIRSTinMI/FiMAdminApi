using Asp.Versioning;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
using User = Supabase.Gotrue.User;

namespace FiMAdminApi.Controllers;

[Authorize(nameof(GlobalRole.Superuser))]
[ApiVersion("1.0")]
[Route("/api/v{apiVersion:apiVersion}/users")]
public class UsersController(
        IGotrueAdminClient<User> adminClient,
        DataContext dbContext
    ) : BaseController
{
    [HttpGet("")]
    [ProducesResponseType(typeof(List<Data.Models.User>), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetUsers([FromQuery] string? searchTerm = null)
    {
        var users = await adminClient.ListUsers(searchTerm, perPage: 20);
        if (users is null) return Ok(Array.Empty<Data.Models.User>());

        var selectedUsers = users.Users.Select(u =>
        {
            IEnumerable<GlobalRole> roles = Array.Empty<GlobalRole>();
            u.AppMetadata.TryGetValue("globalRoles", out var jsonRoles);
            if (jsonRoles is JArray rolesArray)
            {
                roles = rolesArray.Select<JToken, GlobalRole?>(t =>
                {
                    var value = t.Value<string>();
                    return Enum.TryParse<GlobalRole>(value, true, out var role) ? role : null;
                }).Where(r => r is not null).Select(r => r!.Value);
            }

            return new Data.Models.User
            {
                Id = Guid.Parse(u.Id!),
                Email = u.Email,
                Name = null,
                GlobalRoles = roles.ToList()
            };
        });

        var profiles = await dbContext.Profiles.Where(p => selectedUsers.Select(u => u.Id).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);
        
        return Ok(selectedUsers.Select(user =>
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
        }).ToList());
    }

    /// <summary>
    /// TODO: This endpoint and the list endpoint should get cleaned up as they share a lot of (ugly) code
    /// </summary>
    /// <param name="id">The user's ID</param>
    /// <returns>The user, or not found</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Data.Models.User), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetUser(string id)
    {
        var user = await adminClient.GetUserById(id);
        if (user is null) return NotFound();
        
        IEnumerable<GlobalRole> roles = Array.Empty<GlobalRole>();
        user.AppMetadata.TryGetValue("globalRoles", out var jsonRoles);
        if (jsonRoles is JArray rolesArray)
        {
            roles = rolesArray.Select<JToken, GlobalRole?>(t =>
            {
                var value = t.Value<string>();
                return Enum.TryParse<GlobalRole>(value, true, out var role) ? role : null;
            }).Where(r => r is not null).Select(r => r!.Value);
        }

        var userModel = new Data.Models.User
        {
            Id = Guid.Parse(user.Id!),
            Email = user.Email,
            Name = null,
            GlobalRoles = roles.ToList()
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

        return Ok(userModel);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> UpdateUser(Guid id, [FromBody] UpdateRolesRequest request)
    {
        var update = new FixedAdminUserAttributes();
        if (request.NewRoles is not null)
        {
            update.AppMetadata = new Dictionary<string, object>
            {
                { "globalRoles", request.NewRoles.Select(s => s.ToString()) }
            };

            // This handles a special case, we want superusers to have access to literally everything
            update.Role = request.NewRoles.Contains(GlobalRole.Superuser) ? "service_role" : "authenticated";
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
                profile = new Profile()
                {
                    Id = id
                };
                await dbContext.Profiles.AddAsync(profile);
            }
            
            profile.Name = request.Name;

            await dbContext.SaveChangesAsync();
        }
            
        await adminClient.UpdateUserById(id.ToString(), update);

        return Ok();
    }

    public class UpdateRolesRequest
    {
        public string? Name { get; set; }
        public IEnumerable<GlobalRole>? NewRoles { get; set; }
    }

    /// <summary>
    /// For some reason certain things aren't in the SDK, like the ability to override the user's postgres role
    /// </summary>
    private class FixedAdminUserAttributes : AdminUserAttributes
    {
        [JsonProperty("role")]
        public string Role { get; set; }
    }
}