using FiMAdminApi.Clients;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Models;

namespace FiMAdminApi.Services;


public class EventStreamService(DataContext dataContext, IServiceProvider services, ILogger<EventStreamService> logger)
{
    public async Task CreateEventStreams(Event[] events)
    {
        foreach (var evt in events)
        {
            // Skip if no truck route
            if (evt.TruckRoute == null)
            {
                logger.LogWarning("Event {EventId} has no associated truck route; skipping stream creation", evt.Id);
                continue;
            }

            // get the truck route
            var truckRouteData = dataContext.TruckRoutes.FirstOrDefault(r => r.Id == evt.TruckRoute.Id);

            if (truckRouteData == null)
            {
                logger.LogWarning("Truck route {TruckRouteId} not found for event {EventId}; skipping stream creation", evt.TruckRoute.Id, evt.Id);
                continue;
            }

            // skip if no streaming config
            if (truckRouteData.StreamingConfig == null)
            {
                logger.LogWarning("Truck route {TruckRouteId} has no streaming config for event {EventId}; skipping stream creation", truckRouteData.Id, evt.Id);
                continue;
            }

            // skip if no streaming channel id or type
            if (string.IsNullOrWhiteSpace(truckRouteData.StreamingConfig.Channel_Id) ||
                string.IsNullOrWhiteSpace(truckRouteData.StreamingConfig.Channel_Type))
            {
                logger.LogWarning("Truck route {TruckRouteId} has incomplete streaming config for event {EventId}; skipping stream creation", truckRouteData.Id, evt.Id);
                continue;
            }

            // create the stream based on provider
            var streamKey = string.Empty;
            var rtmpUrl = string.Empty;
            var embedUrl = string.Empty;
            var channelUrl = string.Empty;
            var provider = truckRouteData.StreamingConfig.Channel_Type;


            var description = "";
            var descriptionShort = "";
            var prefix = "";
            switch (evt.SyncSource)
            {
                case Models.Enums.DataSources.FtcEvents:
                    prefix = "MI FTC";
                    description = $"https://ftc.events/{evt.Code}";
                    descriptionShort = $" ftc.events/{evt.Code}";
                    break;
                case Models.Enums.DataSources.FrcEvents:
                    prefix = "MI FRC";
                    description = $"https://frc.events/{evt.Code}";
                    descriptionShort = $" frc.events/{evt.Code}";
                    break;
                case Models.Enums.DataSources.BlueAlliance:
                    prefix = "MI FRC";
                    description = $"https://www.thebluealliance.com/event/{evt.Code}";
                    break;
                default:
                    prefix = "";
                    break;
            }

            // Actually create or update the stream
            if (provider == "twitch")
            {
                logger.LogInformation("Creating Twitch event stream for event {EventId}", evt.Id);
                var twitchService = services.GetService<TwitchService>();
                if (twitchService != null)
                {
                    var twitchSuccess = await twitchService.UpdateLivestreamInformation(truckRouteData.StreamingConfig.Channel_Id!, $"{prefix} {evt.Name}{descriptionShort}");
                    if (twitchSuccess)
                    {
                        streamKey = await twitchService.GetStreamKey(truckRouteData.StreamingConfig.Channel_Id!);
                        var config = services.GetService<IConfiguration>();
                        if (config != null)
                        {
                            var configuredRtmp = config["Twitch:RtmpUrl"];
                            if (!string.IsNullOrWhiteSpace(configuredRtmp))
                            {
                                rtmpUrl = configuredRtmp;
                            }
                            else
                            {
                                logger.LogWarning("Twitch:RtmpUrl not set in configuration; falling back to default RTMP URL for channel {ChannelId}", truckRouteData.StreamingConfig.Channel_Id);
                                rtmpUrl = "rtmp://use20.contribute.live-video.net/app/";
                            }
                        }
                        else
                        {
                            logger.LogWarning("IConfiguration service unavailable; falling back to default RTMP URL for channel {ChannelId}", truckRouteData.StreamingConfig.Channel_Id);
                            rtmpUrl = "rtmp://use20.contribute.live-video.net/app/";
                        }
                        embedUrl = $"https://player.twitch.tv/?channel={truckRouteData.StreamingConfig.Channel_Id}";
                        channelUrl = $"https://www.twitch.tv/{truckRouteData.StreamingConfig.Channel_Id}";
                    }
                }
            }
            else if (provider == "youtube")
            {
                logger.LogInformation("Creating YouTube event stream for event {EventId}", evt.Id);
                var youtubeService = services.GetService<YoutubeService>();
                if (youtubeService != null)
                {
                    try
                    {
                        var acctId = truckRouteData.StreamingConfig.Channel_Id!;
                        var youtubeSuccess = await youtubeService.UpdateLiveStreamNowAsync(acctId, $"{prefix} {evt.Name}", description);
                        if (!youtubeSuccess)
                        {
                            logger.LogWarning("Could not update YouTube live stream title for channel {ChannelId} when creating stream for event {EventId}", truckRouteData.StreamingConfig.Channel_Id, evt.Id);
                        }
                        var ingestion = await youtubeService.GetDefaultStreamIngestionInfo(acctId);
                        if (ingestion != null)
                        {
                            // Use the ingestion address as the RTMP URL and the stream name as the key
                            rtmpUrl = ingestion.RtmpIngestionAddress;
                            streamKey = ingestion.StreamName;

                            if (!string.IsNullOrWhiteSpace(ingestion.ChannelId))
                            {
                                // Embed the live stream by channel and set channel URL
                                embedUrl = $"https://www.youtube.com/embed/live_stream?channel={ingestion.ChannelId}";
                                channelUrl = $"https://www.youtube.com/channel/{ingestion.ChannelId}";
                            }
                        }
                        else
                        {
                            logger.LogWarning("Could not retrieve YouTube ingestion info for channel {ChannelId} when creating stream for event {EventId}", truckRouteData.StreamingConfig.Channel_Id, evt.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error while creating YouTube stream for event {EventId}", evt.Id);
                    }
                }
            }

            // Update stream key if it is not empty
            if (!string.IsNullOrEmpty(streamKey))
            {
                // get the first av cart that is assigned to this route
                var cart = dataContext.AvCarts.FirstOrDefault(e => e.TruckRouteId == truckRouteData.Id);
                if (cart != null)
                {
                    cart.SetFirstStreamInfo(rtmpUrl, streamKey);
                    await dataContext.SaveChangesAsync();
                }
            }

            if (!string.IsNullOrEmpty(embedUrl) && !string.IsNullOrEmpty(evt.Code) && evt.SyncSource == Models.Enums.DataSources.FtcEvents)
            {
                var oaClient = services.GetService<OrangeAllianceDataClient>();
                if (oaClient != null)
                {
                    // Attempt to find the TOA event key
                    var toaEventKey = await oaClient.GetEventKeyFromFTCEventsKey(evt.Code);
                    if (string.IsNullOrEmpty(toaEventKey))
                    {
                        logger.LogWarning("Could not find TOA event key for event {EventId} with FTCEvents key {FTCEventsKey}", evt.Id, evt.Code);
                        continue;
                    }
                    // Update the event stream
                    if (provider == null)
                    {
                        logger.LogWarning("Stream provider is null for event {EventId} with FTCEvents key {FTCEventsKey}", evt.Id, evt.Code);
                        continue;
                    }
                    await oaClient.UpdateEventStream(
                        toaEventKey,
                        truckRouteData.Name,
                        evt.Name,
                        provider,
                        embedUrl,
                        channelUrl,
                        evt.StartTime,
                        evt.EndTime
                    );
                }
            }
        }
    }
}
