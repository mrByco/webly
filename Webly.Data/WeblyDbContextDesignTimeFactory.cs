using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Webly.Data;

/// <summary>
/// Used only by <c>dotnet ef</c>. Without it the tools boot <c>Webly.Api</c>'s host, which in
/// Development resolves a real database (and can start a Testcontainers Postgres) just to read the
/// model. Scaffolding a migration needs the provider, not a server, so this hands EF a connection
/// string it never connects to.
/// </summary>
public class WeblyDbContextDesignTimeFactory : IDesignTimeDbContextFactory<WeblyDbContext>
{
    public WeblyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WeblyDbContext>()
            .UseNpgsql("Host=localhost;Port=5434;Database=webly;Username=webly;Password=webly")
            .Options;

        return new WeblyDbContext(options);
    }
}
