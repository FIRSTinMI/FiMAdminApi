using Asp.Versioning.Builder;
using FiMAdminApi.Services;
using FiMAdminApi.Data.EfPgsql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.HttpResults;
using FiMAdminApi.Models.Enums;

namespace FiMAdminApi.Endpoints;

public static class YoutubeEndpoints
{
    public static WebApplication RegisterYoutubeEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var routeGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/youtube")
            .WithApiVersionSet(vs).HasApiVersion(1).WithTags("YouTube")
            .RequireAuthorization(nameof(GlobalPermission.Superuser));

        routeGroup.MapGet("/connect", Connect)
            .WithSummary("Begin YouTube authorization (get URL)")
            .WithDescription("Returns the Google OAuth2 authorization URL that a user should visit to authorize the application.");

        routeGroup.MapPost("/set-code", SetCode)
            .WithSummary("Exchange authorization code for tokens and persist them")
            .WithDescription("Exchanges a Google OAuth code for access/refresh tokens and stores them in the vault. Accepts `code` and optional `scope` and `redirectUri` in the request body.");

        routeGroup.MapGet("/scopes", GetYoutubeScopes)
            .WithSummary("List Google scopes in Vault")
            .WithDescription("Returns all vault secrets with keys starting with 'google:scope:' as a mapping of email -> scopes string and expiry.");

        routeGroup.MapPost("/broadcasts/{broadcastId}/stop", StopBroadcast)
            .WithSummary("Stop/complete a broadcast")
            .WithDescription("Transition a broadcast to 'complete' for the given account email and broadcast id.");

        routeGroup.MapPut("/broadcasts/{broadcastId}/auto-stop", SetBroadcastAutoStop);

        routeGroup.MapGet("/broadcasts/status", GetBroadcastStatus)
            .WithSummary("Get current broadcast status")
            .WithDescription("Returns the current broadcast lifecycle/status for the account (live, ready, complete, none).");

