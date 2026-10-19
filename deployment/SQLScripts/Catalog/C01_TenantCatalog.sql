/* =====================================================================
   Central tenant catalog (database-per-tenant). Run once on the catalog database, e.g. ITInv_Catalog.
   The catalog only lists customers and where their own databases are; it holds no customer data.
   ConnectionString is stored encrypted by the application ("enc:" prefix). A plain connection string written by
   hand is accepted too. Idempotent. The web application also creates this table at start-up when it is missing.
   ===================================================================== */
SET NOCOUNT ON;
GO
IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Tenants
    (
        Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY CLUSTERED,
        Code             NVARCHAR(10)   NOT NULL,
        Name             NVARCHAR(200)  NOT NULL,
        ConnectionString NVARCHAR(2000) NOT NULL,
        IsDemo           BIT            NOT NULL CONSTRAINT DF_Tenants_IsDemo DEFAULT (0),
        IsActive         BIT            NOT NULL CONSTRAINT DF_Tenants_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)   NOT NULL CONSTRAINT DF_Tenants_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_Tenants_Code CHECK (Code NOT LIKE N'%[^a-z0-9]%' AND LEN(Code) BETWEEN 2 AND 10)
    );
    CREATE UNIQUE NONCLUSTERED INDEX UX_Tenants_Code ON dbo.Tenants (Code);
    PRINT 'Created dbo.Tenants';
END
GO
