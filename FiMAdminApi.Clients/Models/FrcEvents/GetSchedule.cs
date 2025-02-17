namespace FiMAdminApi.Clients.Models.FrcEvents;

internal record GetSchedule(
    ScheduleResult[] Schedule);
    
internal record ScheduleResult(
    DateTime? StartTime,
    int MatchNumber,
    MatchTeam[] Teams);