using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Gateway.Infrastructure.Data.Migrations;

[DbContext(typeof(GatewayDbContext))]
[Migration("20260908100000_AddProxyRequestEvents")]
public partial class AddProxyRequestEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "ProxyRequestEvents", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false), DurationMs = table.Column<double>(type: "double precision", nullable: true),
            Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false), RequestPath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
            EndpointId = table.Column<Guid>(type: "uuid", nullable: false), GroupId = table.Column<Guid>(type: "uuid", nullable: false), EndpointName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
            GroupName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false), ConfiguredDestination = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
            Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false), ResponseStatus = table.Column<int>(type: "integer", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_ProxyRequestEvents", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_ProxyRequestEvents_OccurredAt_Id", table: "ProxyRequestEvents", columns: new[] { "OccurredAt", "Id" });
        migrationBuilder.CreateIndex(name: "IX_ProxyRequestEvents_EndpointId_OccurredAt_Id", table: "ProxyRequestEvents", columns: new[] { "EndpointId", "OccurredAt", "Id" });
        migrationBuilder.CreateIndex(name: "IX_ProxyRequestEvents_GroupId_OccurredAt_Id", table: "ProxyRequestEvents", columns: new[] { "GroupId", "OccurredAt", "Id" });
        migrationBuilder.Sql("ALTER TABLE \"ProxyRequestEvents\" ADD CONSTRAINT \"CK_ProxyRequestEvents_Outcome\" CHECK (\"Outcome\" IN ('upstream_response','gateway_rejected','network_failure','gateway_failure','client_disconnected'));");
        migrationBuilder.Sql("ALTER TABLE \"ProxyRequestEvents\" ADD CONSTRAINT \"CK_ProxyRequestEvents_Path\" CHECK (position('?' in \"RequestPath\") = 0);");
    }
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
