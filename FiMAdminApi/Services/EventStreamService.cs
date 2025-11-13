using FiMAdminApi.Models.Models;
using Google.Apis.Auth.OAuth2;

namespace FiMAdminApi.Services;


public class EventStreamService(IConfiguration configuration, ILogger<EventStreamService> logger)
{
    public async Task CreateEventStreams(Event[] events, StreamProvider provider)
    {
        foreach (var evt in events)
        {
            if (provider == StreamProvider.Twitch)
            {
                logger.LogInformation("Creating Twitch event stream for event {EventId}", evt.Id);
                // Logic to create Twitch event stream
            }
            else if (provider == StreamProvider.YouTube)
            {
                logger.LogInformation("Creating YouTube event stream for event {EventId}", evt.Id);
                try
                {
                    var accessToken = configuration["YouTube:AccessToken"];
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        logger.LogWarning("YouTube access token not configured; skipping YouTube stream creation for event {EventId}", evt.Id);
                        continue;
                    }

                    var accountCred = await GoogleCredential.GetApplicationDefaultAsync();

                    using var youtube = new Google.Apis.YouTube.v3.YouTubeService(new Google.Apis.Services.BaseClientService.Initializer
                    {
                        HttpClientInitializer = accountCred.CreateScoped(
                            "https://www.googleapis.com/auth/youtube",
                            "https://www.googleapis.com/auth/youtube.force-ssl"),
                        ApplicationName = "FiMAdminApi"
                    });

                    // Schedule times (replace with evt-specific times if available)
                    var scheduledStart = evt.StartTime;
                    var scheduledEnd = evt.EndTime;

                    // Create a LiveBroadcast (scheduled event)
                    var broadcast = new Google.Apis.YouTube.v3.Data.LiveBroadcast
                    {
                        Snippet = new Google.Apis.YouTube.v3.Data.LiveBroadcastSnippet
                        {
                            Title = $"Scheduled Stream for {evt.Id}",
                            ScheduledStartTimeRaw = scheduledStart.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        },
                        Status = new Google.Apis.YouTube.v3.Data.LiveBroadcastStatus
                        {
                            PrivacyStatus = "public"
                        }
                    };

                    var insertBroadcastReq = youtube.LiveBroadcasts.Insert(broadcast, "snippet,status,contentDetails");
                    var createdBroadcast = await insertBroadcastReq.ExecuteAsync();

                    // Create a LiveStream (ingestion target)
                    var stream = new Google.Apis.YouTube.v3.Data.LiveStream
                    {
                        Snippet = new Google.Apis.YouTube.v3.Data.LiveStreamSnippet
                        {
                            Title = $"{evt.Name}"
                        },
                        Cdn = new Google.Apis.YouTube.v3.Data.CdnSettings
                        {
                            Format = "1080p",
                            IngestionType = "rtmp"
                        }
                    };

                    var insertStreamReq = youtube.LiveStreams.Insert(stream, "snippet,cdn");
                    var createdStream = await insertStreamReq.ExecuteAsync();

                    // Bind the broadcast to the stream
                    var bindReq = youtube.LiveBroadcasts.Bind(createdBroadcast.Id, "id,contentDetails");
                    bindReq.StreamId = createdStream.Id;
                    var bound = await bindReq.ExecuteAsync();

                    logger.LogInformation("Created YouTube broadcast {BroadcastId} bound to stream {StreamId} for event {EventId}", createdBroadcast.Id, createdStream.Id, evt.Id);
                }
                catch (Google.GoogleApiException gex)
                {
                    logger.LogError(gex, "Google API error while creating YouTube event stream for {EventId}", evt.Id);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error while creating YouTube event stream for {EventId}", evt.Id);
                }
            }
        }
    }
}

public enum StreamProvider
{
    Twitch,
    YouTube,
}