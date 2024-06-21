using System.ComponentModel;
using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Services;

public class UpsertEventsService(DataContext context, IServiceProvider services)
{
    private static readonly Random Random = new();
    
    public async Task<UpsertEventsResponse> UpsertFromDataSource(UpsertFromDataSourceRequest request)
    {
        var response = new UpsertEventsResponse();

        if (!(!string.IsNullOrWhiteSpace(request.DistrictCode) ^ request.EventCodes.Any()))
        {
            response.Errors.Add("Must provide either district code or event codes, but not both");
            return response;
        }

        var season = await GetAndValidateSeason(request.SeasonId, response);
        if (season is null)
        {
            return response;
        }

        var dbEvents = await context.Events.Where(e => e.SeasonId == season.Id).ToListAsync();

        var dataClient = request.DataSource switch
        {
            DataSources.FrcEvents => services.GetRequiredKeyedService<IDataClient>("FrcEvents"),
            DataSources.BlueAlliance => throw new ArgumentOutOfRangeException(),
            DataSources.OrangeAlliance => throw new ArgumentOutOfRangeException(),
            _ => throw new ArgumentOutOfRangeException()
        };

        List<Clients.Models.Event> apiEvents;
        if (!string.IsNullOrWhiteSpace(request.DistrictCode))
        {
            apiEvents = await dataClient.GetDistrictEventsAsync(season, request.DistrictCode.Trim());
        }
        else
        {
            // Fetch the events from the data source in parallel, `degreeOfParallelism` at a time
            const int degreeOfParallelism = 4;
            apiEvents = (await Task.WhenAll(request.EventCodes.AsParallel().WithDegreeOfParallelism(degreeOfParallelism)
                .Select(async code =>
                {
                    var resp = await dataClient.GetEventAsync(season, code.Trim());
                    if (resp is null) response.Errors.Add($"Event with code {code} not found");
                    return resp;
                }))).Where(e => e is not null).Select(e => e!).ToList();
        }

        if (apiEvents.Count == 0)
        {
            response.Warnings.Add("No events to create");
            return response;
        }

        foreach (var apiEvent in apiEvents)
        {
            var dbEvent = dbEvents.FirstOrDefault(e => e.Code == apiEvent.EventCode);
            if (dbEvent is not null)
            {
                if (!request.OverrideExisting)
                {
                    response.Warnings.Add($"Found existing event {apiEvent.EventCode}, skipping");
                    continue;
                }

                dbEvent.Name = apiEvent.Name;
                dbEvent.StartTime = apiEvent.StartTime.UtcDateTime;
                dbEvent.EndTime = apiEvent.EndTime.UtcDateTime;
                context.Events.Update(dbEvent);
                response.UpsertedEvents.Add(dbEvent);
            }
            else
            {
                var newEvent = new Event
                {
                    Id = new Guid(),
                    SeasonId = season.Id,
                    Key = GenerateEventKey(),
                    Code = apiEvent.EventCode,
                    Name = apiEvent.Name,
                    IsOfficial = false,
                    StartTime = apiEvent.StartTime.UtcDateTime,
                    EndTime = apiEvent.EndTime.UtcDateTime,
                    TimeZone = apiEvent.TimeZone.Id,
                    Status = "NotStarted",
                    SyncSource = request.DataSource
                };

                context.Events.Add(newEvent);
                response.UpsertedEvents.Add(newEvent);
            }
        }

        await context.SaveChangesAsync();
        return response;
    }

    private async Task<Season?> GetAndValidateSeason(int seasonId, UpsertEventsResponse response)
    {
        var season = await context.Seasons.FindAsync(seasonId);
        if (season is null)
        {
            response.Errors.Add("Season not found");
            return null;
        }

        if (season.EndTime.ToUniversalTime() < DateTime.UtcNow)
        {
            response.Errors.Add("Cannot add events to a season which has already ended");
            return null;
        }

        return season;
    }

    private static string GenerateEventKey()
    {
        // Removes any ambiguous characters
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        const int length = 10;
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[Random.Next(s.Length)]).ToArray());
    }
    
    public class UpsertFromDataSourceRequest
    {
        [Description(
            "If true, re-fetch data from the sync source and update in the database. "+
            "If false, events which already exist will cause a warning.")]
        public bool OverrideExisting { get; set; }
        
        [Description("Note: This is not the year of the event")]
        public int SeasonId { get; set; }
        
        public DataSources DataSource { get; set; }
        public string? DistrictCode { get; set; }
        public IEnumerable<string> EventCodes { get; set; } = [];
    }

    public class UpsertEventsResponse
    {
        public List<string> Errors { get; init; } = [];
        public List<string> Warnings { get; init; } = [];
        public List<Event> UpsertedEvents { get; init; } = [];
        public bool IsSuccess => Errors.Count == 0 && Warnings.Count == 0;
    }
}