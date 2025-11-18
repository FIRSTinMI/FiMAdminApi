using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Oauth2.v2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3.Data;
using System.IO;

namespace FiMAdminApi.Services;

public class YoutubeService(IConfiguration configuration, ILogger<YoutubeService> logger, FiMAdminApi.Data.EfPgsql.VaultService vaultService)
{
    /// <summary>
    /// Build the Google OAuth2 authorization URL a user should visit to authorize this application.
    /// Reads `Google:ClientId`, `Google:ClientSecret`, `Google:RedirectUri` and optionally `Google:Scopes` (comma-separated)
    /// from configuration.
    /// </summary>
    public string GetAuthorizationUrl(string? state = null)
    {
        var clientId = configuration["Google:ClientId"];

        if (string.IsNullOrWhiteSpace(clientId))
            throw new ApplicationException("Google OAuth configuration (ClientId) is required");

        // Default scopes: YouTube Live plus OpenID/email/profile so we can call userinfo
        var scopes = new[] { "openid", "email", "profile", "https://www.googleapis.com/auth/youtube.force-ssl" };

        // Build authorization URL manually to avoid depending on request-url helpers.
        var authEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        var scopeParam = string.Join(" ", scopes);
        var query = new List<string>
        {
            $"response_type=code",
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"scope={Uri.EscapeDataString(scopeParam)}",
            $"access_type=offline",
            $"include_granted_scopes=true",
            $"prompt=consent"
        };
        if (!string.IsNullOrEmpty(state)) query.Add($"state={Uri.EscapeDataString(state)}");

        var url = authEndpoint + "?" + string.Join('&', query);
        return url;
    }

