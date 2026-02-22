using FiMAdminApi.Clients;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.EventHandlers;
using FiMAdminApi.Events;
using FiMAdminApi.Models.Models;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.EventSync.Steps;

public class InitialSync(EventPublisher eventPublisher, DataContext dbContext) : EventSyncStep([EventStatus.NotStarted])
{
    public override async Task RunStep(Event evt, IDataClient dataClient)
    {
        evt.Status = EventStatus.AwaitingQuals;
        
        await SyncStreams(evt, dataClient);
        
        if (evt.EndTime > DateTime.UtcNow)
        {
            await eventPublisher.Publish(new EventStarted(evt));
        }
    }

    /// <summary>
    /// Pull any livestreams from the datasource into the DB, will not overwrite existing streams
    /// </summary>
    private async Task SyncStreams(Event evt, IDataClient dataClient)
    {
        if (evt.Season is null || string.IsNullOrEmpty(evt.Code)) return;
        
        var eventInfo = await dataClient.GetEventAsync(evt.Season, evt.Code);
        if (eventInfo is null || eventInfo.Webcasts.Length == 0) return;

        var dbStreams = await dbContext.EventStreams.Where(s => s.EventId == evt.Id).ToListAsync();

        var streamsToAdd = eventInfo.Webcasts.Where(w => !dbStreams.Any(d =>
            d.Platform == w.Platform && d.InternalId == w.InternalId && d.Channel == w.Channel)).ToList();

        if (streamsToAdd.Count == 0) return;
        
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(evt.TimeZone, out var timeZone))
            timeZone = TimeZoneInfo.Utc;

        foreach (var stream in streamsToAdd)
        {
            var startTime = evt.StartTime;
            var endTime = evt.EndTime;

            if (stream.Date is not null)
            {
                var dateTime = stream.Date.Value.ToDateTime(TimeOnly.MinValue);
                startTime = new DateTimeOffset(dateTime, timeZone.GetUtcOffset(dateTime)).UtcDateTime;
                endTime = startTime.AddDays(1).AddSeconds(-1);
            }

            dbContext.EventStreams.Add(new EventStream
            {
                EventId = evt.Id,
                Title = $"{evt.StartTime.Year} {evt.Name}{(startTime != evt.StartTime ? $" - {startTime.DayOfWeek.ToString()}" : "")}",
                Primary = true,
                Platform = stream.Platform,
                Channel = stream.Channel,
                Url = stream.Url,
                InternalId = stream.InternalId,
                StartTime = startTime,
                EndTime = endTime
            });
        }

        await dbContext.SaveChangesAsync();
    }
}