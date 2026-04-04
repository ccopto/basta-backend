using Microsoft.EntityFrameworkCore;

namespace Basta.Server.Data;

public class BastaDbContext : DbContext
{
    public BastaDbContext(DbContextOptions<BastaDbContext> options)
        : base(options)
    {
    }

    // DbSets will be added in future user stories for Games, Users, rounds, etc.
}
