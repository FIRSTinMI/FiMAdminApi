using System.Text.Json;
using System.Text.Json.Serialization;
using FiMAdminApi.Clients.PlayoffTiebreaks;

namespace FiMAdminApi.Clients.Models.FrcEvents;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(GetTeams))]
[JsonSerializable(typeof(GetEvents))]
[JsonSerializable(typeof(GetSchedule))]
[JsonSerializable(typeof(GetMatches))]
[JsonSerializable(typeof(GetAlliances))]
[JsonSerializable(typeof(GetRankings))]
[JsonSerializable(typeof(FrcEvents2025Tiebreak.ScoreDetailResponse))]
internal partial class FrcEventsJsonSerializerContext : JsonSerializerContext
{
}