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
END
IF OBJECT_ID(N'dbo.TenantEvents', N'U') IS NULL
    CREATE TABLE dbo.TenantEvents
    (
        Id     BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TenantEvents PRIMARY KEY CLUSTERED,
        AtUtc  DATETIME2(3)   NOT NULL CONSTRAINT DF_TenantEvents_AtUtc DEFAULT (SYSUTCDATETIME()),
        Code   NVARCHAR(10)   NULL,
        Action NVARCHAR(60)   NOT NULL,
        Detail NVARCHAR(1000) NULL,
        Actor  NVARCHAR(100)  NOT NULL
    );
IF OBJECT_ID(N'dbo.CompanyKeys', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompanyKeys
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompanyKeys PRIMARY KEY CLUSTERED,
        KeyHash         CHAR(64)      NOT NULL,
        KeyHint         NVARCHAR(8)   NOT NULL,
        IssuedTo        NVARCHAR(256) NULL,
        Note            NVARCHAR(200) NULL,
        RequestId       INT           NULL,
        CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_CompanyKeys_Created DEFAULT (SYSUTCDATETIME()),
        CreatedBy       NVARCHAR(100) NOT NULL,
        ExpiresAtUtc    DATETIME2(3)  NOT NULL,
        UsedAtUtc       DATETIME2(3)  NULL,
        UsedCompanyCode NVARCHAR(10)  NULL,
        RevokedAtUtc    DATETIME2(3)  NULL
    );
    CREATE UNIQUE NONCLUSTERED INDEX UX_CompanyKeys_KeyHash ON dbo.CompanyKeys (KeyHash);
END
IF OBJECT_ID(N'dbo.DemoRequests', N'U') IS NULL
    CREATE TABLE dbo.DemoRequests
    (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DemoRequests PRIMARY KEY CLUSTERED,
        CreatedAtUtc DATETIME2(3)  NOT NULL CONSTRAINT DF_DemoRequests_Created DEFAULT (SYSUTCDATETIME()),
        FullName     NVARCHAR(150) NOT NULL,
        CompanyName  NVARCHAR(150) NOT NULL,
        Email        NVARCHAR(256) NOT NULL,
        Phone        NVARCHAR(50)  NULL,
        Message      NVARCHAR(1000) NULL,
        Status       NVARCHAR(20)  NOT NULL CONSTRAINT DF_DemoRequests_Status DEFAULT (N'New'),
        KeyId        INT           NULL,
        HandledAtUtc DATETIME2(3)  NULL,
        IpAddress    NVARCHAR(45)  NULL
    );", cancellationToken);
#pragma warning restore EF1002
    }
}
