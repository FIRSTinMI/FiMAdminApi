using System.Security.Principal;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FiMAdminApi.Auth;

public class EventSyncAuthHandler(
    IOptionsMonitor<EventSyncAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<EventSyncAuthOptions>(options, logger, encoder)
{
    public const string EventSyncAuthScheme = "SyncSecret";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var hasHeader = Context.Request.Headers.TryGetValue("X-fim-sync-secret", out var attemptedSecret);
        if (!hasHeader || attemptedSecret.Count != 1) return Task.FromResult(AuthenticateResult.NoResult());

        var configuration = Context.RequestServices.GetRequiredService<IConfiguration>();
        var expectedSecret = configuration["Sync:Secret"];
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            Logger.LogWarning("No sync secret was found, failing any attempts to authenticate with it");
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (attemptedSecret.Single() == expectedSecret)
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new GenericPrincipal(new GenericIdentity("Sync Engine"), []),
                    EventSyncAuthScheme)));

        return Task.FromResult(AuthenticateResult.Fail("Incorrect sync secret provided"));
    }
}

public class EventSyncAuthOptions : AuthenticationSchemeOptions
{
}