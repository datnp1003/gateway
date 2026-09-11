using Microsoft.EntityFrameworkCore;
using Gateway.Domain.Entities;

namespace Gateway.Infrastructure.Data;

public class GatewayDbContext : DbContext
{
    public DbSet<ProxyGroup> Groups => Set<ProxyGroup>();
    public DbSet<ProxyEndpoint> Endpoints => Set<ProxyEndpoint>();
    public DbSet<ProxyRequestEvent> ProxyRequestEvents => Set<ProxyRequestEvent>();

    public GatewayDbContext(DbContextOptions<GatewayDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ProxyGroup>(e =>
        {
            e.HasKey(g => g.Id);
            e.HasIndex(g => g.Name).IsUnique();
            e.HasIndex(g => g.Path).IsUnique();
            e.Property(g => g.BlockedIpRanges).HasMaxLength(4000);
            e.Property(g => g.AllowedIpRanges).HasMaxLength(4000);
            e.HasMany(g => g.Endpoints)
             .WithOne(e => e.Group)
             .HasForeignKey(e => e.GroupId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProxyEndpoint>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.BlockedIpRanges).HasMaxLength(4000);
            e.Property(x => x.AllowedIpRanges).HasMaxLength(4000);
        });

        builder.Entity<ProxyRequestEvent>(e =>
        {
            e.ToTable("ProxyRequestEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Method).HasMaxLength(16);
            e.Property(x => x.RequestPath).HasMaxLength(2048);
            e.Property(x => x.EndpointName).HasMaxLength(200);
            e.Property(x => x.GroupName).HasMaxLength(200);
            e.Property(x => x.ConfiguredDestination).HasMaxLength(512);
            e.Property(x => x.Outcome).HasMaxLength(32);
            e.HasIndex(x => new { x.OccurredAt, x.Id });
            e.HasIndex(x => new { x.EndpointId, x.OccurredAt, x.Id });
            e.HasIndex(x => new { x.GroupId, x.OccurredAt, x.Id });
        });
    }
}
