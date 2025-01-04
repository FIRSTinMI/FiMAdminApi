using System.ComponentModel.DataAnnotations;

namespace FiMAdminApi.Data.Models;

public class EventRanking
{
    [Key]
    public int Id { get; set; }
    
    public required Guid EventId { get; set; }
    public required int Rank { get; set; }
    public required int TeamNumber { get; set; }
    public IEnumerable<double>? SortOrders { get; set; }
    public int? Wins { get; set; }
    public int? Ties { get; set; }
    public int? Losses { get; set; }
    public double? QualAverage { get; set; }
    public int? Disqualifications { get; set; }
    public int? MatchesPlayed { get; set; }
}