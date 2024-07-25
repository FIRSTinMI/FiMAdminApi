using FiMAdminApi.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Data;

public class DataContext(DbContextOptions<DataContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles { get; init; }
    public DbSet<Level> Levels { get; init; }
    public DbSet<Season> Seasons { get; init; }
    public DbSet<Event> Events { get; init; }
    public DbSet<EventNote> EventNotes { get; init; }
    public DbSet<EventStaff> EventStaffs { get; init; }
    public DbSet<TruckRoute> TruckRoutes { get; init; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties(typeof(Enum)).HaveConversion<string>();
    }
}