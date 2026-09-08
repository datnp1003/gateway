using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gateway.Infrastructure.Data;

public sealed class GatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable("ConnectionStrings__Gateway")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Gateway for EF tooling."))
        .Options);
}
