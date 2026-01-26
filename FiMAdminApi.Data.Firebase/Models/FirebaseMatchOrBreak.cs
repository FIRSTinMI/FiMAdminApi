using System.Text.Json.Serialization;

namespace FiMAdminApi.Data.Firebase.Models;

[JsonDerivedType(typeof(FirebaseMatch), "match")]
[JsonDerivedType(typeof(FirebaseBreak), "break")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")] // firebase doesn't like "$" in object keys
public record FirebaseMatchOrBreak;

public record FirebaseMatch : FirebaseMatchOrBreak
{
    public required long Id { get; set; }
    public required int Number { get; set; }
    public required Dictionary<string, int> Participants { get; set; }
    public int? RedAlliance { get; set; }
    public int? BlueAlliance { get; set; }
    public string? Winner { get; set; } // red, blue, null
}

public record FirebaseBreak : FirebaseMatchOrBreak
{
    public string? Description { get; set; }
}