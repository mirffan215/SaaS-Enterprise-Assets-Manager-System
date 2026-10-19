using Microsoft.EntityFrameworkCore;

namespace AMGH.ITInventory.Persistence.Tenancy;

/// <summary>Creates the catalog database and its Tenants table when missing (same DDL as deployment/SQLScripts/Catalog/C01_TenantCatalog.sql).</summary>
public static class TenantCatalogInitializer
{
    public static async Task EnsureAsync(TenantCatalogDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        // EnsureCreated does nothing when the database already holds other tables, so the table is also created explicitly.
#pragma warning disable EF1002 // constant DDL, no user input
        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Tenants
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY CLUSTERED,
        Code NVARCHAR(10) NOT NULL,
        Name NVARCHAR(200) NOT NULL,
        ConnectionString NVARCHAR(2000) NOT NULL,
        IsDemo BIT NOT NULL CONSTRAINT DF_Tenants_IsDemo DEFAULT (0),
        IsActive BIT NOT NULL CONSTRAINT DF_Tenants_IsActive DEFAULT (1),
        CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Tenants_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_Tenants_Code CHECK (Code NOT LIKE N'%[^a-z0-9]%' AND LEN(Code) BETWEEN 2 AND 10)
    );
    CREATE UNIQUE NONCLUSTERED INDEX UX_Tenants_Code ON dbo.Tenants (Code);
END", cancellationToken);
#pragma warning restore EF1002
    }
}
