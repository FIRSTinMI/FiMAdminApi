using System.Diagnostics;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Data.Firebase;
using FiMAdminApi.Models.Models;

namespace FiMAdminApi.Repositories;

public partial class EventRepository(DataContext dbContext, FrcFirebaseRepository? frcFirebaseRepository = null)
{
    public async Task<Event> UpdateEvent(Event evt, bool saveChanges = true)
    {
        dbContext.Update(evt);

        Debug.Assert(evt.Season?.Level is not null, "EventRepository.UpdateEvent must be called with Level populated");

        if (frcFirebaseRepository is not null)
            await frcFirebaseRepository.UpdateEvent(evt);

        if (saveChanges) await dbContext.SaveChangesAsync();
        
        return evt;
    }
}