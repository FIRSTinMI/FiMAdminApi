using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiMAdminApi.Clients.Models.BlueAlliance;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GetMatches[]))]
internal partial class BlueAllianceJsonSerializerContext : JsonSerializerContext
{
}