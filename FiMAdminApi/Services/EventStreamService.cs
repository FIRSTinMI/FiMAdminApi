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
            if (truckRouteData.Streaming_Config == null)
            {
                logger.LogWarning("Truck route {TruckRouteId} has no streaming config for event {EventId}; skipping stream creation", truckRouteData.Id, evt.Id);
                continue;
            }

            // create the stream based on provider
            var streamKey = string.Empty;
            var rtmpUrl = string.Empty;
            var embedUrl = string.Empty;
            var channelUrl = string.Empty;
            var provider = truckRouteData.Streaming_Config.Channel_Type;

            // Actually create or update the stream
            if (provider == "twitch")
            {
                logger.LogInformation("Creating Twitch event stream for event {EventId}", evt.Id);
                var twitchService = services.GetService<TwitchService>();
                if (twitchService != null)
                {
                    var twitchSuccess = await twitchService.UpdateLivestreamInformation(truckRouteData.Streaming_Config.Channel_Id!, evt.Name);
                    if (twitchSuccess)
                    {
                        streamKey = await twitchService.GetStreamKey(truckRouteData.Streaming_Config.Channel_Id!);
                        rtmpUrl = "rtmp://use20.contribute.live-video.net/app/"; // bad practice 101
                        embedUrl = $"https://player.twitch.tv/?channel={truckRouteData.Streaming_Config.Channel_Id}";
                        channelUrl = $"https://www.twitch.tv/{truckRouteData.Streaming_Config.Channel_Id}";
                    }
                }
            }
            else if (truckRouteData.Streaming_Config.Channel_Type == "youtube")
            {
               // TODO
            }

            // Update stream key if it is not empty
            if (!string.IsNullOrEmpty(streamKey))
            {
                // get the first av cart that is assigned to this route
                var cart = dataContext.AvCarts.FirstOrDefault(e => e.TruckRouteId == truckRouteData.Id);
                if (cart != null)
                {
                    await cart.SetFirstStreamInfo(rtmpUrl, streamKey);
                    await dataContext.SaveChangesAsync();
                }
            }

            // TODO: Don't hard code this to Orange Alliance
            if (!string.IsNullOrEmpty(embedUrl) && !string.IsNullOrEmpty(evt.Code))
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
                    await oaClient.UpdateEventStream(
                        toaEventKey,
                        truckRouteData.Name,
                        evt.Name,
                        provider!,
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

public enum StreamProvider
{
    Twitch,
    YouTube,
}