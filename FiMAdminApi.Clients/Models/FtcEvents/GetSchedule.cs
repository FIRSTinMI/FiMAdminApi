namespace FiMAdminApi.Clients.Models.FtcEvents;

internal record GetSchedule(
    ScheduleResult[] Schedule);
    
internal record ScheduleResult(
    DateTime? StartTime,
    int MatchNumber,
    int Series,
    MatchTeam[] Teams);