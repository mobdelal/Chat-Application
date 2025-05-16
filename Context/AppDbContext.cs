using Microsoft.EntityFrameworkCore;
using Models;

namespace Context
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options): base(options) { }
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<UserBlock> UserBlocks { get; set; } = null!;
        public DbSet<Chat> Chats { get; set; } = null!;
        public DbSet<Message> Messages { get; set; } = null!;
        public DbSet<ChatParticipant> ChatParticipants { get; set; } = null!;
        public DbSet<MessageReaction> MessageReactions { get; set; } = null!;
        public DbSet<FileAttachment> FileAttachments { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<UserBlock>()
                .HasIndex(b => new { b.BlockerId, b.BlockedId })
                .IsUnique();

            modelBuilder.Entity<UserBlock>()
                .HasOne(b => b.Blocker)
                .WithMany(u => u.BlockedUsers)
                .HasForeignKey(b => b.BlockerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserBlock>()
                .HasOne(b => b.Blocked)
                .WithMany(u => u.BlockedByUsers)
                .HasForeignKey(b => b.BlockedId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ChatParticipant>()
                .HasKey(cp => new { cp.ChatId, cp.UserId });

            modelBuilder.Entity<ChatParticipant>()
                .HasOne(cp => cp.Chat)
                .WithMany(c => c.Participants)
                .HasForeignKey(cp => cp.ChatId);

            modelBuilder.Entity<ChatParticipant>()
                .HasOne(cp => cp.User)
                .WithMany(u => u.ChatParticipants)
                .HasForeignKey(cp => cp.UserId);

            modelBuilder.Entity<MessageReaction>()
                .HasKey(r => new { r.MessageId, r.UserId });

            modelBuilder.Entity<MessageReaction>()
                .HasOne(r => r.Message)
                .WithMany(m => m.Reactions)
                .HasForeignKey(r => r.MessageId);

            modelBuilder.Entity<MessageReaction>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId);

        }



    }
}
