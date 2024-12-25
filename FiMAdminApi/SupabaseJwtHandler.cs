using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace FiMAdminApi;

public class SupabaseJwtHandler : JwtBearerHandler
{
    private readonly IGotrueAdminClient<User> _adminClient;
    public SupabaseJwtHandler(IGotrueAdminClient<User> adminClient, IOptionsMonitor<JwtBearerOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock) : base(options, logger, encoder, clock)
    {
        _adminClient = adminClient;
    }

    public SupabaseJwtHandler(IGotrueAdminClient<User> adminClient, IOptionsMonitor<JwtBearerOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder)
    {
        _adminClient = adminClient;
    }
    
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Get the token from the Authorization header
        if (!Context.Request.Headers.TryGetValue("Authorization", out var authorizationHeaderValues))
        {
            return AuthenticateResult.NoResult();
        }

        var authorizationHeader = authorizationHeaderValues.FirstOrDefault();
        if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer "))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        
        // Sanity check the token before we send it off to Supabase
        try
        {
            _ = new JwtSecurityToken(token);
        }
        catch (Exception)
        {
            return AuthenticateResult.Fail("JWT was not in a valid format");
        }

        // Call the API to validate the token
        User user;
        try
        {
            user = await _adminClient.GetUser(token) ?? throw new InvalidOperationException("User from Supabase was null");
        }
        catch (Exception ex)
        {
            Logger.LogInformation(ex, "Exception thrown while validating token");
            return AuthenticateResult.Fail("Token validation failed.");
        }
        
        // Set the authentication result with the claims from the API response          
        var principal = GetClaims(user);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, "CustomJwtBearer"));
    }


    private static ClaimsPrincipal GetClaims(User user)
    {
        var claimsIdentity = new ClaimsIdentity(new []
        {
            new Claim("email", user.Email ?? "(no email)"),
            new Claim("id", user.Id ?? throw new InvalidOperationException("User ID was null"))
        }, "Token");
        if (user.AppMetadata.TryGetValue("globalPermissions", out var value) && value is JArray permissions)
        {
            claimsIdentity.AddClaims(permissions.Select(r => r.Value<string>()).Where(r => r != null)
                .Select(r => new Claim("globalPermission", r!)));
        }
        
        var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

        return claimsPrincipal;
    }
}