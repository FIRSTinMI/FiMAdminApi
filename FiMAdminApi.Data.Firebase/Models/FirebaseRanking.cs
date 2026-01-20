namespace FiMAdminApi.Data.Firebase.Models;

public record FirebaseRanking
{
    public int TeamNumber { get; set; }
    public int Rank { get; set; }
    public int? Wins { get; set; }
    public int? Ties { get; set; }
    public int? Losses { get; set; }
    public double? RankingPoints { get; set; }
    public double? SortOrder2 { get; set; }
    public double? SortOrder3 { get; set; }
    public double? SortOrder4 { get; set; }
    public double? SortOrder5 { get; set; }
}