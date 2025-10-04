using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DatabaseGrinder.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchemaBasedSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "databasegrinder");

            migrationBuilder.CreateTable(
                name: "test_records",
                schema: "databasegrinder",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_test_records_sequence_number",
                schema: "databasegrinder",
                table: "test_records",
                column: "sequence_number");

            migrationBuilder.CreateIndex(
                name: "ix_test_records_timestamp",
                schema: "databasegrinder",
                table: "test_records",
                column: "timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "test_records",
                schema: "databasegrinder");
        }
    }
}
