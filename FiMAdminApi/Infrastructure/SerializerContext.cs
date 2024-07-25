using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Serialization;
using FiMAdminApi.Data.Models;
using FiMAdminApi.Endpoints;
using FiMAdminApi.Services;

namespace FiMAdminApi.Infrastructure;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(UsersEndpoints.UpdateUserRequest))]
[JsonSerializable(typeof(UpsertEventsService.UpsertFromDataSourceRequest))]
[JsonSerializable(typeof(UpsertEventsService.UpsertEventsResponse))]
[JsonSerializable(typeof(EventsEndpoints.UpdateBasicInfoRequest))]
[JsonSerializable(typeof(EventsEndpoints.UpsertEventStaffRequest))]
[JsonSerializable(typeof(EventsEndpoints.CreateEventNoteRequest))]
[JsonSerializable(typeof(User[]))]
[JsonSerializable(typeof(EventStaff))]
[JsonSerializable(typeof(EventNote))]
[JsonSerializable(typeof(HealthEndpoints.ThinHealthReport))]
public partial class SerializerContext : JsonSerializerContext
{
}