using Asp.Versioning.Builder;
using FiMAdminApi.Data.EfPgsql;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SlackNet;

namespace FiMAdminApi.Endpoints;

public static class SlackBotEndpoints
{
    public static WebApplication RegisterSlackBotEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var matchesGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/slack")
            .WithTags("Slack Bot").WithApiVersionSet(vs).HasApiVersion(1)
            .AllowAnonymous().ExcludeFromDescription();

        matchesGroup.MapGet("/redirect", OauthRedirect);

        return app;
    }

    private static async Task<Results<Ok<string>, NotFound, ForbidHttpResult>> OauthRedirect(
        [FromServices] DataContext dataContext,
        [FromServices] ISlackApiClient slackClient,
        [FromServices] IConfiguration config,
        [FromServices] DataContext dbContext,
        [FromQuery] string code)
    {
        var clientId = config["Slack:ClientId"];
        var clientSecret = config["Slack:ClientSecret"];
        var tokenResp =
            await slackClient.OAuthV2.Access(clientId, clientSecret, code, null, null, null, CancellationToken.None);

        if (tokenResp.AuthedUser == null) return TypedResults.Ok("You can close this tab.");
        var secretName = $"slack_token:{tokenResp.AuthedUser.Id}";
        await dbContext.Database.ExecuteSqlAsync($"select vault.create_secret({tokenResp.AuthedUser.AccessToken}, {secretName})");
        return TypedResults.Ok("You can close this tab.");
    }
}