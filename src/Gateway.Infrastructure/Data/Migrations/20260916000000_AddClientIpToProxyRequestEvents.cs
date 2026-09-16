using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Gateway.Infrastructure.Data.Migrations;

[DbContext(typeof(GatewayDbContext))]
[Migration("20260916000000_AddClientIpToProxyRequestEvents")]
public partial class AddClientIpToProxyRequestEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ClientIp",
            table: "ProxyRequestEvents",
            type: "character varying(45)",
            maxLength: 45,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ClientIp",
            table: "ProxyRequestEvents");
    }
}
