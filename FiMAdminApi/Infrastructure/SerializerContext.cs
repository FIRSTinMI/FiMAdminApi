using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Serialization;
using FiMAdminApi.Data.Models;
using FiMAdminApi.Endpoints;
using FiMAdminApi.EventSync;
using FiMAdminApi.Services;

namespace FiMAdminApi.Infrastructure;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(UsersEndpoints.UpdateUserRequest))]
[JsonSerializable(typeof(UpsertEventsService.UpsertFromDataSourceRequest))]
[JsonSerializable(typeof(UpsertEventsService.UpsertEventsResponse))]
[JsonSerializable(typeof(EventSyncResult))]
[JsonSerializable(typeof(EventsEndpoints.UpdateBasicInfoRequest))]
[JsonSerializable(typeof(EventsEndpoints.UpsertEventStaffRequest))]
[JsonSerializable(typeof(EventsEndpoints.CreateEventNoteRequest))]
[JsonSerializable(typeof(User[]))]
[JsonSerializable(typeof(EventStaff))]
[JsonSerializable(typeof(EventsEndpoints.EventStaffInfo[]))]
[JsonSerializable(typeof(EventNote))]
[JsonSerializable(typeof(HealthEndpoints.ThinHealthReport))]
[JsonSerializable(typeof(TruckRoute))]
[JsonSerializable(typeof(TruckRoutesEndpoints.CreateTruckRoute))]
[JsonSerializable(typeof(TruckRoutesEndpoints.EditTruckRoute))]
public partial class SerializerContext : JsonSerializerContext
{
}