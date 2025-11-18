using Asp.Versioning.Builder;
using FiMAdminApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.HttpResults;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Enums;

namespace FiMAdminApi.Endpoints;

public static class TwitchEndpoints
{
    public static WebApplication RegisterTwitchEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var routeGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/twitch")
            .WithApiVersionSet(vs).HasApiVersion(1).WithTags("Twitch")
            .RequireAuthorization(nameof(GlobalPermission.Superuser));

        routeGroup.MapGet("/connect", Connect)
            .WithSummary("Begin Twitch authorization (redirect)")
            .WithDescription("Redirects the user agent to Twitch's /authorize endpoint to start the OAuth authorization code flow.");

        routeGroup.MapPost("/set-code", SetCode)
            .WithSummary("Update authorization code for tokens")
            .WithDescription("Updates an authorization code returned by Twitch for an access token and refresh token.");

        routeGroup.MapGet("/scopes", GetTwitchScopes)
            .WithSummary("List Twitch scopes in Vault")
            .WithDescription("Returns all vault secrets with keys starting with 'twitch:scope:' as a mapping of key -> scopes string.");

        return app;
    }

    private static async Task<Results<Ok, ProblemHttpResult>> SetCode(
        [FromBody] SetCodeRequest request,
        [FromServices] TwitchService twitchService,
        [FromServices] VaultService vaultService,
        [FromServices] ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Scope))
        {
            return TypedResults.Problem("code and scope are required");
        }

        var token = await twitchService.ExchangeCodeForTokenAsync(request.Code, request.RedirectUri);

        // Get channel info from token
        var user = await twitchService.GetUserFromAccessTokenAsync(token.AccessToken);

        // persist tokens in vault
        try
        {
            await vaultService.UpsertSecret($"twitch:access_token:{user.Login}", token.AccessToken);
            await vaultService.UpsertSecret($"twitch:refresh_token:{user.Login}", token.RefreshToken);
            await vaultService.UpsertSecret($"twitch:expires_at:{user.Login}", DateTime.UtcNow.AddSeconds(token.ExpiresIn).ToString("o"));
            await vaultService.UpsertSecret($"twitch:scope:{user.Login}", string.Join(" ", token.Scopes));
        }
        catch
        {
            logger.LogError("Error saving Twitch tokens to vault for channel {Channel}", user.Login);
        }

        return TypedResults.Ok();
    }

    private static Results<Ok<TwitchConnectResponse>, ProblemHttpResult> Connect(
        [FromServices] IConfiguration configuration,
        [FromQuery] string? redirectUri,
        [FromQuery] string? scope,
        [FromQuery] bool forceVerify = false)
    {
        var clientId = configuration["Twitch:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return TypedResults.Problem("Twitch ClientId not configured");
        }

        // if redirectUri not provided, try configuration
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            redirectUri = configuration["Twitch:RedirectUri"];
            if (string.IsNullOrWhiteSpace(redirectUri))
            {
                return TypedResults.Problem("redirectUri is required either as query parameter or in configuration (Twitch:RedirectUri)");
            }
        }

        var state = Guid.NewGuid().ToString("N");

        var query = new List<string>
        {
            $"response_type=code",
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"state={Uri.EscapeDataString(state)}"
        };

        if (!string.IsNullOrWhiteSpace(scope))
        {
            // Twitch expects scopes space-delimited and URL-encoded
            query.Add($"scope={Uri.EscapeDataString(scope)}");
        }

        if (forceVerify)
        {
            query.Add("force_verify=true");
        }

        var url = "https://id.twitch.tv/oauth2/authorize?" + string.Join("&", query);

        var resp = new TwitchConnectResponse(url, state);
        return TypedResults.Ok(resp);
    }

    public record TwitchScopeInfo(string? Scopes, string? ExpiresAt);

    private static async Task<Results<Ok<Dictionary<string, TwitchScopeInfo>>, ProblemHttpResult>> GetTwitchScopes([
        FromServices] VaultService vaultService)
    {
        try
        {
            var scopePrefix = "twitch:scope:";
            var expiresPrefix = "twitch:expires_at:";

            var scopes = await vaultService.GetSecretsByPrefix(scopePrefix);
            var expires = await vaultService.GetSecretsByPrefix(expiresPrefix);

            var result = new Dictionary<string, TwitchScopeInfo>(StringComparer.OrdinalIgnoreCase);

            // include channels that have scopes
            foreach (var kv in scopes)
            {
                var name = kv.Key;
                if (!name.StartsWith(scopePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var channel = name.Substring(scopePrefix.Length);
                expires.TryGetValue(expiresPrefix + channel, out var expVal);
                result[channel] = new TwitchScopeInfo(kv.Value, expVal);
            }

            // include channels that have expires but no scopes
            foreach (var kv in expires)
            {
                var name = kv.Key;
                if (!name.StartsWith(expiresPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var channel = name.Substring(expiresPrefix.Length);
                if (!result.ContainsKey(channel))
                {
                    result[channel] = new TwitchScopeInfo(null, kv.Value);
                }
            }

            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ex.Message);
        }
    }

    public record SetCodeRequest(string Code, string Scope, string RedirectUri);

    public record TwitchConnectResponse(string AuthorizeUrl, string State);
}
