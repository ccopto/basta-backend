using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Basta.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EnglishName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SpanishName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.CategoryId);
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "CategoryId", "EnglishName", "SpanishName" },
                values: new object[,]
                {
                    { 1, "Name", "Nombre" },
                    { 2, "Animal", "Animal" },
                    { 3, "City/Country", "Ciudad/País" },
                    { 4, "Food/Drink", "Comida/Bebida" },
                    { 5, "Color", "Color" },
                    { 6, "Thing", "Cosa" },
                    { 7, "Profession", "Profesión" },
                    { 8, "Brand", "Marca" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
