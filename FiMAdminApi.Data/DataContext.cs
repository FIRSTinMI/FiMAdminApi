using FiMAdminApi.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Data;

public class DataContext(DbContextOptions<DataContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles { get; init; }
    public DbSet<Level> Levels { get; init; }
    public DbSet<Season> Seasons { get; init; }
    public DbSet<Event> Events { get; init; }
    public DbSet<TruckRoute> TruckRoutes { get; init; }
}