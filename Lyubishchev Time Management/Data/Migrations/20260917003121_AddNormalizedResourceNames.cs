using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lyubishchev_Time_Management.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedResourceNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Tags",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Categories",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql("UPDATE `Tags` SET `NormalizedName` = UPPER(TRIM(`Name`));");
            migrationBuilder.Sql("UPDATE `Categories` SET `NormalizedName` = UPPER(TRIM(`Name`));");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "Tags",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "Categories",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            // Create the new unique indexes before dropping the old ones: both old indexes lead
            // with UserId and are the only index covering the Tags.UserId/Categories.UserId FK
            // columns, so InnoDB refuses to drop them until a replacement index for that FK
            // already exists. The new (UserId, NormalizedName) indexes also lead with UserId, so
            // creating them first keeps the FK covered throughout the migration.
            migrationBuilder.CreateIndex(
                name: "IX_Tags_UserId_NormalizedName",
                table: "Tags",
                columns: new[] { "UserId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UserId_NormalizedName",
                table: "Categories",
                columns: new[] { "UserId", "NormalizedName" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_Tags_UserId_Name",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Categories_UserId_Name",
                table: "Categories");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Same FK-coverage constraint in reverse: recreate the old indexes before dropping
            // the NormalizedName ones.
            migrationBuilder.CreateIndex(
                name: "IX_Tags_UserId_Name",
                table: "Tags",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UserId_Name",
                table: "Categories",
                columns: new[] { "UserId", "Name" });

            migrationBuilder.DropIndex(
                name: "IX_Tags_UserId_NormalizedName",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Categories_UserId_NormalizedName",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Categories");
        }
    }
}
