using FiMAdminApi.Data.EfPgsql;
namespace FiMAdminApi.Services;

public class TwitchService(IConfiguration configuration, ILogger<TwitchService> logger, VaultService vaultService)
{
    // Get an access token for simple basic API access 
    public async Task<string> GetAppAccessToken()
    {
        var clientId = configuration["Twitch:ClientId"];
        var clientSecret = configuration["Twitch:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogError("Twitch configuration missing (ClientId or ClientSecret).");
            throw new InvalidOperationException("Twitch configuration incomplete.");
        }

        var api = new TwitchLib.Api.TwitchAPI();
        api.Settings.ClientId = clientId;
        api.Settings.Secret = clientSecret;

        return await api.Auth.GetAccessTokenAsync();
    }

    public async Task<string> GetBroadcasterId(string channel)
    {

        if (string.IsNullOrWhiteSpace(channel))
        {
            return string.Empty;
        }

        try
        {
            var clientId = configuration["Twitch:ClientId"];
            var accessToken = await GetAppAccessToken();

            var api = new TwitchLib.Api.TwitchAPI();
            api.Settings.ClientId = clientId;
            api.Settings.AccessToken = accessToken;
            var users = await api.Helix.Users.GetUsersAsync(logins: new List<string> { channel });
            var user = users.Users.FirstOrDefault();
            return user?.Id ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resolving Twitch broadcaster id for login {Login}.", channel);
            return string.Empty;
        }
    }

    public async Task<bool> UpdateLivestreamInformation(string channel, string name)
    {
        var clientId = configuration["Twitch:ClientId"];
        var accessToken = await GetAppAccessToken();
        var broadcasterId = await GetBroadcasterId(channel);

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(broadcasterId))
        {
            logger.LogError("Twitch configuration missing (ClientId, AccessToken or BroadcasterId).");
            return false;
        }

        // Get access token for channel
        var accessTokenForChannel = await GetAuthorizationToken(channel);
        if (string.IsNullOrWhiteSpace(accessTokenForChannel))
        {
            logger.LogError("No valid access token for Twitch channel {Channel}.", channel);
            return false;
        }

