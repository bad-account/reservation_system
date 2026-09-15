using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace reservation_system_for_padel.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Courts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimeSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeSlots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Surname = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CourtId = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeSlotId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reservations_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reservations_TimeSlots_TimeSlotId",
                        column: x => x.TimeSlotId,
                        principalTable: "TimeSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reservations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Courts",
                columns: new[] { "Id", "IsActive", "Number" },
                values: new object[,]
                {
                    { 1, true, 1 },
                    { 2, true, 2 },
                    { 3, true, 3 }
                });

            migrationBuilder.InsertData(
                table: "TimeSlots",
                columns: new[] { "Id", "EndTime", "StartTime" },
                values: new object[,]
                {
                    { 1, new TimeOnly(9, 0, 0), new TimeOnly(8, 0, 0) },
                    { 2, new TimeOnly(10, 0, 0), new TimeOnly(9, 0, 0) },
                    { 3, new TimeOnly(11, 0, 0), new TimeOnly(10, 0, 0) },
                    { 4, new TimeOnly(12, 0, 0), new TimeOnly(11, 0, 0) },
                    { 5, new TimeOnly(13, 0, 0), new TimeOnly(12, 0, 0) },
                    { 6, new TimeOnly(14, 0, 0), new TimeOnly(13, 0, 0) },
                    { 7, new TimeOnly(15, 0, 0), new TimeOnly(14, 0, 0) },
                    { 8, new TimeOnly(16, 0, 0), new TimeOnly(15, 0, 0) },
                    { 9, new TimeOnly(17, 0, 0), new TimeOnly(16, 0, 0) },
                    { 10, new TimeOnly(18, 0, 0), new TimeOnly(17, 0, 0) },
                    { 11, new TimeOnly(19, 0, 0), new TimeOnly(18, 0, 0) },
                    { 12, new TimeOnly(20, 0, 0), new TimeOnly(19, 0, 0) },
                    { 13, new TimeOnly(21, 0, 0), new TimeOnly(20, 0, 0) },
                    { 14, new TimeOnly(22, 0, 0), new TimeOnly(21, 0, 0) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_CourtId_Date_TimeSlotId",
                table: "Reservations",
                columns: new[] { "CourtId", "Date", "TimeSlotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TimeSlotId",
                table: "Reservations",
                column: "TimeSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_UserId",
                table: "Reservations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "Courts");

            migrationBuilder.DropTable(
                name: "TimeSlots");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
