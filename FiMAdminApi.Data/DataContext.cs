using FiMAdminApi.Data.Enums;
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
    public DbSet<Match> Matches { get; set; }
    public DbSet<TruckRoute> TruckRoutes { get; init; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties(typeof(TournamentLevel)).HaveConversion<TournamentLevel>();
        configurationBuilder.Properties(typeof(Enum)).HaveConversion<string>();
        configurationBuilder.Properties(typeof(IEnumerable<Enum>)).HaveConversion<IEnumerable<string>>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<EventStaff>()
            .Property(e => e.Permissions)
            .HasConversion(v => v.Select(p => p.ToString()).ToList(),
            v => v.Select(Enum.Parse<EventPermission>).ToList());
    }
}