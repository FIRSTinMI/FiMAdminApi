using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Oauth2.v2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3.Data;

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
    /// Create a new live broadcast and ensure it is bound to a live stream. By default this binds to
    /// the account's existing (default) live stream. If <paramref name="createNewStreamKey"/> is true
    /// a new live stream (and therefore a new stream key) will be created and the broadcast will be
    /// bound to that stream. Optional scheduled start and end datetimes can be provided.
    /// </summary>
    public async Task<YoutubeIngestionInfo?> CreateNewLiveStreamAsync(
        string acctEmail, 
        string title, 
        string description, 
        DateTimeOffset? scheduledStart = null,
        byte[]? thumbnailBytes = null, 
        bool createNewStreamKey = false,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(acctEmail)) throw new ArgumentNullException(nameof(acctEmail));

        var accessToken = await GetAuthorizationToken(acctEmail, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogError("No access token available for account {Email}; cannot create livestream.", acctEmail);
            return null;
        }

        try
        {
            var cred = GoogleCredential.FromAccessToken(accessToken);
            var yt = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred,
                ApplicationName = "FiMAdminApi"
            });

            Google.Apis.YouTube.v3.Data.LiveStream? streamToUse = null;

            if (createNewStreamKey)
            {
                var newStream = new Google.Apis.YouTube.v3.Data.LiveStream
                {
                    Snippet = new Google.Apis.YouTube.v3.Data.LiveStreamSnippet
                    {
                        Title = string.IsNullOrWhiteSpace(title) ? "New Stream" : title + " Stream",
                        Description = description
                    },
                    Cdn = new Google.Apis.YouTube.v3.Data.CdnSettings
                    {
                        IngestionType = "rtmp",
                        Resolution = "variable",
                        Format = "1080p"
                    }
                };

                var insertStreamReq = yt.LiveStreams.Insert(newStream, "snippet,cdn");
                streamToUse = await insertStreamReq.ExecuteAsync(cancellationToken);
            }
            else
            {
                var listReq = yt.LiveStreams.List("id,cdn,snippet");
                listReq.Mine = true;
                var listResp = await listReq.ExecuteAsync(cancellationToken);
                streamToUse = listResp.Items?.FirstOrDefault();

                if (streamToUse is null)
                {
                    var fallbackStream = new Google.Apis.YouTube.v3.Data.LiveStream
                    {
                        Snippet = new Google.Apis.YouTube.v3.Data.LiveStreamSnippet
                        {
                            Title = string.IsNullOrWhiteSpace(title) ? "Default Stream" : title + " Stream",
                            Description = description
                        },
                        Cdn = new Google.Apis.YouTube.v3.Data.CdnSettings
                        {
                            IngestionType = "rtmp",
                            Resolution = "variable",
                            Format = "1080p"
                        }
                    };

                    streamToUse = await yt.LiveStreams.Insert(fallbackStream, "snippet,cdn").ExecuteAsync(cancellationToken);
                }
            }

            if (streamToUse?.Id is null)
            {
                logger.LogError("Failed to obtain or create a live stream for account {Email}.", acctEmail);
                return null;
            }

            // Create the broadcast
            var start = scheduledStart ?? DateTimeOffset.UtcNow;
            var end = new DateTimeOffset(start.Year, start.Month, start.Day, 23, 59, 59, start.Offset);

            var broadcast = new Google.Apis.YouTube.v3.Data.LiveBroadcast
            {
                Snippet = new Google.Apis.YouTube.v3.Data.LiveBroadcastSnippet
                {
                    Title = title,
                    Description = description,
                    ScheduledStartTimeDateTimeOffset = start,
                    ScheduledEndTimeDateTimeOffset = end
                },
                Status = new Google.Apis.YouTube.v3.Data.LiveBroadcastStatus
                {
                    PrivacyStatus = "public"
                },
                ContentDetails = new Google.Apis.YouTube.v3.Data.LiveBroadcastContentDetails
                {
                    EnableAutoStart = true,
                    EnableAutoStop = false,
                }
            };

            // Do not include contentDetails during insert (some accounts/APIs reject that part on insert).
            // We'll set contentDetails via a separate update call after creating and binding the broadcast.

            var insertBroadcastReq = yt.LiveBroadcasts.Insert(broadcast, "snippet,status,contentDetails");
            var broadcastResp = await insertBroadcastReq.ExecuteAsync(cancellationToken);

            // Bind the broadcast to the chosen stream
            var bindReq = yt.LiveBroadcasts.Bind(broadcastResp.Id, "id,contentDetails");
            bindReq.StreamId = streamToUse.Id;
            await bindReq.ExecuteAsync(cancellationToken);

            // Upload thumbnail if provided
            if (thumbnailBytes is not null && thumbnailBytes.Length > 0)
            {
                using var ms = new MemoryStream(thumbnailBytes);
                var thumbReq = yt.Thumbnails.Set(broadcastResp.Id, ms, "image/jpeg");
                var progress = await thumbReq.UploadAsync(cancellationToken);
                if (progress.Status != UploadStatus.Completed)
                {
                    logger.LogWarning("Thumbnail upload did not complete successfully. Status: {Status}", progress.Status);
                }
            }

            var ingestion = streamToUse.Cdn?.IngestionInfo;
            return new YoutubeIngestionInfo(
                RtmpIngestionAddress: ingestion?.IngestionAddress ?? string.Empty,
                StreamName: ingestion?.StreamName ?? string.Empty,
                ChannelId: streamToUse.Snippet?.ChannelId,
                BroadcastId: broadcastResp.Id
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating new livestream for account {Email}.", acctEmail);
            return null;
        }
    }

    /// <summary>
    /// Update an existing live stream/broadcast for the account. Attempts to find a matching broadcast
    /// by scheduled start time or title and update its metadata + thumbnail. Returns ingestion info
    /// (RTMP address / stream name / channel id / broadcast id) on success, or null if no suitable
    /// broadcast was found or the update failed.
    /// </summary>
    public async Task<YoutubeIngestionInfo?> UpdateExistingLiveStreamAsync(
        string acctEmail,
        string broadcastId,
        string title,
        string description,
        DateTimeOffset? scheduledStart = null,
        byte[]? thumbnailBytes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(acctEmail)) throw new ArgumentNullException(nameof(acctEmail));
        if (string.IsNullOrWhiteSpace(broadcastId)) throw new ArgumentNullException(nameof(broadcastId));
        var accessToken = await GetAuthorizationToken(acctEmail, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogError("No access token available for account {Email}; cannot update livestream.", acctEmail);
            return null;
        }

        try
        {
            // Directly update the provided broadcast id
            var updated = await UpdateExistingBroadcastAsync(acctEmail, broadcastId, title, description, scheduledStart, thumbnailBytes, cancellationToken);
            if (!updated)
            {
                logger.LogWarning("Failed to update existing broadcast {BroadcastId} for account {Email}.", broadcastId, acctEmail);
                return null;
            }

            // Retrieve ingestion info (default live stream) to return stream name/RTMP address
            var ingestion = await GetDefaultStreamIngestionInfo(acctEmail);
            return new YoutubeIngestionInfo(
                RtmpIngestionAddress: ingestion?.RtmpIngestionAddress ?? string.Empty,
                StreamName: ingestion?.StreamName ?? string.Empty,
                ChannelId: ingestion?.ChannelId,
                BroadcastId: broadcastId
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while locating/updating existing broadcast for account {Email}.", acctEmail);
            return null;
        }
    }
    
    /// <summary>
    /// Delete a live broadcast (live stream event) for the authorized account.
    /// `broadcastId` should be the broadcast id (the id returned when creating/listing broadcasts).
    /// Returns true if deletion succeeded (or the broadcast didn't exist), false on error.
    /// </summary>
    public async Task<bool> DeleteLiveBroadcastAsync(string acctEmail, string broadcastId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(acctEmail)) throw new ArgumentNullException(nameof(acctEmail));
        if (string.IsNullOrWhiteSpace(broadcastId)) throw new ArgumentNullException(nameof(broadcastId));

        var accessToken = await GetAuthorizationToken(acctEmail, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogError("No access token available for account {Email}; cannot delete livestream.", acctEmail);
            return false;
        }

        try
        {
            var cred = GoogleCredential.FromAccessToken(accessToken);
            var yt = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred,
                ApplicationName = "FiMAdminApi"
            });

            // Attempt to delete the broadcast by id. If it does not exist, the API may return a 404 which will throw;
            // we treat non-existence as success for idempotency.
            try
            {
                var delReq = yt.LiveBroadcasts.Delete(broadcastId);
                await delReq.ExecuteAsync(cancellationToken);
            }
            catch (Google.GoogleApiException gae) when (gae.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.LogInformation("Broadcast {BroadcastId} not found for account {Email}; nothing to delete.", broadcastId, acctEmail);
                return true;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting livestream {BroadcastId} for account {Email}.", broadcastId, acctEmail);
            return false;
        }
    }

    /// <summary>
    /// Enable auto-stop for an existing live broadcast (event) for the authorized account.
    /// `broadcastId` should be the broadcast id returned when creating/listing broadcasts.
    /// Returns true on success, false on error.
    /// </summary>
    public async Task<bool> SetAutoStartStopAsync(bool autoStart, bool autoStop, string acctEmail, string broadcastId, LiveBroadcast? existingBroadcast = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(acctEmail)) throw new ArgumentNullException(nameof(acctEmail));
        if (string.IsNullOrWhiteSpace(broadcastId)) throw new ArgumentNullException(nameof(broadcastId));

        var accessToken = await GetAuthorizationToken(acctEmail, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogError("No access token available for account {Email}; cannot enable auto-stop.", acctEmail);
            return false;
        }

        try
        {
            var cred = GoogleCredential.FromAccessToken(accessToken);
            var yt = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred,
                ApplicationName = "FiMAdminApi"
            });

            if (existingBroadcast is null)
            {
                var existingBroadcastReq = yt.LiveBroadcasts.List("contentDetails");
                existingBroadcastReq.Id = broadcastId;
                existingBroadcast = (await existingBroadcastReq.ExecuteAsync(cancellationToken)).Items.Single();
            }

            var update = new LiveBroadcast
            {
                Id = broadcastId,
                ContentDetails = new LiveBroadcastContentDetails
                {
                    EnableAutoStop = autoStop,
                    EnableAutoStart = autoStart,
                    
                    // Persist any other populated values, if not passed in then existing values will be set to defaults
                    MonitorStream = new MonitorStreamInfo
                    {
                        EnableMonitorStream = existingBroadcast.ContentDetails.MonitorStream.EnableMonitorStream,
                        BroadcastStreamDelayMs = existingBroadcast.ContentDetails.MonitorStream.BroadcastStreamDelayMs
                    },
                    EnableClosedCaptions = existingBroadcast.ContentDetails.EnableClosedCaptions,
                    EnableDvr = existingBroadcast.ContentDetails.EnableDvr,
                    EnableEmbed = existingBroadcast.ContentDetails.EnableEmbed,
                    RecordFromStart = existingBroadcast.ContentDetails.RecordFromStart
                }
            };

            var req = yt.LiveBroadcasts.Update(update, "contentDetails");
            await req.ExecuteAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error enabling auto-stop for broadcast {BroadcastId} on account {Email}.", broadcastId, acctEmail);
            return false;
        }
    }

    /// <summary>
    /// Stop (complete) a live broadcast for the authorized account by transitioning its lifecycle to "complete".
    /// Returns true on success or if the broadcast does not exist; false on error.
    /// </summary>
    public async Task<bool> StopBroadcastAsync(string acctEmail, string broadcastId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(acctEmail)) throw new ArgumentNullException(nameof(acctEmail));
        if (string.IsNullOrWhiteSpace(broadcastId)) throw new ArgumentNullException(nameof(broadcastId));

        var accessToken = await GetAuthorizationToken(acctEmail, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogError("No access token available for account {Email}; cannot stop broadcast.", acctEmail);
            return false;
        }

        try
        {
            var cred = GoogleCredential.FromAccessToken(accessToken);
            var yt = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred,
                ApplicationName = "FiMAdminApi"
            });

            try
            {
                var existingBroadcastReq = yt.LiveBroadcasts.List("contentDetails");
                existingBroadcastReq.Id = broadcastId;
                var existingBroadcast = (await existingBroadcastReq.ExecuteAsync(cancellationToken)).Items.Single();

                if (existingBroadcast.Status.LifeCycleStatus == "live")
                {
                    // Transition the broadcast to the "complete" lifecycle state using the client library.
                    var transitionReq = yt.LiveBroadcasts.Transition(
                        Google.Apis.YouTube.v3.LiveBroadcastsResource.TransitionRequest.BroadcastStatusEnum.Complete,
                        broadcastId,
                        "id,status");
                    await transitionReq.ExecuteAsync(cancellationToken);
                }
                else
                {
                    await SetAutoStartStopAsync(false, false, acctEmail, broadcastId, existingBroadcast,
                        cancellationToken);
                }
                
            }
            catch (Google.GoogleApiException gae) when (gae.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.LogInformation("Broadcast {BroadcastId} not found for account {Email}; nothing to stop.", broadcastId, acctEmail);
                return true;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error stopping/transitioning broadcast {BroadcastId} for account {Email}.", broadcastId, acctEmail);
            return false;
        }
    }

    /// <summary>
    /// Retrieve the current broadcasts (first page) status for the authorized account.
    /// Returns an array of status records describing lifecycle state (e.g. "live", "ready", "complete")
    /// and basic scheduling info for each broadcast. Returns null on error or if token unavailable.
    /// </summary>
    public async Task<YoutubeBroadcastStatus[]?> GetCurrentBroadcastsStatusAsync(string acctEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(acctEmail)) throw new ArgumentNullException(nameof(acctEmail));

        var accessToken = await GetAuthorizationToken(acctEmail, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogWarning("No access token available for account {Email}; cannot query broadcast status.", acctEmail);
            return null;
        }

        try
        {
            var cred = GoogleCredential.FromAccessToken(accessToken);
            var yt = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred,
                ApplicationName = "FiMAdminApi"
            });

            var broadcastStatusReq = yt.LiveBroadcasts.List("id,snippet,contentDetails,status");
            broadcastStatusReq.BroadcastStatus = Google.Apis.YouTube.v3.LiveBroadcastsResource.ListRequest.BroadcastStatusEnum.All;
            var broadcastStatusResp = broadcastStatusReq.ExecuteAsync(cancellationToken);

            var streamStatusReq = yt.LiveStreams.List("id,status");
            streamStatusReq.MaxResults = 50;
            streamStatusReq.Mine = true;
            var streamStatusResp = streamStatusReq.ExecuteAsync(cancellationToken);

            await Task.WhenAll(broadcastStatusResp, streamStatusResp);
            
            var items = broadcastStatusResp.Result.Items;
            var streams = streamStatusResp.Result.Items.ToDictionary(i => i.Id);

            if (items == null || items.Count == 0)
            {
                return [];
            }

            // Map each broadcast item on the first page to a status record
            var statuses = items.Select(b =>
            {
                var stream = !string.IsNullOrEmpty(b.ContentDetails.BoundStreamId)
                    ? streams[b.ContentDetails.BoundStreamId]
                    : null;
                return new YoutubeBroadcastStatus(
                    BroadcastId: b.Id,
                    LifeCycleStatus: b.Status?.LifeCycleStatus,
                    PrivacyStatus: b.Status?.PrivacyStatus,
                    StreamStatus: stream?.Status.StreamStatus,
                    StreamHealth: stream?.Status.HealthStatus.ConfigurationIssues?
                        .Where(i => i.Severity is "warning" or "error").Select(i => i.Description)
                        .ToArray(),
                    IsLive: string.Equals(b.Status?.LifeCycleStatus, "live", StringComparison.OrdinalIgnoreCase) &&
                            (stream is null || (stream.Status.StreamStatus == "active" &&
                                                stream.Status.HealthStatus.Status == "good")),
                    AutoStart: b.ContentDetails.EnableAutoStart!.Value,
                    AutoStop: b.ContentDetails.EnableAutoStop!.Value,
                    ScheduledStartTime: b.Snippet?.ScheduledStartTimeDateTimeOffset,
                    ScheduledEndTime: b.Snippet?.ScheduledEndTimeDateTimeOffset
                );
            }).ToArray();

            return statuses;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving broadcast status for account {Email}.", acctEmail);
            return null;
        }
    }
    
    /// <summary>
    /// Return a valid access token for the given account identifier (email). Uses the stored access token if not expired,
    /// otherwise uses the stored refresh token to obtain a new access token and updates the vault.
    /// </summary>
    private async Task<string?> GetAuthorizationToken(string email, CancellationToken cancellationToken = default)
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
    /// Update an existing live broadcast's metadata (title, description, scheduled start) and optionally thumbnail.
    /// Also attempts to bind the broadcast to the account's default live stream and set contentDetails (auto-start/stop) via a separate update.
    /// </summary>
    private async Task<bool> UpdateExistingBroadcastAsync(
        string acctEmail,
        string broadcastId,
        string? title = null,
        string? description = null,
        DateTimeOffset? scheduledStart = null,
        byte[]? thumbnailBytes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(acctEmail)) throw new ArgumentNullException(nameof(acctEmail));
        if (string.IsNullOrWhiteSpace(broadcastId)) throw new ArgumentNullException(nameof(broadcastId));

        var accessToken = await GetAuthorizationToken(acctEmail, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogError("No access token available for account {Email}; cannot update livestream.", acctEmail);
            return false;
        }

        try
        {
            var cred = GoogleCredential.FromAccessToken(accessToken);
            var yt = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred,
                ApplicationName = "FiMAdminApi"
            });

            // Retrieve the existing broadcast
            var listReq = yt.LiveBroadcasts.List("id,snippet,contentDetails,status");
            listReq.Id = broadcastId;
            var listResp = await listReq.ExecuteAsync(cancellationToken);
            var broadcast = listResp.Items?.FirstOrDefault();
            if (broadcast is null)
            {
                logger.LogWarning("Broadcast {BroadcastId} not found for account {Email} when attempting update.", broadcastId, acctEmail);
                return false;
            }

            // Prepare snippet update
            var updateSnippet = new Google.Apis.YouTube.v3.Data.LiveBroadcastSnippet
            {
                Title = title ?? broadcast.Snippet?.Title,
                Description = description ?? broadcast.Snippet?.Description,
            };

            // If scheduledStart was provided, set scheduled start and default end-of-day end time
            if (scheduledStart.HasValue)
            {
                var start = scheduledStart.Value;
                var end = new DateTimeOffset(start.Year, start.Month, start.Day, 23, 59, 59, start.Offset);
                updateSnippet.ScheduledStartTimeDateTimeOffset = start;
                updateSnippet.ScheduledEndTimeDateTimeOffset = end;
            }

            var updateBroadcast = new Google.Apis.YouTube.v3.Data.LiveBroadcast
            {
                Id = broadcastId,
                Snippet = updateSnippet
            };

            var updateReq = yt.LiveBroadcasts.Update(updateBroadcast, "snippet");
            await updateReq.ExecuteAsync(cancellationToken);

            // Ensure the broadcast is bound to the account's default live stream so it uses the default stream key.
            try
            {
                var streamsReq = yt.LiveStreams.List("id,cdn,snippet");
                streamsReq.Mine = true;
                var streamsResp = await streamsReq.ExecuteAsync(cancellationToken);
                var defaultStream = streamsResp.Items?.FirstOrDefault();
                if (defaultStream?.Id is not null)
                {
                    var bindReq = yt.LiveBroadcasts.Bind(broadcastId, "id,contentDetails");
                    bindReq.StreamId = defaultStream.Id;
                    await bindReq.ExecuteAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to bind broadcast to default live stream; stream key may not be updated.");
            }

            // Upload thumbnail if provided.
            if (thumbnailBytes is not null && thumbnailBytes.Length > 0)
            {
                try
                {
                    using var ms = new MemoryStream(thumbnailBytes);
                    var thumbReq = yt.Thumbnails.Set(broadcastId, ms, "image/jpeg");
                    var progress = await thumbReq.UploadAsync(cancellationToken);
                    if (progress.Status != UploadStatus.Completed)
                    {
                        logger.LogWarning("Thumbnail upload did not complete successfully for broadcast {BroadcastId}. Status: {Status}", broadcastId, progress.Status);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Thumbnail upload failed for broadcast {BroadcastId}; continuing.", broadcastId);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating livestream metadata for account {Email}", acctEmail);
            return false;
        }
    }
    
    /// <summary>
    /// Retrieve the default/live stream ingestion information for the authorized account (RTMP/ingestionAddress and streamName).
    /// Returns null if no stream is found or on error.
    /// </summary>
    private async Task<YoutubeIngestionInfo?> GetDefaultStreamIngestionInfo(string acctEmail)
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
                ChannelId: channelId,
                BroadcastId: null
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving YouTube ingestion info for account {Email}.", acctEmail);
            return null;
        }
    }
}

public record YoutubeBroadcastStatus(
    string? BroadcastId,
    string? LifeCycleStatus,
    string? StreamStatus,
    string[]? StreamHealth,
    string? PrivacyStatus,
    bool IsLive,
    bool AutoStart,
    bool AutoStop,
    DateTimeOffset? ScheduledStartTime,
    DateTimeOffset? ScheduledEndTime
);

public record GoogleUserInfo(
    string? sub,
    string? email
);

public record YoutubeIngestionInfo(
    string RtmpIngestionAddress,
    string StreamName,
    string? ChannelId,
    string? BroadcastId
);