        return app;
    }

    private static Results<Ok<YoutubeConnectResponse>, ProblemHttpResult> Connect(
        [FromServices] YoutubeService youtubeService,
        [FromQuery] string? state,
        [FromQuery] string? redirectUri)
    {
        try
        {
            var url = youtubeService.GetAuthorizationUrl(state);
            if (!string.IsNullOrWhiteSpace(redirectUri))
            {
                var sep = url.Contains('?') ? '&' : '?';
                url = url + sep + "redirect_uri=" + Uri.EscapeDataString(redirectUri);
            }

            return TypedResults.Ok(new YoutubeConnectResponse(url, state ?? string.Empty));
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ex.Message);
        }
    }

    private static async Task<Results<Ok, ProblemHttpResult>> SetCode(
        [FromBody] YoutubeSetCodeRequest? request,
        [FromServices] YoutubeService youtubeService,
        [FromServices] VaultService vaultService,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(YoutubeEndpoints));
        if (request is null || string.IsNullOrWhiteSpace(request.Code))
            return TypedResults.Problem("code is required");

        try
        {
            var token = await youtubeService.ExchangeCodeForTokenAsync(request.Code, request.RedirectUri ?? string.Empty);
            if (string.IsNullOrWhiteSpace(token.AccessToken))
                return TypedResults.Problem("Failed to exchange code for token");

            // Identify account: prefer email, then channelId, then sub
            var user = await youtubeService.GetUserFromAccessTokenAsync(token.AccessToken);
            var identifier = user?.email;

            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new ApplicationException("Could not identify Google account from token response");
            }

            // persist tokens in vault
            try
            {
                await vaultService.UpsertSecret($"google:access_token:{identifier}", token.AccessToken ?? string.Empty);
                await vaultService.UpsertSecret($"google:refresh_token:{identifier}", token.RefreshToken);

                if (token.ExpiresInSeconds.HasValue)
                {
                    var expiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds.Value);
                    await vaultService.UpsertSecret($"google:expires_at:{identifier}", expiresAt.ToString("o"));
                }
                if (!string.IsNullOrWhiteSpace(request.Scope))
                {
                    await vaultService.UpsertSecret($"google:scope:{identifier}", request.Scope);
                }
            }
            catch
            {
                logger.LogError("Error saving YouTube tokens to vault for account {Account}", identifier);
            }

            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ex.Message);
        }
    }

    private static async Task<Results<Ok, ProblemHttpResult>> StopBroadcast(
        [FromRoute] string broadcastId,
        [FromQuery] string? acctEmail,
        [FromServices] YoutubeService youtubeService)
    {
        if (string.IsNullOrWhiteSpace(broadcastId)) return TypedResults.Problem("broadcastId is required");
        if (string.IsNullOrWhiteSpace(acctEmail)) return TypedResults.Problem("acctEmail (account identifier/email) is required as query parameter");

        try
        {
            var ok = await youtubeService.StopBroadcastAsync(acctEmail, broadcastId);
            if (!ok) return TypedResults.Problem("Failed to stop broadcast");
            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ex.Message);
        }
    }
    
    private static async Task<Results<Ok, ProblemHttpResult>> SetBroadcastAutoStop(
        [FromRoute] string broadcastId,
        [FromQuery] string? acctEmail,
        [FromBody] bool autoStop,
        [FromServices] YoutubeService youtubeService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(broadcastId)) return TypedResults.Problem("broadcastId is required");
        if (string.IsNullOrWhiteSpace(acctEmail)) return TypedResults.Problem("acctEmail (account identifier/email) is required as query parameter");

        try
        {
            var ok = await youtubeService.SetAutoStopAsync(autoStop, acctEmail, broadcastId, cancellationToken);
            if (!ok) return TypedResults.Problem("Failed to stop broadcast");
            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ex.Message);
        }
    }

    private static async Task<Results<Ok<IEnumerable<YoutubeBroadcastStatus>>, ProblemHttpResult>> GetBroadcastStatus(
        [FromQuery] string? acctEmail,
        [FromServices] YoutubeService youtubeService)
    {
        if (string.IsNullOrWhiteSpace(acctEmail)) return TypedResults.Problem("acctEmail (account identifier/email) is required as query parameter");

        try
        {
            var statuses = await youtubeService.GetCurrentBroadcastsStatusAsync(acctEmail);
            if (statuses is null) return TypedResults.Problem("Could not retrieve broadcast status or no access token available");
            return TypedResults.Ok<IEnumerable<YoutubeBroadcastStatus>>(statuses);
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ex.Message);
        }
    }

    private static async Task<Results<Ok<Dictionary<string, YoutubeScopeInfo>>, ProblemHttpResult>> GetYoutubeScopes([
        FromServices] VaultService vaultService)
    {
        try
        {
            const string scopePrefix = "google:scope:";
            const string expiresPrefix = "google:expires_at:";

            var scopes = await vaultService.GetSecretsByPrefix(scopePrefix);
            var expires = await vaultService.GetSecretsByPrefix(expiresPrefix);

            var result = new Dictionary<string, YoutubeScopeInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in scopes)
            {
                var name = kv.Key;
                if (!name.StartsWith(scopePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var ident = name[scopePrefix.Length..];
                expires.TryGetValue(expiresPrefix + ident, out var expVal);
                result[ident] = new YoutubeScopeInfo(kv.Value, expVal);
            }

            // include identifiers that have expires but no scopes
            foreach (var kv in expires)
            {
                var name = kv.Key;
                if (!name.StartsWith(expiresPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var ident = name[expiresPrefix.Length..];
                if (!result.ContainsKey(ident))
                {
                    result[ident] = new YoutubeScopeInfo(null, kv.Value);
                }
            }

            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ex.Message);
        }
    }

    public record YoutubeConnectResponse(string AuthorizeUrl, string State);

    public record YoutubeScopeInfo(string? Scopes, string? ExpiresAt);

    public record YoutubeSetCodeRequest(string Code, string? Scope, string? RedirectUri);
}
