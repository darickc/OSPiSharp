using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OSPi.Infrastructure.Persistence;

/// <summary>
/// Used only by the <c>dotnet ef</c> tooling at design time so migrations can be
/// generated without booting the Web host. Runtime resolution uses the
/// <c>AddDbContextFactory</c> registration in <c>DependencyInjection</c>.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OSPiDbContext>
{
    public OSPiDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OSPiDbContext>()
            .UseSqlite("Data Source=design.db")
            .Options;

        return new OSPiDbContext(options);
    }
}
