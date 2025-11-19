using FiMAdminApi.Clients;
using FiMAdminApi.Models.Models;

namespace FiMAdminApi.EventSync;

/// <summary>
/// A generic step to be taken when syncing events. This will only be run when the event is in one of
/// <paramref name="applicableStatuses"/>.
/// </summary>
/// <param name="applicableStatuses">The list of statuses where this step should be run.</param>
public abstract class EventSyncStep(EventStatus[] applicableStatuses)
{
    public virtual bool ShouldRun(Event evt)
    {
        return applicableStatuses.Contains(evt.Status);
    }

    public abstract Task RunStep(Event evt, IDataClient eventDataClient);
}