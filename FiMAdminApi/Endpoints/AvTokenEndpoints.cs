using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Asp.Versioning.Builder;
using FiMAdminApi.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace FiMAdminApi.Endpoints;

public static class AvTokenEndpoints
{
    public static WebApplication RegisterAvTokenEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var eventsCreateGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/av-token")
            .WithApiVersionSet(vs).HasApiVersion(1).WithTags("AV Tokens")
            .AllowAnonymous();

        eventsCreateGroup.MapPost("", CreateAvToken)
            .WithSummary("Create Token for AV System")
            .WithDescription(
                "Will return a token with limited permissions for a specific event, valid until the event's end date");
        
        return app;
    }

    private static async Task<Results<Ok<CreateAvTokenResponse>, ProblemHttpResult>> CreateAvToken(
        [FromBody] CreateAvTokenRequest request,
        [FromServices] IConfiguration configuration,
        [FromServices] DataContext dbContext)
    {
        var escapedEventKey = request.EventKey.Replace("%", "\\%").Replace("_", "\\_");
        var evt = await dbContext.Events.FirstOrDefaultAsync(e =>
            EF.Functions.ILike(e.Key, escapedEventKey) &&
            e.StartTime < DateTime.UtcNow && e.EndTime > DateTime.UtcNow);
        if (evt is null) return TypedResults.Problem("Unable to authenticate at this time with the given event key");

        var jwtSecret = configuration["Auth:JwtSecret"] ??
                        throw new ApplicationException("Unable to get JWT secret from configuration");

        List<Claim> claims = [new Claim("eventId", evt.Id.ToString()), new Claim("eventKey", evt.Key)];
        if (evt.Code is not null) claims.Add(new Claim("eventCode", evt.Code));

        var maxAllowableExpiry = DateTime.UtcNow.AddDays(7);
        
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(
            jwtSecret));
        var cred = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "https://admin.fimav.us/av-token",
            claims: claims,
            // 7 days or when the event ends, whichever is sooner
            expires: evt.EndTime > maxAllowableExpiry ? maxAllowableExpiry : evt.EndTime,
            signingCredentials: cred
        );
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        return TypedResults.Ok(new CreateAvTokenResponse(jwt));
    }

    public record CreateAvTokenRequest(string EventKey);

    public record CreateAvTokenResponse(string AccessToken);
}