    /// <summary>
    /// Return a valid access token for the given account identifier (email). Uses the stored access token if not expired,
    /// otherwise uses the stored refresh token to obtain a new access token and updates the vault.
    /// </summary>
    public async Task<string?> GetAuthorizationToken(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentNullException(nameof(email));

        try
        {
            var accessKey = $"google:access_token:{email}";
            var refreshKey = $"google:refresh_token:{email}";
            var expiresKey = $"google:expires_at:{email}";

            var access = await vaultService.GetSecret(accessKey);
            var refresh = await vaultService.GetSecret(refreshKey);
            var expiresRaw = await vaultService.GetSecret(expiresKey);

            DateTimeOffset expiresAt = DateTimeOffset.MinValue;
            if (!string.IsNullOrWhiteSpace(expiresRaw) && DateTimeOffset.TryParse(expiresRaw, out var parsed))
            {
                expiresAt = parsed;
            }

            var buffer = TimeSpan.FromSeconds(30);
            if (!string.IsNullOrWhiteSpace(access) && expiresAt > DateTimeOffset.UtcNow + buffer)
            {
                return access;
            }

            // No refresh token available -> return existing access (may be expired) or null
            if (string.IsNullOrWhiteSpace(refresh))
            {
                logger.LogWarning("No refresh token available for Google account {Email}; returning existing access token (may be expired).", email);
                return access;
            }

            var clientId = configuration["Google:ClientId"];
            var clientSecret = configuration["Google:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                logger.LogError("Google ClientId/ClientSecret missing; cannot refresh token for account {Email}.", email);
                return access;
            }

            var scopes = new[] { "openid", "email", "profile", "https://www.googleapis.com/auth/youtube.force-ssl" };

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                },
                Scopes = scopes
            });

            var refreshed = await flow.RefreshTokenAsync("user", refresh, cancellationToken);
            if (refreshed is null)
            {
                logger.LogWarning("Refresh token exchange returned null for account {Email}.", email);
                return access;
            }

            var newAccess = refreshed.AccessToken;
            var newRefresh = refreshed.RefreshToken;

            try
            {
                if (!string.IsNullOrWhiteSpace(newAccess))
                {
                    await vaultService.UpsertSecret(accessKey, newAccess);
                }
                if (!string.IsNullOrWhiteSpace(newRefresh))
                {
                    await vaultService.UpsertSecret(refreshKey, newRefresh);
                }
                if (refreshed.ExpiresInSeconds.HasValue)
                {
                    var newExpiresAt = DateTime.UtcNow.AddSeconds(refreshed.ExpiresInSeconds.Value);
                    await vaultService.UpsertSecret(expiresKey, newExpiresAt.ToString("o"));
                }
                if (!string.IsNullOrWhiteSpace(refreshed.Scope))
                {
                    await vaultService.UpsertSecret($"google:scope:{email}", refreshed.Scope);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating vault with refreshed tokens for account {Email}", email);
            }

            return newAccess ?? access;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error obtaining/refreshing Google authorization token for account {Email}.", email);
            return null;
        }
    }

    /// <summary>
    /// Exchange an authorization code returned by Google for an access token and refresh token.
    /// Returns a <see cref="TokenResponse"/> containing access_token, refresh_token, expires_in, etc.
    /// </summary>
    public async Task<TokenResponse> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        var clientId = configuration["Google:ClientId"];
        var clientSecret = configuration["Google:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogError("Google ClientId/ClientSecret missing; cannot exchange code for token.");
            throw new InvalidOperationException("Google OAuth configuration incomplete.");
        }

        var scopes = new[] { "openid", "email", "profile", "https://www.googleapis.com/auth/youtube.force-ssl" };

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            },
            Scopes = scopes
        });

        // userId is used by the flow's data store; we don't persist here so a static id is fine
        var token = await flow.ExchangeCodeForTokenAsync("user", code, redirectUri, cancellationToken);
        return token;
    }

    /// <summary>
    /// Update the currently active "go live now" broadcast for the authorized account.
    /// You must pass a valid OAuth access token for the channel that owns the broadcast.
    /// </summary>
    /// <param name="acctEmail">Email of the account whose broadcast is being updated</param>
    /// <param name="title">New stream title</param>
    /// <param name="description">New stream description</param>
    /// <param name="thumbnailData">Optional image bytes for the stream thumbnail (JPEG/PNG). Use a byte[]; callers can create via File.ReadAllBytes or MemoryStream.</param>
    /// <returns>True if update succeeded; false otherwise.</returns>
    public async Task<bool> UpdateLiveStreamNowAsync(string acctEmail, string title, string description, byte[]? thumbnailData = null)
    {
        if (string.IsNullOrWhiteSpace(acctEmail)) throw new ArgumentNullException(nameof(acctEmail));

        var accessToken = await GetAuthorizationToken(acctEmail);

        try
        {
            var cred = GoogleCredential.FromAccessToken(accessToken);
            var yt = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred,
                ApplicationName = "FiMAdminApi"
            });

            // Find the active broadcast for this channel.
            // Request broadcasts by status (All) and include contentDetails and status so we can inspect LifeCycleStatus.
            var listReq = yt.LiveBroadcasts.List("id,snippet,contentDetails,status");
            listReq.BroadcastStatus = Google.Apis.YouTube.v3.LiveBroadcastsResource.ListRequest.BroadcastStatusEnum.All;
            var listResp = await listReq.ExecuteAsync();
            // Find the first broadcast where LifeCycleStatus is "ready"
            var broadcast = listResp.Items?.FirstOrDefault(b => b.Status?.LifeCycleStatus == "ready");
            if (broadcast is null)
            {
                logger.LogWarning("No active live broadcast found for channel when attempting to update livestream metadata.");
                return false;
            }

            // Update snippet only. Build a minimal LiveBroadcast object containing only Id and Snippet
            // so we don't send other parts (like status) in the update payload.
            var updateBroadcast = new Google.Apis.YouTube.v3.Data.LiveBroadcast
            {
                Id = broadcast.Id,
                Snippet = new Google.Apis.YouTube.v3.Data.LiveBroadcastSnippet
                {
                    Title = title,
                    Description = description
                }
            };

            var updateReq = yt.LiveBroadcasts.Update(updateBroadcast, "snippet");
            await updateReq.ExecuteAsync();

            // Ensure the broadcast is bound to the account's default live stream so it uses the default stream key.
            try
            {
                var streamsReq = yt.LiveStreams.List("id,cdn,snippet");
                streamsReq.Mine = true;
                var streamsResp = await streamsReq.ExecuteAsync();
                var defaultStream = streamsResp.Items?.FirstOrDefault();
                if (defaultStream?.Id is not null)
                {
                    // Bind the broadcast to this live stream (this makes the broadcast use the stream's ingestion info/streamName)
                    var bindReq = yt.LiveBroadcasts.Bind(broadcast.Id, "id,contentDetails");
                    bindReq.StreamId = defaultStream.Id;
                    await bindReq.ExecuteAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to bind broadcast to default live stream; stream key may not be updated.");
            }

            // Upload thumbnail if provided. Thumbnails API expects a video id; for live broadcasts the broadcast id is used.
            if (thumbnailData is not null && thumbnailData.Length > 0)
            {
                using var ms = new MemoryStream(thumbnailData);
                var thumbReq = yt.Thumbnails.Set(broadcast.Id, ms, "image/jpeg");
                var progress = await thumbReq.UploadAsync();
                if (progress.Status != UploadStatus.Completed)
                {
                    logger.LogWarning("Thumbnail upload did not complete successfully. Status: {Status}", progress.Status);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating live stream metadata");
            return false;
        }
    }

    /// <summary>
    /// Retrieve the Google account information associated with an OAuth access token.
    /// Returns null if the token is invalid or the request fails.
    /// </summary>
    public async Task<GoogleUserInfo?> GetUserFromAccessTokenAsync(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return null;

        try
        {
            // Use Google client libraries to build a credential from the raw access token
            var googleCred = GoogleCredential.FromAccessToken(accessToken);
            var oauth2 = new Oauth2Service(new BaseClientService.Initializer
            {
                HttpClientInitializer = googleCred
            });

            var userinfo = await oauth2.Userinfo.Get().ExecuteAsync();

            if (userinfo == null) return null;

            return new GoogleUserInfo(
                sub: userinfo.Id,
                email: userinfo.Email
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving Google user info from access token via client library");
            return null;
        }
    }

    /// <summary>
    /// Retrieve the default/live stream ingestion information for the authorized account (RTMP/ingestionAddress and streamName).
    /// Returns null if no stream is found or on error.
    /// </summary>
    public async Task<YoutubeIngestionInfo?> GetDefaultStreamIngestionInfo(string acctEmail)
    {
        var accessToken = await GetAuthorizationToken(acctEmail);
        if (string.IsNullOrWhiteSpace(accessToken)) return null;

        try
        {
            var cred = GoogleCredential.FromAccessToken(accessToken);
            var yt = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred,
                ApplicationName = "FiMAdminApi"
            });

            // List live streams for the authorized account. Prefer the first one (default/primary).
            var listReq = yt.LiveStreams.List("id,cdn,snippet");
            listReq.Mine = true;
            var listResp = await listReq.ExecuteAsync();
            var stream = listResp.Items?.FirstOrDefault();
            if (stream is null)
            {
                logger.LogWarning("No live streams found for Google account {Email} when attempting to retrieve ingestion info.", acctEmail);
                return null;
            }

            var ingestion = stream.Cdn?.IngestionInfo;
            if (ingestion == null)
            {
                logger.LogWarning("Live stream for account {Email} has no ingestion info.", acctEmail);
                return null;
            }

            var channelId = stream.Snippet?.ChannelId;

            return new YoutubeIngestionInfo(
                RtmpIngestionAddress: ingestion.IngestionAddress ?? string.Empty,
                StreamName: ingestion.StreamName ?? string.Empty,
                ChannelId: channelId
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving YouTube ingestion info for account {Email}.", acctEmail);
            return null;
        }
    }
}

public record GoogleUserInfo(
    string? sub,
    string? email
);

public record YoutubeIngestionInfo(
    string RtmpIngestionAddress,
    string StreamName,
    string? ChannelId
);