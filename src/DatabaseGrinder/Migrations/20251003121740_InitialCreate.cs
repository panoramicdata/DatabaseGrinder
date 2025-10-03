using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DatabaseGrinder.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			name: "test_records",
			columns: table => new
			{
				id = table.Column<long>(type: "bigint", nullable: false)
					.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
				timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_test_records", x => x.id);
			});

		migrationBuilder.CreateIndex(
			name: "ix_test_records_timestamp",
			table: "test_records",
			column: "timestamp");
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(
			name: "test_records");
}