        // Update stream title
        try
        {
            var api = new TwitchLib.Api.TwitchAPI();
            api.Settings.ClientId = clientId;
            api.Settings.AccessToken = accessTokenForChannel;
            var newInfo = new TwitchLib.Api.Helix.Models.Channels.ModifyChannelInformation.ModifyChannelInformationRequest
            {
                Title = name,
                BroadcasterLanguage = "en",
            };
            await api.Helix.Channels.ModifyChannelInformationAsync(broadcasterId, newInfo);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while updating Twitch title.");
            return false;
        }
    }

    public async Task<string?> GetStreamKey(string channel)
    {
        var clientId = configuration["Twitch:ClientId"];
        var accessToken = await GetAppAccessToken();
        var broadcasterId = await GetBroadcasterId(channel);

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(broadcasterId))
        {
            logger.LogError("Twitch configuration missing (ClientId, AccessToken or BroadcasterId).");
            return null;
        }

        try
        {
            var channelAccessToken = await GetAuthorizationToken(channel);
            if (string.IsNullOrWhiteSpace(channelAccessToken))
            {
                logger.LogError("No valid access token for Twitch channel {Channel}.", channel);
                return null;
            }

            var api = new TwitchLib.Api.TwitchAPI();
            api.Settings.ClientId = clientId;
            api.Settings.AccessToken = channelAccessToken;
            var streamKeyResponse = await api.Helix.Streams.GetStreamKeyAsync(broadcasterId);
            return streamKeyResponse.Streams.FirstOrDefault()?.Key;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while retrieving Twitch stream key.");
            return null;
        }
    }

    // Exchange an authorization code (from the redirect) for an access token + refresh token
    public async Task<TwitchLib.Api.Auth.AuthCodeResponse> ExchangeCodeForTokenAsync(string code, string redirectUri)
    {
        var clientId = configuration["Twitch:ClientId"];
        var clientSecret = configuration["Twitch:ClientSecret"];

        var api = new TwitchLib.Api.TwitchAPI();
        api.Settings.ClientId = clientId;
        api.Settings.Secret = clientSecret;
        return await api.Auth.GetAccessTokenFromCodeAsync(code, clientSecret, redirectUri);
    }

    // Get channel from access token
    public async Task<TwitchLib.Api.Helix.Models.Users.GetUsers.User> GetUserFromAccessTokenAsync(string accessToken)
    {
        var clientId = configuration["Twitch:ClientId"];

        var api = new TwitchLib.Api.TwitchAPI();
        api.Settings.ClientId = clientId;
        api.Settings.AccessToken = accessToken;

        var users = await api.Helix.Users.GetUsersAsync();
        return users.Users.FirstOrDefault()!;
    }

    // Return a valid authorization access token for the given channel.
    // Checks stored expiry in Vault and refreshes using Twitch OAuth if expired.
    public async Task<string?> GetAuthorizationToken(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ArgumentNullException(nameof(channel));
        }

        try
        {
            var accessKey = $"twitch:access_token:{channel}";
            var refreshKey = $"twitch:refresh_token:{channel}";
            var expiresKey = $"twitch:expires_at:{channel}";

            var access = await vaultService.GetSecret(accessKey);
            var refresh = await vaultService.GetSecret(refreshKey);
            var expiresAtRaw = await vaultService.GetSecret(expiresKey);

            DateTimeOffset expiresAt = DateTimeOffset.MinValue;
            if (!string.IsNullOrWhiteSpace(expiresAtRaw) && DateTimeOffset.TryParse(expiresAtRaw, out var parsed))
            {
                expiresAt = parsed;
            }

            // small safety buffer
            var buffer = TimeSpan.FromSeconds(30);
            if (!string.IsNullOrWhiteSpace(access) && expiresAt > DateTimeOffset.UtcNow + buffer)
            {
                return access;
            }

            // no refresh token available -> return existing access if present (may be expired) or null
            if (string.IsNullOrWhiteSpace(refresh))
            {
                logger.LogWarning("No refresh token available for Twitch channel {Channel}; returning existing access token (may be expired).", channel);
                return access;
            }

            // attempt refresh via Twitch token endpoint
            var clientId = configuration["Twitch:ClientId"];
            var clientSecret = configuration["Twitch:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                logger.LogError("Twitch ClientId/ClientSecret missing; cannot refresh token for channel {Channel}.", channel);
                return access;
            }

            var api = new TwitchLib.Api.TwitchAPI();
            api.Settings.ClientId = clientId;
            api.Settings.Secret = clientSecret;
            var refreshed = await api.Auth.RefreshAuthTokenAsync(refresh, clientSecret);

            string? newAccess = refreshed.AccessToken;
            string? newRefresh = refreshed.RefreshToken;
            long? expiresIn = refreshed.ExpiresIn;
            string? scopeStr = refreshed.Scopes != null ? string.Join(' ', refreshed.Scopes) : null;

            // update vault with new values if present
            if (!string.IsNullOrWhiteSpace(newAccess))
            {
                await vaultService.UpsertSecret(accessKey, newAccess);
                access = newAccess;
            }
            if (!string.IsNullOrWhiteSpace(newRefresh))
            {
                await vaultService.UpsertSecret(refreshKey, newRefresh);
            }
            if (expiresIn.HasValue)
            {
                var newExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value);
                await vaultService.UpsertSecret(expiresKey, newExpiresAt.ToString("o"));
            }
            if (!string.IsNullOrWhiteSpace(scopeStr))
            {
                await vaultService.UpsertSecret($"twitch:scope:{channel}", scopeStr);
            }

            return access;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error obtaining/refreshing Twitch authorization token for channel {Channel}.", channel);
            return null;
        }
    }
}