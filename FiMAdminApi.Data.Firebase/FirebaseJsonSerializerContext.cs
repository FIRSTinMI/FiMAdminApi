using System.Text.Json;
using System.Text.Json.Serialization;
using FiMAdminApi.Data.Firebase.Models;

namespace FiMAdminApi.Data.Firebase;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(FirebaseEvent))]
[JsonSerializable(typeof(FirebaseEventState))]
public partial class FirebaseJsonSerializerContext : JsonSerializerContext;