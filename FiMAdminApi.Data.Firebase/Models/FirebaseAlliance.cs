namespace FiMAdminApi.Data.Firebase.Models;

public record FirebaseAlliance
{
    public required int Number { get; set; }
    public required string ShortName { get; set; } // "2"
    public required int[] Teams { get; set; }
};