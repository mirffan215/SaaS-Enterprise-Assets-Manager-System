using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AMGH.ITInventory.Persistence.DesignTime;

/// <summary>
/// Used only by the "dotnet ef" tooling (migrations). Reads the connection string from the
/// AMGH_ConnectionString environment variable, falling back to the local default instance.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AMGHDbContext>
{
    private const string DefaultConnection =
        "Server=localhost;Database=AMGH_ITInventory;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True";

    public AMGHDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AMGH_ConnectionString") ?? DefaultConnection;
        var options = new DbContextOptionsBuilder<AMGHDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo"))
            .Options;
        return new AMGHDbContext(options, new SystemUserService("DESIGN-TIME"), new SystemDateTimeProvider(), new CompanyContext());
    }
}
