using FiMAdminApi.Clients;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Models;
using Microsoft.EntityFrameworkCore;

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
            var streams = new List<StreamInfo>();
            var provider = truckRouteData.StreamingConfig.Channel_Type;


            var description = "";
            var descriptionShort = "";
            var prefix = "";
            var program = "";
            switch (evt.SyncSource)
            {
                case Models.Enums.DataSources.FtcEvents:
                    prefix = $"{evt.StartTime:yyyy} MI FTC";
                    description = $"https://ftc.events/{evt.Code}";
                    program = "FTC";
                    descriptionShort = $" ftc.events/{evt.Code}";
                    break;
                case Models.Enums.DataSources.FrcEvents:
                    prefix = $"{evt.StartTime:yyyy} MI FRC";
                    description = $"https://frc.events/{evt.Code}";
                    program = "FRC";
                    descriptionShort = $" frc.events/{evt.Code}";
                    break;
                case Models.Enums.DataSources.BlueAlliance:
                    prefix = $"{evt.StartTime:yyyy} MI FRC";
                    description = $"https://www.thebluealliance.com/event/{evt.Code}";
                    program = "FRC";
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
                        streams.Add(new StreamInfo
                        {
                            embedUrl = $"https://player.twitch.tv/?channel={truckRouteData.StreamingConfig.Channel_Id}",
                            channelUrl = $"https://www.twitch.tv/{truckRouteData.StreamingConfig.Channel_Id}",
                            streamName = $"{prefix} {evt.Name}",
                            startTime = evt.StartTime,
                            endTime = evt.EndTime,
                            internalId = truckRouteData.StreamingConfig.Channel_Id,
                            isUpdate = false
                        });
                    }
                }
            }
            else if (provider == "youtube")
            {
                logger.LogInformation("Creating YouTube event stream for event {EventId}", evt.Id);
                var youtubeService = services.GetService<YoutubeService>();
                var thumbnailService = services.GetService<ThumbnailService>();

                // Get all livestreams that exist for the current event
                var existingStreams = dataContext.EventStreams?
                    .Where(es => es.EventId == evt.Id && es.Platform == Models.Enums.StreamPlatform.Youtube)
                    .ToList();
                if (youtubeService != null && thumbnailService != null)
                {
                    logger.LogInformation("YouTubeService and ThumbnailService available; proceeding to create YouTube stream for event {EventId}", evt.Id);
                    try
                    {
                        // Count number of days that the event is going on
                        var eventDuration = Math.Ceiling((evt.EndTime - evt.StartTime).TotalDays) - 2; // -1 day for "buffer" day, -1 because math is hard.
                        var acctId = truckRouteData.StreamingConfig.Channel_Id!;

                        logger.LogInformation("YouTube account ID for event {EventId} is {AcctId}", evt.Id, acctId);
                        logger.LogInformation("Event {EventId} duration is {EventDuration} days", evt.Id, eventDuration);

                        // List of streams to be created
                        var youtubeStreams = new List<YoutubeIngestionInfo>();

                        // Create the streams
                        for (int day = 0; day <= eventDuration; day++)
                        {
                            var dayName = eventDuration > 0 ? (day == 0 ? "Practice Day" : $"Day {day}") : "";
                            var daySuffix = !string.IsNullOrEmpty(dayName) ? $" - {dayName}" : "";
                            var streamDate = evt.StartTime.AddDays(day + 1);
                            var streamTitle = $"{prefix} {evt.Name}{daySuffix}";
                            var streamDesc = description;
                            var thumbnail = await thumbnailService.DrawThumbnailAsync(program, $"{evt.StartTime:yyyy} {program}", $"{evt.Name}", dayName);

                            logger.LogInformation("Creating YouTube stream for event {EventId} on day {Day} with title '{StreamTitle}' starting at {StreamDate}", evt.Id, day, streamTitle, streamDate);

                            // Determine if a stream already exists and we should update, or create new.
                            // Look for existing streams
                            var exactExisting = existingStreams != null ?
                                          existingStreams.FindAll(es =>
                                              (es.Title != null && es.Title.EndsWith(daySuffix, StringComparison.OrdinalIgnoreCase))
                                              || (es.StartTime != null && DateTimeOffset.Compare((DateTimeOffset)es.StartTime, streamDate) == 0))
                                              : new List<EventStream>();

                            // 1. Stream exists if eventDuration is 0 and there is 1 existing stream
                            var exists1 = existingStreams != null &&
                                          eventDuration == 0 &&
                                          existingStreams.Count == 1;
                            // 2. Look for stream with identical suffixes or start dates
                            var exists2 = existingStreams != null && exactExisting.Count > 0;

                            YoutubeIngestionInfo? youtubeStream = null;
                            if (exists1 || exists2)
                            {
                                logger.LogInformation("Existing YouTube stream found for event {EventId} on day {Day}; updating stream", evt.Id, day);

                                // Choose DB record to update
                                EventStream? existingToUpdate = exactExisting.FirstOrDefault();

                                if (existingToUpdate != null && !string.IsNullOrWhiteSpace(existingToUpdate.InternalId))
                                {
                                    youtubeStream = await youtubeService.UpdateExistingLiveStreamAsync(acctId, existingToUpdate.InternalId, streamTitle, streamDesc, streamDate, thumbnail);

                                    // Update the db
                                    if (youtubeStream != null && existingToUpdate != null && dataContext.EventStreams != null)
                                    {
                                        try
                                        {
                                            await dataContext.EventStreams.Where(es => es.Id == existingToUpdate.Id).ExecuteUpdateAsync(s => 
                                                s.SetProperty(e => e.Title, streamTitle)
                                                    .SetProperty(e => e.StartTime, streamDate)
                                                    // TODO: we should put together StreamInfos first so we can ensure consistency
                                                    .SetProperty(e => e.EndTime, streamDate.AddDays(1))
                                                    .SetProperty(e => e.Url, $"https://www.youtube.com/embed/{youtubeStream.BroadcastId}")
                                            );
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.LogError(ex, "Failed to update EventStream record {EventStreamId} for event {EventId}", existingToUpdate.Id, evt.Id);
                                        }
                                    }
                                }
                                else
                                {
                                    logger.LogInformation("Could not resolve broadcast id for existing stream; creating new stream instead for event {EventId} day {Day}", evt.Id, day);
                                    youtubeStream = await youtubeService.CreateNewLiveStreamAsync(acctId, streamTitle, streamDesc, streamDate, thumbnail);
                                }
                            }
                            else
                            {
                                // We can't create a stream that starts earlier than the current time.  If the stream is BEFORE TODAY, we'll just ignore it.
                                // If the stream is for TODAY, we'll just set the start time to the nearest hour in the future.
                                if (streamDate < DateTime.UtcNow)
                                {
                                    if (streamDate.Date < DateTime.UtcNow.Date)
                                    {
                                        logger.LogInformation("Skipping creation of YouTube stream for event {EventId} on day {Day} since start time {StreamDate} is in the past", evt.Id, day, streamDate);
                                        continue;
                                    }
                                    else
                                    {
                                        // set to nearest hour in future
                                        var now = DateTime.Now;
                                        streamDate = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1).ToUniversalTime();
                                        logger.LogInformation("Adjusting YouTube stream start time for event {EventId} on day {Day} to nearest hour in future: {StreamDate}", evt.Id, day, streamDate);
                                    }
                                }
                                logger.LogInformation("No existing YouTube stream found for event {EventId} on day {Day}; creating new stream", evt.Id, day);
                                youtubeStream = await youtubeService.CreateNewLiveStreamAsync(acctId, streamTitle, streamDesc, streamDate, thumbnail);
                            }

                            if (youtubeStream != null)
                            {
                                youtubeStreams.Add(youtubeStream);
                                streams.Add(new StreamInfo
                                {
                                    embedUrl = $"https://www.youtube.com/embed/{youtubeStream.BroadcastId}",
                                    channelUrl = $"https://www.youtube.com/channel/{youtubeStream.ChannelId}",
                                    streamName = streamTitle,
                                    startTime = streamDate,
                                    endTime = streamDate.AddHours(24),
                                    internalId = youtubeStream.BroadcastId,
                                    isUpdate = exists1 || exists2
                                });
                            }
                        }
                        var firstStream = youtubeStreams.FirstOrDefault();
                        if (firstStream != null)
                        {
                            // Use the ingestion address as the RTMP URL and the stream name as the key
                            rtmpUrl = firstStream.RtmpIngestionAddress;
                            streamKey = firstStream.StreamName;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error while creating YouTube stream(s) for event {EventId}", evt.Id);
                    }
                }
                else
                {
                    logger.LogError($"Cnnot create YouTube stream for event {evt.Id}. Missing YoutubeService or ThumbnailService.");
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

            if (streams.Any() && !string.IsNullOrEmpty(evt.Code) && evt.SyncSource == Models.Enums.DataSources.FtcEvents)
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
                    for (int i = 0; i < streams.Count; i++)
                    {
                        var streamSuffix = $"LS{i + 1}";
                        var url = provider == "twitch"
                            ? $"https://player.twitch.tv/?channel={truckRouteData.StreamingConfig.Channel_Id}"
                            : $"https://youtube.com/embed/{streams[i].internalId}";
                        var updateSuccess = await oaClient.UpdateEventStream(
                            toaEventKey,
                            streams[i].streamName,
                            streams[i].streamName,
                            provider,
                            url,
                            streams[i].channelUrl,
                            streams[i].startTime,
                            streams[i].endTime,
                            streamSuffix);
                        if (updateSuccess)
                        {
                            logger.LogInformation("Successfully updated event stream for event {EventId} with FTCEvents key {FTCEventsKey}, stream suffix {StreamSuffix}", evt.Id, evt.Code, streamSuffix);
                        }
                        else
                        {
                            logger.LogError("Failed to update event stream for event {EventId} with FTCEvents key {FTCEventsKey}, stream suffix {StreamSuffix}", evt.Id, evt.Code, streamSuffix);
                        }
                    }
                }
            }

            if (streams.Any())
            {
                try
                {

                    streams.ForEach(s =>
                        {
                            // Don't push database record if this is an update
                            if (s.isUpdate)
                            {
                                return;
                            }
                            var dbStream = new EventStream
                            {
                                EventId = evt.Id,
                                Channel = s.channelUrl,
                                InternalId = s.internalId,
                                Platform = provider == "twitch" ? Models.Enums.StreamPlatform.Twitch : Models.Enums.StreamPlatform.Youtube,
                                Title = s.streamName,
                                Url = s.embedUrl,
                                StartTime = s.startTime,
                                EndTime = s.endTime
                            };

                            dataContext.EventStreams?.Add(dbStream);
                        }
                    );

                    await dataContext.SaveChangesAsync();
                    logger.LogInformation("Inserted {Count} EventStream record(s) for event {EventId}", streams.Count, evt.Id);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to insert EventStream records for event {EventId}", evt.Id);
                }
            }
        }
    }

    /// <summary>
    /// Delete (or logically delete) a live event for the given EventStream id.
    /// For Twitch: rename the stream to "{channel_id}'s Live Stream" since Twitch broadcasts cannot be deleted.
    /// For YouTube: call into <see cref="YoutubeService.DeleteLiveBroadcastAsync"/> using the account email found on the truck route streaming config.
    /// If the provider action succeeds the EventStream record is removed from the database.
    /// </summary>
    public async Task<bool> DeleteLiveEventAsync(long eventStreamId, CancellationToken cancellationToken = default)
    {
        try
        {
            Event? evt = null;
    
            var stream = dataContext.EventStreams?.FirstOrDefault(s => s.Id == eventStreamId);
            if (stream == null)
            {
                logger.LogWarning("EventStream {StreamId} not found; nothing to delete", eventStreamId);
                return false;
            }

            switch (stream.Platform)
            {
                case Models.Enums.StreamPlatform.Twitch:
                    {
                        var twitchService = services.GetService<TwitchService>();
                        if (twitchService == null)
                        {
                            logger.LogError("TwitchService unavailable; cannot rename Twitch stream for EventStream {StreamId}", eventStreamId);
                            return false;
                        }

                        var channelId = stream.InternalId;
                        if (string.IsNullOrWhiteSpace(channelId))
                        {
                            logger.LogError("No channel id available for Twitch EventStream {StreamId}", eventStreamId);
                            return false;
                        }

                        var renameTitle = $"{channelId}'s Live Stream";
                        var renameSuccess = await twitchService.UpdateLivestreamInformation(channelId, renameTitle);
                        if (!renameSuccess)
                        {
                            logger.LogError("Failed to rename Twitch channel {ChannelId} for EventStream {StreamId}", channelId, eventStreamId);
                            return false;
                        }

                        // remove DB record
                        dataContext.EventStreams?.Remove(stream);
                        await dataContext.SaveChangesAsync(cancellationToken);
                        logger.LogInformation("Removed EventStream {StreamId} after Twitch rename", eventStreamId);
                        break;
                    }
                case Models.Enums.StreamPlatform.Youtube:
                    {
                        var youtubeService = services.GetService<YoutubeService>();
                        if (youtubeService == null)
                        {
                            logger.LogError("YoutubeService unavailable; cannot delete YouTube broadcast for EventStream {StreamId}", eventStreamId);
                            return false;
                        }

                        // Find the event -> truck route -> streaming config to get account email
                        evt = dataContext.Events?.FirstOrDefault(e => e.Id == stream.EventId);
                        if (evt == null || evt.TruckRouteId == null)
                        {
                            logger.LogError("Could not resolve Event or TruckRoute for EventStream {StreamId}; cannot delete YouTube broadcast", eventStreamId);
                            return false;
                        }

                        var truckRoute = dataContext.TruckRoutes.FirstOrDefault(t => t.Id == evt.TruckRouteId.Value);
                        var acctEmail = truckRoute?.StreamingConfig?.Channel_Id;
                        if (string.IsNullOrWhiteSpace(acctEmail))
                        {
                            logger.LogError("No YouTube account id/email found on TruckRoute for EventStream {StreamId}; cannot delete broadcast", eventStreamId);
                            return false;
                        }

                        var broadcastId = stream.InternalId;
                        if (string.IsNullOrWhiteSpace(broadcastId))
                        {
                            logger.LogError("No broadcast id (InternalId) on EventStream {StreamId}; cannot delete YouTube broadcast", eventStreamId);
                            return false;
                        }

                        var deleted = await youtubeService.DeleteLiveBroadcastAsync(acctEmail, broadcastId, cancellationToken);
                        if (!deleted)
                        {
                            logger.LogError("YouTube DeleteLiveBroadcastAsync failed for broadcast {BroadcastId} (EventStream {StreamId})", broadcastId, eventStreamId);
                            return false;
                        }

                        dataContext.EventStreams?.Remove(stream);
                        await dataContext.SaveChangesAsync(cancellationToken);
                        logger.LogInformation("Removed EventStream {StreamId} after YouTube broadcast deletion", eventStreamId);
                        break;
                    }
            }

            // Attempt to delete the stream from TOA if applicable
            if (evt == null)
            {
                evt = dataContext.Events?.FirstOrDefault(e => e.Id == stream.EventId);
            }

            if (evt != null && !string.IsNullOrEmpty(evt.Code) && evt.SyncSource == Models.Enums.DataSources.FtcEvents)
            {
                var oaClient = services.GetService<OrangeAllianceDataClient>();
                if (oaClient != null)
                {
                    try
                    {
                        // Attempt to find the TOA event key
                        var toaEventKey = await oaClient.GetEventKeyFromFTCEventsKey(evt.Code);
                        if (!string.IsNullOrEmpty(toaEventKey))
                        {
                            var streams = await oaClient.GetEventStreams(toaEventKey);
                            if (streams != null)
                            {
                                // iterate over streams
                                foreach (var toaStream in streams)
                                {
                                    if (toaStream == null) continue;
                                    try
                                    {
                                        if (toaStream.stream_name == stream.Title)
                                        {
                                            if (!string.IsNullOrWhiteSpace(toaStream.stream_key))
                                            {
                                                await oaClient.DeleteEventStream(toaStream.stream_key);
                                                logger.LogInformation("Deleted TOA event stream {StreamKey} for EventStream {StreamId}", toaStream.stream_key, eventStreamId);
                                            }
                                            else
                                            {
                                                logger.LogWarning("TOA stream matched by URL but has no stream_key; cannot delete (EventStream {StreamId})", eventStreamId);
                                            }
                                        }
                                    }
                                    catch (Exception innerEx)
                                    {
                                        logger.LogWarning(innerEx, "Failed to delete TOA event stream {StreamKey} for EventStream {StreamId}", toaStream.stream_key, eventStreamId);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Do not fail entire operation for TOA client errors; log and continue
                        logger.LogWarning(ex, "Failed to synchronize deletion with OrangeAlliance for event {EventCode}; continuing", evt.Code);
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting live event for EventStream {StreamId}", eventStreamId);
            return false;
        }
    }

}

public class StreamInfo
{
    public required string embedUrl { get; set; }
    public required string channelUrl { get; set; }
    public required string streamName { get; set; }
    public required DateTime startTime { get; set; }
    public required DateTime endTime { get; set; }
    public required string? internalId { get; set; }
    public required bool isUpdate { get; set; }
}