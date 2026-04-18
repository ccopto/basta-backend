using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basta.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRoundAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoundAnswers",
                columns: table => new
                {
                    RoundAnswerId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    RoundNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedAnswer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsValid = table.Column<bool>(type: "INTEGER", nullable: true),
                    PointsAwarded = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundAnswers", x => x.RoundAnswerId);
                    table.ForeignKey(
                        name: "FK_RoundAnswers_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "GameId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoundAnswers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoundAnswers_GameId_RoundNumber_UserId_CategoryId",
                table: "RoundAnswers",
                columns: new[] { "GameId", "RoundNumber", "UserId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoundAnswers_UserId",
                table: "RoundAnswers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoundAnswers");
        }
    }
}
