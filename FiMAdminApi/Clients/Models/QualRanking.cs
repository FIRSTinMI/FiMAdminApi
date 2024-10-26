namespace FiMAdminApi.Clients.Models;

public class QualRanking
{
    public int Rank { get; set; }
    public int TeamNumber { get; set; }
    public double SortOrder1 { get; set; }
    public double SortOrder2 { get; set; }
    public double SortOrder3 { get; set; }
    public double SortOrder4 { get; set; }
    public double SortOrder5 { get; set; }
    public double SortOrder6 { get; set; }
    public int Wins { get; set; }
    public int Ties { get; set; }
    public int Losses { get; set; }
    public double QualAverage { get; set; }
    public int Disqualifications { get; set; }
    public int MatchesPlayed { get; set; }
}