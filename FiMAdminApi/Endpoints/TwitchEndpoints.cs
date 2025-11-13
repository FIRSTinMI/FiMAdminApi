using Asp.Versioning.Builder;
using FiMAdminApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FiMAdminApi.Endpoints;

public static class TwitchEndpoints
{
    public static WebApplication RegisterTwitchEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var routeGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/twitch")
            .WithApiVersionSet(vs).HasApiVersion(1).WithTags("Twitch")
            .AllowAnonymous();

        routeGroup.MapGet("/connect", Connect)
            .WithSummary("Begin Twitch authorization (redirect)")
            .WithDescription("Redirects the user agent to Twitch's /authorize endpoint to start the OAuth authorization code flow.");

        routeGroup.MapPost("/exchange-code", ExchangeCode)
            .WithSummary("Exchange Twitch authorization code for tokens")
            .WithDescription("Exchanges an authorization code returned by Twitch for an access token and refresh token.");

        return app;
    }

    private static async Task<Results<Ok<TwitchTokenResponse>, ProblemHttpResult>> ExchangeCode(
        [FromBody] ExchangeCodeRequest request,
        [FromServices] TwitchService twitchService)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            return TypedResults.Problem("code and redirectUri are required");
        }

        var token = await twitchService.ExchangeCodeForTokenAsync(request.Code, request.RedirectUri);

        var resp = new TwitchTokenResponse(
            token.AccessToken,
            token.RefreshToken,
            token.ExpiresIn,
            token.Scope,
            token.TokenType
        );

        return TypedResults.Ok(resp);
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

    public record ExchangeCodeRequest(string Code, string RedirectUri);
    public record TwitchTokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, string[] Scope, string TokenType);

    public record TwitchConnectResponse(string AuthorizeUrl, string State);
}
