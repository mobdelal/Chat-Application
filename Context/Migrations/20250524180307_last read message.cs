using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Context.Migrations
{
    /// <inheritdoc />
    public partial class lastreadmessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastReadMessageId",
                table: "ChatParticipants",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatParticipants_LastReadMessageId",
                table: "ChatParticipants",
                column: "LastReadMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatParticipants_Messages_LastReadMessageId",
                table: "ChatParticipants",
                column: "LastReadMessageId",
                principalTable: "Messages",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatParticipants_Messages_LastReadMessageId",
                table: "ChatParticipants");

            migrationBuilder.DropIndex(
                name: "IX_ChatParticipants_LastReadMessageId",
                table: "ChatParticipants");

            migrationBuilder.DropColumn(
                name: "LastReadMessageId",
                table: "ChatParticipants");
        }
    }
}
