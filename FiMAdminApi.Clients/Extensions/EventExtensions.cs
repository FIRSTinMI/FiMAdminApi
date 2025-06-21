using FiMAdminApi.Clients.Models;
using FiMAdminApi.Models.Enums;
using Event = FiMAdminApi.Models.Models.Event;

namespace FiMAdminApi.Clients.Extensions;

public static class EventExtensions
{
    public static string? GetWebUrl(this Event evt, WebUrlType type)
    {
        return evt.SyncSource switch
        {
            DataSources.FrcEvents => FrcEventsDataClient.GetWebUrl(type, evt),
            DataSources.BlueAlliance => BlueAllianceDataClient.GetWebUrl(type, evt),
            DataSources.FtcEvents => FtcEventsDataClient.GetWebUrl(type, evt),
            null => null,
            _ => throw new ArgumentException(
                $"Tried to get web URL but no method was defined for DataSource {evt.SyncSource}")
        };
    }
}