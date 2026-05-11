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
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<RoundAnswer> RoundAnswers => Set<RoundAnswer>();

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

            // CreatedAt is owned by the database; prevents clock-skew across app instances.
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("datetime('now')");

            entity.HasOne(d => d.Host)
                .WithMany(p => p.HostedGames)
                .HasForeignKey(d => d.HostUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GamePlayer>(entity =>
        {
            entity.HasKey(e => e.GamePlayerId);

            // A player may only appear once per game.
            entity.HasIndex(e => new { e.GameId, e.UserId }).IsUnique();

            entity.HasOne(d => d.Game)
                .WithMany(p => p.Players)
                .HasForeignKey(d => d.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.User)
                .WithMany(p => p.Participations)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.CategoryId);
            entity.Property(e => e.EnglishName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.SpanishName).IsRequired().HasMaxLength(100);

            entity.HasData(
                new Category { CategoryId = 1, EnglishName = "Name",         SpanishName = "Nombre",       ValidationType = CategoryValidationType.Name },
                new Category { CategoryId = 2, EnglishName = "Animal",       SpanishName = "Animal",       ValidationType = CategoryValidationType.Animal },
                new Category { CategoryId = 3, EnglishName = "City/Country", SpanishName = "Ciudad/País",  ValidationType = CategoryValidationType.City },
                new Category { CategoryId = 4, EnglishName = "Food/Drink",   SpanishName = "Comida/Bebida",ValidationType = CategoryValidationType.CommonWord },
                new Category { CategoryId = 5, EnglishName = "Color",        SpanishName = "Color",        ValidationType = CategoryValidationType.CommonWord },
                new Category { CategoryId = 6, EnglishName = "Thing",        SpanishName = "Cosa",         ValidationType = CategoryValidationType.CommonWord },
                new Category { CategoryId = 7, EnglishName = "Profession",   SpanishName = "Profesión",    ValidationType = CategoryValidationType.CommonWord },
                new Category { CategoryId = 8, EnglishName = "Brand",        SpanishName = "Marca",        ValidationType = CategoryValidationType.CommonWord }
            );
        });

        modelBuilder.Entity<RoundAnswer>(entity =>
        {
            entity.HasKey(e => e.RoundAnswerId);

            // Ensure a player can only have one answer per category in a round
            entity.HasIndex(e => new { e.GameId, e.RoundNumber, e.UserId, e.CategoryId }).IsUnique();

            entity.HasOne(d => d.Game)
                .WithMany()
                .HasForeignKey(d => d.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
