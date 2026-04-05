using Microsoft.EntityFrameworkCore;
using Basta.Server.Entities;

namespace Basta.Server.Data;

public class BastaDbContext : DbContext
{
    public BastaDbContext(DbContextOptions<BastaDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GamePlayer> GamePlayers => Set<GamePlayer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.Nickname).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PreferredLanguage).HasMaxLength(10).HasDefaultValue("en");
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasKey(e => e.GameId);
            entity.Property(e => e.GameId).HasMaxLength(4);
            
            entity.HasOne(d => d.Host)
                .WithMany(p => p.HostedGames)
                .HasForeignKey(d => d.HostUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GamePlayer>(entity =>
        {
            entity.HasKey(e => e.GamePlayerId);

            entity.HasOne(d => d.Game)
                .WithMany(p => p.Players)
                .HasForeignKey(d => d.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.User)
                .WithMany(p => p.Participations)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
