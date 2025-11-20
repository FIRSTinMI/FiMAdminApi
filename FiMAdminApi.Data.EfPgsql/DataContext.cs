using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FiMAdminApi.Data.EfPgsql;

public class DataContext(DbContextOptions<DataContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles { get; init; }
    public DbSet<Level> Levels { get; init; }
    public DbSet<Season> Seasons { get; init; }
    public DbSet<Event> Events { get; init; }
    public DbSet<EventNote> EventNotes { get; init; }
    public DbSet<EventStaff> EventStaffs { get; init; }
    public DbSet<EventRanking> EventRankings { get; init; }
    public DbSet<Match> Matches { get; init; }
    public DbSet<ScheduleDeviation> ScheduleDeviations { get; init; }
    public DbSet<TruckRoute> TruckRoutes { get; init; }
    public DbSet<EventTeam> EventTeams { get; init; }
    public DbSet<EventTeamStatus> EventTeamStatuses { get; init; }
    public DbSet<Alliance> Alliances { get; init; }
    public DbSet<Equipment> Equipment { get; init; }
    public DbSet<AvCartEquipment> AvCarts { get; set; }
    public DbSet<EquipmentType> EquipmentTypes { get; init; }
    public DbSet<EventStream> EventStreams { get; init; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties(typeof(TournamentLevel)).HaveConversion<TournamentLevel>();
        configurationBuilder.Properties(typeof(MatchWinner)).HaveConversion<MatchWinner>();
        configurationBuilder.Properties(typeof(StreamPlatform)).HaveConversion<StreamPlatform>();
        configurationBuilder.Properties(typeof(Enum)).HaveConversion<string>();
        configurationBuilder.Properties(typeof(IEnumerable<Enum>)).HaveConversion<IEnumerable<string>>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Register Postgres enum mapping so Npgsql sends the correct enum type instead of text
        modelBuilder.HasPostgresEnum<StreamPlatform>("stream_platform");
        modelBuilder.Entity<EventStream>(entity =>
        {
            entity.ToTable("event_streams");

            entity.Property(e => e.Id).HasColumnName("id");

            entity.Property(e => e.EventId).HasColumnName("event_id").IsRequired();

            entity.Property(e => e.Title).HasColumnName("title").HasMaxLength(255);

            entity.Property(e => e.Platform)
                .HasColumnName("platform")
                .HasColumnType("stream_platform")
                .IsRequired();

            entity.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(255).IsRequired();

            entity.Property(e => e.Url).HasColumnName("url").HasMaxLength(512);

            entity.Property(e => e.StartTime).HasColumnName("start_time");
        });

        modelBuilder
            .Entity<EventStaff>()
            .Property(e => e.Permissions)
            .HasConversion(v => v.Select(p => p.ToString()).ToList(),
                v => v.Select(Enum.Parse<EventPermission>).ToList(),
                new ValueComparer<ICollection<EventPermission>>((c1, c2) => c2 != null && c1 != null && c1.SequenceEqual(c2),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()));

        modelBuilder.Entity<Equipment>(entity =>
        {
            entity.HasDiscriminator(e => e.EquipmentTypeId)
                // EF should be smart enough skip the discriminator on the base type, returning all results 
                .HasValue<Equipment>(0)
                .HasValue<AvCartEquipment>(1);
        });
        
        modelBuilder.Entity<AvCartEquipment>(entity =>
        {
            entity.OwnsOne(e => e.Configuration, builder =>
            {
                builder.ToJson();
                builder.OwnsMany(c => c.StreamInfo);
            });
        });
        
        modelBuilder.Entity<TruckRoute>(entity =>
        {
            entity.OwnsOne(t => t.StreamingConfig, builder =>
            {
                builder.ToJson();
            });
        });
    }
}