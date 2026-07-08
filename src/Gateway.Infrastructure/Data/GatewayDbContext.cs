using Microsoft.EntityFrameworkCore;
using Gateway.Domain.Entities;

namespace Gateway.Infrastructure.Data;

public class GatewayDbContext : DbContext
{
    public DbSet<ProxyGroup> Groups => Set<ProxyGroup>();
    public DbSet<ProxyEndpoint> Endpoints => Set<ProxyEndpoint>();

    public GatewayDbContext(DbContextOptions<GatewayDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ProxyGroup>(e =>
        {
            e.HasKey(g => g.Id);
            e.HasIndex(g => g.Name).IsUnique();
            e.HasIndex(g => g.Path).IsUnique();
            e.HasMany(g => g.Endpoints)
             .WithOne(e => e.Group)
             .HasForeignKey(e => e.GroupId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProxyEndpoint>(e =>
        {
            e.HasKey(x => x.Id);
        });
    }
}
