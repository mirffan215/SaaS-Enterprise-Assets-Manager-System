/* =====================================================================
   AMGH Inventory Management System v1.0
   Script 02 - Tables, primary/foreign keys, defaults, check and unique
   constraints, indexes.  Idempotent: safe to re-run.
   GENERATED from the schema definition - keep in sync with Domain entities.
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ---------------------------------------------------- MASTER TABLES */
IF OBJECT_ID(N'dbo.tblAssetNumberSequence', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAssetNumberSequence]
    (
        [AssetNumberSequenceId] INT IDENTITY(1,1) NOT NULL,
        [SequenceCode] NVARCHAR(10) NOT NULL,
        [Prefix] NVARCHAR(20) NOT NULL CONSTRAINT DF_tblAssetNumberSequence_Prefix DEFAULT (N'AMGH-IT'),
        [LastNumber] BIGINT NOT NULL CONSTRAINT DF_tblAssetNumberSequence_LastNumber DEFAULT (0),
        [PaddingLength] TINYINT NOT NULL CONSTRAINT DF_tblAssetNumberSequence_PaddingLength DEFAULT (5),
        [Description] NVARCHAR(500) NULL,
        [SortOrder] INT NOT NULL CONSTRAINT DF_tblAssetNumberSequence_SortOrder DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblAssetNumberSequence_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblAssetNumberSequence_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetNumberSequence_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblAssetNumberSequence_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblAssetNumberSequence PRIMARY KEY CLUSTERED ([AssetNumberSequenceId]),
        CONSTRAINT CK_tblAssetNumberSequence_SequenceCode CHECK ([SequenceCode] NOT LIKE N'%[^A-Z]%' AND LEN([SequenceCode]) BETWEEN 2 AND 10),
        CONSTRAINT CK_tblAssetNumberSequence_LastNumber CHECK ([LastNumber] >= 0),
        CONSTRAINT CK_tblAssetNumberSequence_PaddingLength CHECK ([PaddingLength] BETWEEN 3 AND 10)
    );
    PRINT 'Created dbo.tblAssetNumberSequence';
END
GO

IF OBJECT_ID(N'dbo.tblAssetCategory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAssetCategory]
    (
        [AssetCategoryId] INT IDENTITY(1,1) NOT NULL,
        [CategoryCode] NVARCHAR(20) NOT NULL,
        [CategoryName] NVARCHAR(100) NOT NULL,
        [AssetNumberSequenceId] INT NOT NULL,
        [DefaultUsefulLifeMonths] INT NULL,
        [RequiresHostname] BIT NOT NULL CONSTRAINT DF_tblAssetCategory_RequiresHostname DEFAULT (0),
        [Description] NVARCHAR(500) NULL,
        [SortOrder] INT NOT NULL CONSTRAINT DF_tblAssetCategory_SortOrder DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblAssetCategory_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblAssetCategory_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetCategory_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblAssetCategory_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblAssetCategory PRIMARY KEY CLUSTERED ([AssetCategoryId]),
        CONSTRAINT CK_tblAssetCategory_DefaultUsefulLifeMonths CHECK ([DefaultUsefulLifeMonths] IS NULL OR [DefaultUsefulLifeMonths] > 0)
    );
    PRINT 'Created dbo.tblAssetCategory';
END
GO

IF OBJECT_ID(N'dbo.tblAssetSubCategory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAssetSubCategory]
    (
        [AssetSubCategoryId] INT IDENTITY(1,1) NOT NULL,
        [AssetCategoryId] INT NOT NULL,
        [SubCategoryCode] NVARCHAR(20) NOT NULL,
        [SubCategoryName] NVARCHAR(100) NOT NULL,
        [Description] NVARCHAR(500) NULL,
        [SortOrder] INT NOT NULL CONSTRAINT DF_tblAssetSubCategory_SortOrder DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblAssetSubCategory_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblAssetSubCategory_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetSubCategory_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblAssetSubCategory_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblAssetSubCategory PRIMARY KEY CLUSTERED ([AssetSubCategoryId])
    );
    PRINT 'Created dbo.tblAssetSubCategory';
END
GO

IF OBJECT_ID(N'dbo.tblManufacturer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblManufacturer]
    (
        [ManufacturerId] INT IDENTITY(1,1) NOT NULL,
        [ManufacturerCode] NVARCHAR(20) NOT NULL,
        [ManufacturerName] NVARCHAR(150) NOT NULL,
        [Website] NVARCHAR(250) NULL,
        [SupportEmail] NVARCHAR(256) NULL,
        [SupportPhone] NVARCHAR(30) NULL,
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblManufacturer_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblManufacturer_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblManufacturer_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblManufacturer_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblManufacturer PRIMARY KEY CLUSTERED ([ManufacturerId])
    );
    PRINT 'Created dbo.tblManufacturer';
END
GO

IF OBJECT_ID(N'dbo.tblVendor', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblVendor]
    (
        [VendorId] INT IDENTITY(1,1) NOT NULL,
        [VendorCode] NVARCHAR(20) NOT NULL,
        [VendorName] NVARCHAR(200) NOT NULL,
        [ContactPerson] NVARCHAR(150) NULL,
        [Email] NVARCHAR(256) NULL,
        [Phone] NVARCHAR(30) NULL,
        [Address] NVARCHAR(500) NULL,
        [CommercialRegistrationNo] NVARCHAR(30) NULL,
        [VatNumber] NVARCHAR(30) NULL,
        [IsApproved] BIT NOT NULL CONSTRAINT DF_tblVendor_IsApproved DEFAULT (0),
        [ApprovalDate] DATE NULL,
        [SecurityAssessmentDate] DATE NULL,
        [Notes] NVARCHAR(1000) NULL,
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblVendor_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblVendor_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblVendor_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblVendor_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblVendor PRIMARY KEY CLUSTERED ([VendorId]),
        CONSTRAINT CK_tblVendor_Email CHECK ([Email] IS NULL OR [Email] LIKE N'%_@_%._%'),
        CONSTRAINT CK_tblVendor_Rule01 CHECK ([IsApproved] = 0 OR [ApprovalDate] IS NOT NULL)
    );
    PRINT 'Created dbo.tblVendor';
END
GO

IF OBJECT_ID(N'dbo.tblLocation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblLocation]
    (
        [LocationId] INT IDENTITY(1,1) NOT NULL,
        [LocationCode] NVARCHAR(20) NOT NULL,
        [LocationName] NVARCHAR(150) NOT NULL,
        [AddressLine] NVARCHAR(500) NULL,
        [City] NVARCHAR(100) NULL,
        [Country] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblLocation_Country DEFAULT (N'Saudi Arabia'),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblLocation_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblLocation_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblLocation_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblLocation_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblLocation PRIMARY KEY CLUSTERED ([LocationId])
    );
    PRINT 'Created dbo.tblLocation';
END
GO

IF OBJECT_ID(N'dbo.tblBuilding', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblBuilding]
    (
        [BuildingId] INT IDENTITY(1,1) NOT NULL,
        [LocationId] INT NOT NULL,
        [BuildingCode] NVARCHAR(20) NOT NULL,
        [BuildingName] NVARCHAR(150) NOT NULL,
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblBuilding_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblBuilding_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblBuilding_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblBuilding_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblBuilding PRIMARY KEY CLUSTERED ([BuildingId])
    );
    PRINT 'Created dbo.tblBuilding';
END
GO

IF OBJECT_ID(N'dbo.tblFloor', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblFloor]
    (
        [FloorId] INT IDENTITY(1,1) NOT NULL,
        [BuildingId] INT NOT NULL,
        [FloorCode] NVARCHAR(20) NOT NULL,
        [FloorName] NVARCHAR(100) NOT NULL,
        [FloorNumber] SMALLINT NOT NULL CONSTRAINT DF_tblFloor_FloorNumber DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblFloor_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblFloor_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblFloor_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblFloor_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblFloor PRIMARY KEY CLUSTERED ([FloorId])
    );
    PRINT 'Created dbo.tblFloor';
END
GO

IF OBJECT_ID(N'dbo.tblRoom', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblRoom]
    (
        [RoomId] INT IDENTITY(1,1) NOT NULL,
        [FloorId] INT NOT NULL,
        [RoomCode] NVARCHAR(20) NOT NULL,
        [RoomName] NVARCHAR(100) NOT NULL,
        [RoomType] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblRoom_RoomType DEFAULT (N'Office'),
        [IsSecureArea] BIT NOT NULL CONSTRAINT DF_tblRoom_IsSecureArea DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblRoom_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblRoom_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblRoom_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblRoom_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblRoom PRIMARY KEY CLUSTERED ([RoomId]),
        CONSTRAINT CK_tblRoom_RoomType_Domain CHECK ([RoomType] IN (N'Office', N'ServerRoom', N'DataCenter', N'Store', N'MeetingRoom', N'Other'))
    );
    PRINT 'Created dbo.tblRoom';
END
GO

IF OBJECT_ID(N'dbo.tblDepartment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblDepartment]
    (
        [DepartmentId] INT IDENTITY(1,1) NOT NULL,
        [DepartmentCode] NVARCHAR(20) NOT NULL,
        [DepartmentName] NVARCHAR(150) NOT NULL,
        [ParentDepartmentId] INT NULL,
        [HeadEmployeeId] INT NULL,
        [CostCenter] NVARCHAR(30) NULL,
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblDepartment_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblDepartment_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblDepartment_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblDepartment_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblDepartment PRIMARY KEY CLUSTERED ([DepartmentId]),
        CONSTRAINT CK_tblDepartment_Rule01 CHECK ([ParentDepartmentId] IS NULL OR [ParentDepartmentId] <> [DepartmentId])
    );
    PRINT 'Created dbo.tblDepartment';
END
GO

IF OBJECT_ID(N'dbo.tblEmployee', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblEmployee]
    (
        [EmployeeId] INT IDENTITY(1,1) NOT NULL,
        [EmployeeNumber] NVARCHAR(30) NOT NULL,
        [FirstName] NVARCHAR(100) NOT NULL,
        [LastName] NVARCHAR(100) NOT NULL,
        [FullName] AS (CONCAT([FirstName], N' ', [LastName])) PERSISTED,
        [Email] NVARCHAR(256) NULL,
        [Phone] NVARCHAR(30) NULL,
        [JobTitle] NVARCHAR(150) NULL,
        [DepartmentId] INT NOT NULL,
        [LocationId] INT NULL,
        [ManagerEmployeeId] INT NULL,
        [HireDate] DATE NULL,
        [TerminationDate] DATE NULL,
        [EmploymentStatus] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblEmployee_EmploymentStatus DEFAULT (N'Active'),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblEmployee_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblEmployee_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblEmployee_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblEmployee PRIMARY KEY CLUSTERED ([EmployeeId]),
        CONSTRAINT CK_tblEmployee_Email CHECK ([Email] IS NULL OR [Email] LIKE N'%_@_%._%'),
        CONSTRAINT CK_tblEmployee_EmploymentStatus_Domain CHECK ([EmploymentStatus] IN (N'Active', N'OnLeave', N'Terminated')),
        CONSTRAINT CK_tblEmployee_Rule01 CHECK ([TerminationDate] IS NULL OR [HireDate] IS NULL OR [TerminationDate] >= [HireDate]),
        CONSTRAINT CK_tblEmployee_Rule02 CHECK ([EmploymentStatus] <> N'Terminated' OR [TerminationDate] IS NOT NULL),
        CONSTRAINT CK_tblEmployee_Rule03 CHECK ([ManagerEmployeeId] IS NULL OR [ManagerEmployeeId] <> [EmployeeId])
    );
    PRINT 'Created dbo.tblEmployee';
END
GO

IF OBJECT_ID(N'dbo.tblClassification', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblClassification]
    (
        [ClassificationId] INT IDENTITY(1,1) NOT NULL,
        [ClassificationCode] NVARCHAR(20) NOT NULL,
        [ClassificationName] NVARCHAR(100) NOT NULL,
        [ClassificationLevel] TINYINT NOT NULL,
        [HandlingRequirements] NVARCHAR(1000) NULL,
        [ColorHex] CHAR(7) NULL,
        [Description] NVARCHAR(500) NULL,
        [SortOrder] INT NOT NULL CONSTRAINT DF_tblClassification_SortOrder DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblClassification_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblClassification_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblClassification_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblClassification_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblClassification PRIMARY KEY CLUSTERED ([ClassificationId]),
        CONSTRAINT CK_tblClassification_ClassificationLevel CHECK ([ClassificationLevel] BETWEEN 1 AND 9),
        CONSTRAINT CK_tblClassification_ColorHex CHECK ([ColorHex] IS NULL OR [ColorHex] LIKE '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]')
    );
    PRINT 'Created dbo.tblClassification';
END
GO

IF OBJECT_ID(N'dbo.tblRiskLevel', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblRiskLevel]
    (
        [RiskLevelId] INT IDENTITY(1,1) NOT NULL,
        [RiskLevelCode] NVARCHAR(20) NOT NULL,
        [RiskLevelName] NVARCHAR(100) NOT NULL,
        [MinScore] TINYINT NOT NULL,
        [MaxScore] TINYINT NOT NULL,
        [ReviewFrequencyMonths] SMALLINT NULL,
        [ColorHex] CHAR(7) NULL,
        [Description] NVARCHAR(500) NULL,
        [SortOrder] INT NOT NULL CONSTRAINT DF_tblRiskLevel_SortOrder DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblRiskLevel_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblRiskLevel_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblRiskLevel_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblRiskLevel_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblRiskLevel PRIMARY KEY CLUSTERED ([RiskLevelId]),
        CONSTRAINT CK_tblRiskLevel_ReviewFrequencyMonths CHECK ([ReviewFrequencyMonths] IS NULL OR [ReviewFrequencyMonths] > 0),
        CONSTRAINT CK_tblRiskLevel_Rule01 CHECK ([MaxScore] >= [MinScore])
    );
    PRINT 'Created dbo.tblRiskLevel';
END
GO

IF OBJECT_ID(N'dbo.tblCriticality', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblCriticality]
    (
        [CriticalityId] INT IDENTITY(1,1) NOT NULL,
        [CriticalityCode] NVARCHAR(20) NOT NULL,
        [CriticalityName] NVARCHAR(100) NOT NULL,
        [CriticalityLevel] TINYINT NOT NULL,
        [RtoHours] INT NULL,
        [RpoHours] INT NULL,
        [Description] NVARCHAR(500) NULL,
        [SortOrder] INT NOT NULL CONSTRAINT DF_tblCriticality_SortOrder DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblCriticality_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblCriticality_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblCriticality_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblCriticality_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblCriticality PRIMARY KEY CLUSTERED ([CriticalityId]),
        CONSTRAINT CK_tblCriticality_CriticalityLevel CHECK ([CriticalityLevel] BETWEEN 1 AND 9),
        CONSTRAINT CK_tblCriticality_RtoHours CHECK ([RtoHours] IS NULL OR [RtoHours] >= 0),
        CONSTRAINT CK_tblCriticality_RpoHours CHECK ([RpoHours] IS NULL OR [RpoHours] >= 0)
    );
    PRINT 'Created dbo.tblCriticality';
END
GO

IF OBJECT_ID(N'dbo.tblAssetStatus', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAssetStatus]
    (
        [AssetStatusId] INT IDENTITY(1,1) NOT NULL,
        [StatusCode] NVARCHAR(20) NOT NULL,
        [StatusName] NVARCHAR(100) NOT NULL,
        [IsAssignable] BIT NOT NULL CONSTRAINT DF_tblAssetStatus_IsAssignable DEFAULT (0),
        [CountsAsInventory] BIT NOT NULL CONSTRAINT DF_tblAssetStatus_CountsAsInventory DEFAULT (1),
        [IsTerminal] BIT NOT NULL CONSTRAINT DF_tblAssetStatus_IsTerminal DEFAULT (0),
        [ColorHex] CHAR(7) NULL,
        [Description] NVARCHAR(500) NULL,
        [SortOrder] INT NOT NULL CONSTRAINT DF_tblAssetStatus_SortOrder DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblAssetStatus_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblAssetStatus_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetStatus_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblAssetStatus_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblAssetStatus PRIMARY KEY CLUSTERED ([AssetStatusId])
    );
    PRINT 'Created dbo.tblAssetStatus';
END
GO

IF OBJECT_ID(N'dbo.tblCurrency', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblCurrency]
    (
        [CurrencyId] INT IDENTITY(1,1) NOT NULL,
        [CurrencyCode] CHAR(3) NOT NULL,
        [CurrencyName] NVARCHAR(100) NOT NULL,
        [Symbol] NVARCHAR(10) NULL,
        [DecimalPlaces] TINYINT NOT NULL CONSTRAINT DF_tblCurrency_DecimalPlaces DEFAULT (2),
        [IsBaseCurrency] BIT NOT NULL CONSTRAINT DF_tblCurrency_IsBaseCurrency DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblCurrency_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblCurrency_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblCurrency_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblCurrency_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblCurrency PRIMARY KEY CLUSTERED ([CurrencyId]),
        CONSTRAINT CK_tblCurrency_CurrencyCode CHECK ([CurrencyCode] LIKE '[A-Z][A-Z][A-Z]'),
        CONSTRAINT CK_tblCurrency_DecimalPlaces CHECK ([DecimalPlaces] <= 4)
    );
    PRINT 'Created dbo.tblCurrency';
END
GO

IF OBJECT_ID(N'dbo.tblDisposalMethod', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblDisposalMethod]
    (
        [DisposalMethodId] INT IDENTITY(1,1) NOT NULL,
        [MethodCode] NVARCHAR(20) NOT NULL,
        [MethodName] NVARCHAR(100) NOT NULL,
        [RequiresDataSanitization] BIT NOT NULL CONSTRAINT DF_tblDisposalMethod_RequiresDataSanitization DEFAULT (1),
        [RequiresCertificate] BIT NOT NULL CONSTRAINT DF_tblDisposalMethod_RequiresCertificate DEFAULT (0),
        [Description] NVARCHAR(500) NULL,
        [SortOrder] INT NOT NULL CONSTRAINT DF_tblDisposalMethod_SortOrder DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblDisposalMethod_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblDisposalMethod_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblDisposalMethod_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblDisposalMethod_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblDisposalMethod PRIMARY KEY CLUSTERED ([DisposalMethodId])
    );
    PRINT 'Created dbo.tblDisposalMethod';
END
GO

IF OBJECT_ID(N'dbo.tblWarrantyStatus', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblWarrantyStatus]
    (
        [WarrantyStatusId] INT IDENTITY(1,1) NOT NULL,
        [StatusCode] NVARCHAR(20) NOT NULL,
        [StatusName] NVARCHAR(100) NOT NULL,
        [Description] NVARCHAR(500) NULL,
        [SortOrder] INT NOT NULL CONSTRAINT DF_tblWarrantyStatus_SortOrder DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblWarrantyStatus_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblWarrantyStatus_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblWarrantyStatus_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblWarrantyStatus_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblWarrantyStatus PRIMARY KEY CLUSTERED ([WarrantyStatusId])
    );
    PRINT 'Created dbo.tblWarrantyStatus';
END
GO

/* ---------------------------------------------------- SECURITY TABLES */
IF OBJECT_ID(N'dbo.tblUser', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblUser]
    (
        [UserId] INT IDENTITY(1,1) NOT NULL,
        [Username] NVARCHAR(100) NOT NULL,
        [Email] NVARCHAR(256) NOT NULL,
        [DisplayName] NVARCHAR(200) NOT NULL,
        [EmployeeId] INT NULL,
        [PasswordHash] NVARCHAR(500) NOT NULL,
        [SecurityStamp] UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_tblUser_SecurityStamp DEFAULT (NEWID()),
        [IsPlatformAdmin] BIT NOT NULL CONSTRAINT DF_tblUser_IsPlatformAdmin DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblUser_IsActive DEFAULT (1),
        [AccessFailedCount] INT NOT NULL CONSTRAINT DF_tblUser_AccessFailedCount DEFAULT (0),
        [LockoutEndUtc] DATETIME2(3) NULL,
        [MustChangePassword] BIT NOT NULL CONSTRAINT DF_tblUser_MustChangePassword DEFAULT (1),
        [PasswordChangedDate] DATETIME2(3) NULL,
        [LastLoginDate] DATETIME2(3) NULL,
        [LastLoginIp] NVARCHAR(45) NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblUser_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblUser_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblUser_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblUser PRIMARY KEY CLUSTERED ([UserId]),
        CONSTRAINT CK_tblUser_Email CHECK ([Email] LIKE N'%_@_%._%'),
        CONSTRAINT CK_tblUser_AccessFailedCount CHECK ([AccessFailedCount] >= 0)
    );
    PRINT 'Created dbo.tblUser';
END
GO

IF OBJECT_ID(N'dbo.tblRole', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblRole]
    (
        [RoleId] INT IDENTITY(1,1) NOT NULL,
        [RoleName] NVARCHAR(100) NOT NULL,
        [Description] NVARCHAR(500) NULL,
        [IsSystemRole] BIT NOT NULL CONSTRAINT DF_tblRole_IsSystemRole DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblRole_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblRole_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblRole_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblRole_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblRole PRIMARY KEY CLUSTERED ([RoleId])
    );
    PRINT 'Created dbo.tblRole';
END
GO

IF OBJECT_ID(N'dbo.tblPermission', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblPermission]
    (
        [PermissionId] INT IDENTITY(1,1) NOT NULL,
        [PermissionCode] NVARCHAR(100) NOT NULL,
        [ModuleName] NVARCHAR(100) NOT NULL,
        [PermissionName] NVARCHAR(150) NOT NULL,
        [Description] NVARCHAR(500) NULL,
        [SortOrder] INT NOT NULL CONSTRAINT DF_tblPermission_SortOrder DEFAULT (0),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblPermission_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblPermission_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblPermission_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblPermission PRIMARY KEY CLUSTERED ([PermissionId])
    );
    PRINT 'Created dbo.tblPermission';
END
GO

IF OBJECT_ID(N'dbo.tblRolePermission', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblRolePermission]
    (
        [RoleId] INT NOT NULL,
        [PermissionId] INT NOT NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblRolePermission_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblRolePermission_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblRolePermission_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblRolePermission PRIMARY KEY CLUSTERED ([RoleId], [PermissionId])
    );
    PRINT 'Created dbo.tblRolePermission';
END
GO

IF OBJECT_ID(N'dbo.tblUserRole', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblUserRole]
    (
        [UserId] INT NOT NULL,
        [RoleId] INT NOT NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblUserRole_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblUserRole_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblUserRole_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblUserRole PRIMARY KEY CLUSTERED ([UserId], [RoleId])
    );
    PRINT 'Created dbo.tblUserRole';
END
GO

IF OBJECT_ID(N'dbo.tblUserPasswordHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblUserPasswordHistory]
    (
        [UserPasswordHistoryId] BIGINT IDENTITY(1,1) NOT NULL,
        [UserId] INT NOT NULL,
        [PasswordHash] NVARCHAR(500) NOT NULL,
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblUserPasswordHistory_CreatedDate DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_tblUserPasswordHistory PRIMARY KEY CLUSTERED ([UserPasswordHistoryId])
    );
    PRINT 'Created dbo.tblUserPasswordHistory';
END
GO

IF OBJECT_ID(N'dbo.tblLoginAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblLoginAudit]
    (
        [LoginAuditId] BIGINT IDENTITY(1,1) NOT NULL,
        [UserId] INT NULL,
        [UsernameAttempted] NVARCHAR(100) NOT NULL,
        [EventType] NVARCHAR(30) NOT NULL,
        [IpAddress] NVARCHAR(45) NULL,
        [UserAgent] NVARCHAR(500) NULL,
        [FailureReason] NVARCHAR(200) NULL,
        [EventDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblLoginAudit_EventDate DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_tblLoginAudit PRIMARY KEY CLUSTERED ([LoginAuditId]),
        CONSTRAINT CK_tblLoginAudit_EventType_Domain CHECK ([EventType] IN (N'LoginSuccess', N'LoginFailed', N'Logout', N'Lockout', N'PasswordChanged', N'PasswordReset', N'SessionExpired', N'AccountDisabled'))
    );
    PRINT 'Created dbo.tblLoginAudit';
END
GO

IF OBJECT_ID(N'dbo.tblAuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAuditLog]
    (
        [AuditLogId] BIGINT IDENTITY(1,1) NOT NULL,
        [EntityName] NVARCHAR(100) NOT NULL,
        [EntityKey] NVARCHAR(100) NOT NULL,
        [Action] NVARCHAR(30) NOT NULL,
        [OldValues] NVARCHAR(MAX) NULL,
        [NewValues] NVARCHAR(MAX) NULL,
        [ChangedColumns] NVARCHAR(MAX) NULL,
        [PerformedBy] NVARCHAR(100) NOT NULL,
        [IpAddress] NVARCHAR(45) NULL,
        [CorrelationId] NVARCHAR(64) NULL,
        [EventDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAuditLog_EventDate DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_tblAuditLog PRIMARY KEY CLUSTERED ([AuditLogId]),
        CONSTRAINT CK_tblAuditLog_Action_Domain CHECK ([Action] IN (N'Create', N'Update', N'Delete', N'Restore', N'View', N'Export', N'Import', N'Print', N'Approve', N'Reject'))
    );
    PRINT 'Created dbo.tblAuditLog';
END
GO

/* ---------------------------------------------------- TRANSACTION TABLES */
IF OBJECT_ID(N'dbo.tblAsset', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAsset]
    (
        [AssetId] INT IDENTITY(1,1) NOT NULL,
        [AssetNumber] NVARCHAR(30) NOT NULL,
        [AssetName] NVARCHAR(200) NOT NULL,
        [AssetCategoryId] INT NOT NULL,
        [AssetSubCategoryId] INT NULL,
        [AssetStatusId] INT NOT NULL,
        [ManufacturerId] INT NULL,
        [ModelName] NVARCHAR(150) NULL,
        [SerialNumber] NVARCHAR(100) NULL,
        [LegacyAssetTag] NVARCHAR(50) NULL,
        [Description] NVARCHAR(1000) NULL,
        [Hostname] NVARCHAR(100) NULL,
        [IpAddress] NVARCHAR(45) NULL,
        [MacAddress] NVARCHAR(17) NULL,
        [OperatingSystem] NVARCHAR(100) NULL,
        [OsVersion] NVARCHAR(50) NULL,
        [Processor] NVARCHAR(150) NULL,
        [RamGb] DECIMAL(9,2) NULL,
        [StorageGb] DECIMAL(12,2) NULL,
        [FirmwareVersion] NVARCHAR(50) NULL,
        [ImeiNumber] NVARCHAR(20) NULL,
        [PhoneNumber] NVARCHAR(30) NULL,
        [VendorId] INT NULL,
        [PurchaseOrderNumber] NVARCHAR(50) NULL,
        [InvoiceNumber] NVARCHAR(50) NULL,
        [PurchaseDate] DATE NULL,
        [PurchaseCost] DECIMAL(18,2) NULL,
        [CurrencyId] INT NULL,
        [UsefulLifeMonths] INT NULL,
        [SalvageValue] DECIMAL(18,2) NULL,
        [AssignedEmployeeId] INT NULL,
        [DepartmentId] INT NULL,
        [LocationId] INT NULL,
        [BuildingId] INT NULL,
        [FloorId] INT NULL,
        [RoomId] INT NULL,
        [AssetOwnerEmployeeId] INT NULL,
        [ClassificationId] INT NULL,
        [CriticalityId] INT NULL,
        [RiskLevelId] INT NULL,
        [StoresSensitiveData] BIT NOT NULL CONSTRAINT DF_tblAsset_StoresSensitiveData DEFAULT (0),
        [IsDiskEncrypted] BIT NOT NULL CONSTRAINT DF_tblAsset_IsDiskEncrypted DEFAULT (0),
        [EndpointProtectionInstalled] BIT NOT NULL CONSTRAINT DF_tblAsset_EndpointProtectionInstalled DEFAULT (0),
        [IsMdmManaged] BIT NOT NULL CONSTRAINT DF_tblAsset_IsMdmManaged DEFAULT (0),
        [BackupRequired] BIT NOT NULL CONSTRAINT DF_tblAsset_BackupRequired DEFAULT (0),
        [LastSecurityReviewDate] DATE NULL,
        [SecurityNotes] NVARCHAR(1000) NULL,
        [CommissionDate] DATE NULL,
        [EndOfSupportDate] DATE NULL,
        [EndOfLifeDate] DATE NULL,
        [NextAuditDueDate] DATE NULL,
        [DecommissionDate] DATE NULL,
        [Notes] NVARCHAR(2000) NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblAsset_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAsset_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblAsset_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblAsset PRIMARY KEY CLUSTERED ([AssetId]),
        CONSTRAINT CK_tblAsset_AssetNumber CHECK ([AssetNumber] LIKE N'AMGH-IT-%'),
        CONSTRAINT CK_tblAsset_RamGb CHECK ([RamGb] IS NULL OR [RamGb] >= 0),
        CONSTRAINT CK_tblAsset_StorageGb CHECK ([StorageGb] IS NULL OR [StorageGb] >= 0),
        CONSTRAINT CK_tblAsset_PurchaseCost CHECK ([PurchaseCost] IS NULL OR [PurchaseCost] >= 0),
        CONSTRAINT CK_tblAsset_UsefulLifeMonths CHECK ([UsefulLifeMonths] IS NULL OR [UsefulLifeMonths] > 0),
        CONSTRAINT CK_tblAsset_SalvageValue CHECK ([SalvageValue] IS NULL OR [SalvageValue] >= 0),
        CONSTRAINT CK_tblAsset_Rule01 CHECK ([SalvageValue] IS NULL OR [PurchaseCost] IS NULL OR [SalvageValue] <= [PurchaseCost]),
        CONSTRAINT CK_tblAsset_Rule02 CHECK ([EndOfLifeDate] IS NULL OR [PurchaseDate] IS NULL OR [EndOfLifeDate] >= [PurchaseDate]),
        CONSTRAINT CK_tblAsset_Rule03 CHECK ([CommissionDate] IS NULL OR [PurchaseDate] IS NULL OR [CommissionDate] >= [PurchaseDate]),
        CONSTRAINT CK_tblAsset_Rule04 CHECK ([MacAddress] IS NULL OR [MacAddress] LIKE '[0-9A-F][0-9A-F][:-][0-9A-F][0-9A-F][:-][0-9A-F][0-9A-F][:-][0-9A-F][0-9A-F][:-][0-9A-F][0-9A-F][:-][0-9A-F][0-9A-F]')
    );
    PRINT 'Created dbo.tblAsset';
END
GO

IF OBJECT_ID(N'dbo.tblAssetAssignment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAssetAssignment]
    (
        [AssetAssignmentId] INT IDENTITY(1,1) NOT NULL,
        [AssetId] INT NOT NULL,
        [EmployeeId] INT NULL,
        [DepartmentId] INT NULL,
        [LocationId] INT NULL,
        [AssignmentType] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblAssetAssignment_AssignmentType DEFAULT (N'Permanent'),
        [AssignedDate] DATETIME2(3) NOT NULL,
        [ExpectedReturnDate] DATE NULL,
        [ReturnedDate] DATETIME2(3) NULL,
        [ConditionAtAssignment] NVARCHAR(200) NULL,
        [ConditionAtReturn] NVARCHAR(200) NULL,
        [AcknowledgementReceived] BIT NOT NULL CONSTRAINT DF_tblAssetAssignment_AcknowledgementReceived DEFAULT (0),
        [AcknowledgementDate] DATETIME2(3) NULL,
        [IsCurrent] BIT NOT NULL CONSTRAINT DF_tblAssetAssignment_IsCurrent DEFAULT (1),
        [AssignedBy] NVARCHAR(100) NOT NULL,
        [ReturnedTo] NVARCHAR(100) NULL,
        [Notes] NVARCHAR(1000) NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblAssetAssignment_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetAssignment_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblAssetAssignment_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblAssetAssignment PRIMARY KEY CLUSTERED ([AssetAssignmentId]),
        CONSTRAINT CK_tblAssetAssignment_AssignmentType_Domain CHECK ([AssignmentType] IN (N'Permanent', N'Temporary', N'Loan')),
        CONSTRAINT CK_tblAssetAssignment_Rule01 CHECK ([EmployeeId] IS NOT NULL OR [DepartmentId] IS NOT NULL),
        CONSTRAINT CK_tblAssetAssignment_Rule02 CHECK ([ReturnedDate] IS NULL OR [ReturnedDate] >= [AssignedDate]),
        CONSTRAINT CK_tblAssetAssignment_Rule03 CHECK ([IsCurrent] = 0 OR [ReturnedDate] IS NULL),
        CONSTRAINT CK_tblAssetAssignment_Rule04 CHECK ([IsCurrent] = 1 OR [ReturnedDate] IS NOT NULL),
        CONSTRAINT CK_tblAssetAssignment_Rule05 CHECK ([AcknowledgementReceived] = 0 OR [AcknowledgementDate] IS NOT NULL),
        CONSTRAINT CK_tblAssetAssignment_Rule06 CHECK ([AssignmentType] = N'Permanent' OR [ExpectedReturnDate] IS NOT NULL)
    );
    PRINT 'Created dbo.tblAssetAssignment';
END
GO

IF OBJECT_ID(N'dbo.tblAssetMovement', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAssetMovement]
    (
        [AssetMovementId] INT IDENTITY(1,1) NOT NULL,
        [AssetId] INT NOT NULL,
        [FromLocationId] INT NULL,
        [FromBuildingId] INT NULL,
        [FromFloorId] INT NULL,
        [FromRoomId] INT NULL,
        [ToLocationId] INT NOT NULL,
        [ToBuildingId] INT NULL,
        [ToFloorId] INT NULL,
        [ToRoomId] INT NULL,
        [MovementType] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblAssetMovement_MovementType DEFAULT (N'Relocation'),
        [MovementStatus] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblAssetMovement_MovementStatus DEFAULT (N'Requested'),
        [MovementReason] NVARCHAR(500) NOT NULL,
        [ChangeRequestNumber] NVARCHAR(50) NULL,
        [RequestedBy] NVARCHAR(100) NOT NULL,
        [RequestedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetMovement_RequestedDate DEFAULT (SYSUTCDATETIME()),
        [ApprovedBy] NVARCHAR(100) NULL,
        [ApprovedDate] DATETIME2(3) NULL,
        [CompletedDate] DATETIME2(3) NULL,
        [RejectionReason] NVARCHAR(500) NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblAssetMovement_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetMovement_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblAssetMovement_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblAssetMovement PRIMARY KEY CLUSTERED ([AssetMovementId]),
        CONSTRAINT CK_tblAssetMovement_MovementType_Domain CHECK ([MovementType] IN (N'Relocation', N'Transfer', N'Repair', N'Return', N'Storage')),
        CONSTRAINT CK_tblAssetMovement_MovementStatus_Domain CHECK ([MovementStatus] IN (N'Requested', N'Approved', N'Rejected', N'Completed', N'Cancelled')),
        CONSTRAINT CK_tblAssetMovement_Rule01 CHECK ([MovementStatus] NOT IN (N'Approved', N'Completed') OR ([ApprovedBy] IS NOT NULL AND [ApprovedDate] IS NOT NULL)),
        CONSTRAINT CK_tblAssetMovement_Rule02 CHECK ([MovementStatus] <> N'Completed' OR [CompletedDate] IS NOT NULL),
        CONSTRAINT CK_tblAssetMovement_Rule03 CHECK ([MovementStatus] <> N'Rejected' OR [RejectionReason] IS NOT NULL),
        CONSTRAINT CK_tblAssetMovement_Rule04 CHECK ([ApprovedBy] IS NULL OR [ApprovedBy] <> [RequestedBy])
    );
    PRINT 'Created dbo.tblAssetMovement';
END
GO

IF OBJECT_ID(N'dbo.tblAssetMaintenance', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAssetMaintenance]
    (
        [AssetMaintenanceId] INT IDENTITY(1,1) NOT NULL,
        [AssetId] INT NOT NULL,
        [MaintenanceType] NVARCHAR(30) NOT NULL,
        [MaintenanceStatus] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblAssetMaintenance_MaintenanceStatus DEFAULT (N'Scheduled'),
        [ScheduledDate] DATE NOT NULL,
        [StartDate] DATETIME2(3) NULL,
        [CompletedDate] DATETIME2(3) NULL,
        [VendorId] INT NULL,
        [TicketNumber] NVARCHAR(50) NULL,
        [ProblemDescription] NVARCHAR(1000) NOT NULL,
        [ResolutionDetails] NVARCHAR(2000) NULL,
        [PerformedBy] NVARCHAR(150) NULL,
        [Cost] DECIMAL(18,2) NULL,
        [CurrencyId] INT NULL,
        [DowntimeHours] DECIMAL(9,2) NULL,
        [NextMaintenanceDate] DATE NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblAssetMaintenance_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetMaintenance_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblAssetMaintenance_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblAssetMaintenance PRIMARY KEY CLUSTERED ([AssetMaintenanceId]),
        CONSTRAINT CK_tblAssetMaintenance_MaintenanceType_Domain CHECK ([MaintenanceType] IN (N'Preventive', N'Corrective', N'Upgrade', N'Inspection', N'Calibration')),
        CONSTRAINT CK_tblAssetMaintenance_MaintenanceStatus_Domain CHECK ([MaintenanceStatus] IN (N'Scheduled', N'InProgress', N'Completed', N'Cancelled')),
        CONSTRAINT CK_tblAssetMaintenance_Cost CHECK ([Cost] IS NULL OR [Cost] >= 0),
        CONSTRAINT CK_tblAssetMaintenance_DowntimeHours CHECK ([DowntimeHours] IS NULL OR [DowntimeHours] >= 0),
        CONSTRAINT CK_tblAssetMaintenance_Rule01 CHECK ([MaintenanceStatus] <> N'Completed' OR ([CompletedDate] IS NOT NULL AND [ResolutionDetails] IS NOT NULL)),
        CONSTRAINT CK_tblAssetMaintenance_Rule02 CHECK ([CompletedDate] IS NULL OR [StartDate] IS NULL OR [CompletedDate] >= [StartDate])
    );
    PRINT 'Created dbo.tblAssetMaintenance';
END
GO

IF OBJECT_ID(N'dbo.tblAssetAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAssetAudit]
    (
        [AssetAuditId] INT IDENTITY(1,1) NOT NULL,
        [AssetId] INT NOT NULL,
        [AuditCycle] NVARCHAR(20) NOT NULL,
        [AuditDate] DATETIME2(3) NOT NULL,
        [AuditedBy] NVARCHAR(100) NOT NULL,
        [AuditResult] NVARCHAR(30) NOT NULL,
        [ScanMethod] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblAssetAudit_ScanMethod DEFAULT (N'Manual'),
        [FoundLocationId] INT NULL,
        [FoundRoomId] INT NULL,
        [FoundEmployeeId] INT NULL,
        [Remarks] NVARCHAR(1000) NULL,
        [CorrectiveAction] NVARCHAR(1000) NULL,
        [IsResolved] BIT NOT NULL CONSTRAINT DF_tblAssetAudit_IsResolved DEFAULT (0),
        [ResolvedDate] DATETIME2(3) NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblAssetAudit_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetAudit_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblAssetAudit_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblAssetAudit PRIMARY KEY CLUSTERED ([AssetAuditId]),
        CONSTRAINT CK_tblAssetAudit_AuditResult_Domain CHECK ([AuditResult] IN (N'Verified', N'Missing', N'Damaged', N'LocationMismatch', N'CustodianMismatch', N'LabelMissing')),
        CONSTRAINT CK_tblAssetAudit_ScanMethod_Domain CHECK ([ScanMethod] IN (N'QrScan', N'Manual')),
        CONSTRAINT CK_tblAssetAudit_Rule01 CHECK ([IsResolved] = 0 OR [ResolvedDate] IS NOT NULL),
        CONSTRAINT CK_tblAssetAudit_Rule02 CHECK ([AuditResult] = N'Verified' OR [Remarks] IS NOT NULL)
    );
    PRINT 'Created dbo.tblAssetAudit';
END
GO

IF OBJECT_ID(N'dbo.tblAssetDisposal', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAssetDisposal]
    (
        [AssetDisposalId] INT IDENTITY(1,1) NOT NULL,
        [AssetId] INT NOT NULL,
        [DisposalMethodId] INT NOT NULL,
        [DisposalStatus] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblAssetDisposal_DisposalStatus DEFAULT (N'Requested'),
        [Reason] NVARCHAR(1000) NOT NULL,
        [RequestedBy] NVARCHAR(100) NOT NULL,
        [RequestedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetDisposal_RequestedDate DEFAULT (SYSUTCDATETIME()),
        [ApprovedBy] NVARCHAR(100) NULL,
        [ApprovedDate] DATETIME2(3) NULL,
        [RejectionReason] NVARCHAR(500) NULL,
        [DataSanitizationMethod] NVARCHAR(150) NULL,
        [DataSanitizationDate] DATETIME2(3) NULL,
        [DataSanitizationVerifiedBy] NVARCHAR(100) NULL,
        [CertificateNumber] NVARCHAR(100) NULL,
        [DisposalDate] DATE NULL,
        [RecipientName] NVARCHAR(200) NULL,
        [SaleValue] DECIMAL(18,2) NULL,
        [CurrencyId] INT NULL,
        [Remarks] NVARCHAR(1000) NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblAssetDisposal_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetDisposal_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblAssetDisposal_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblAssetDisposal PRIMARY KEY CLUSTERED ([AssetDisposalId]),
        CONSTRAINT CK_tblAssetDisposal_DisposalStatus_Domain CHECK ([DisposalStatus] IN (N'Requested', N'Approved', N'Rejected', N'Completed', N'Cancelled')),
        CONSTRAINT CK_tblAssetDisposal_SaleValue CHECK ([SaleValue] IS NULL OR [SaleValue] >= 0),
        CONSTRAINT CK_tblAssetDisposal_Rule01 CHECK ([DisposalStatus] NOT IN (N'Approved', N'Completed') OR ([ApprovedBy] IS NOT NULL AND [ApprovedDate] IS NOT NULL)),
        CONSTRAINT CK_tblAssetDisposal_Rule02 CHECK ([DisposalStatus] <> N'Completed' OR [DisposalDate] IS NOT NULL),
        CONSTRAINT CK_tblAssetDisposal_Rule03 CHECK ([DisposalStatus] <> N'Rejected' OR [RejectionReason] IS NOT NULL),
        CONSTRAINT CK_tblAssetDisposal_Rule04 CHECK ([ApprovedBy] IS NULL OR [ApprovedBy] <> [RequestedBy]),
        CONSTRAINT CK_tblAssetDisposal_Rule05 CHECK ([DataSanitizationVerifiedBy] IS NULL OR [DataSanitizationDate] IS NOT NULL)
    );
    PRINT 'Created dbo.tblAssetDisposal';
END
GO

IF OBJECT_ID(N'dbo.tblWarranty', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblWarranty]
    (
        [WarrantyId] INT IDENTITY(1,1) NOT NULL,
        [AssetId] INT NOT NULL,
        [WarrantyStatusId] INT NOT NULL,
        [WarrantyType] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblWarranty_WarrantyType DEFAULT (N'Manufacturer'),
        [ProviderVendorId] INT NULL,
        [ContractNumber] NVARCHAR(100) NULL,
        [StartDate] DATE NOT NULL,
        [EndDate] DATE NOT NULL,
        [SupportLevel] NVARCHAR(100) NULL,
        [Cost] DECIMAL(18,2) NULL,
        [CurrencyId] INT NULL,
        [Terms] NVARCHAR(2000) NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblWarranty_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblWarranty_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblWarranty_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblWarranty PRIMARY KEY CLUSTERED ([WarrantyId]),
        CONSTRAINT CK_tblWarranty_WarrantyType_Domain CHECK ([WarrantyType] IN (N'Manufacturer', N'Extended', N'SupportContract')),
        CONSTRAINT CK_tblWarranty_Cost CHECK ([Cost] IS NULL OR [Cost] >= 0),
        CONSTRAINT CK_tblWarranty_Rule01 CHECK ([EndDate] >= [StartDate])
    );
    PRINT 'Created dbo.tblWarranty';
END
GO

IF OBJECT_ID(N'dbo.tblSoftwareLicense', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblSoftwareLicense]
    (
        [SoftwareLicenseId] INT IDENTITY(1,1) NOT NULL,
        [LicenseNumber] NVARCHAR(30) NOT NULL,
        [SoftwareName] NVARCHAR(200) NOT NULL,
        [SoftwareVersion] NVARCHAR(50) NULL,
        [PublisherManufacturerId] INT NULL,
        [VendorId] INT NULL,
        [LicenseType] NVARCHAR(30) NOT NULL,
        [LicenseKeyEncrypted] VARBINARY(MAX) NULL,
        [TotalSeats] INT NULL,
        [PurchaseDate] DATE NULL,
        [StartDate] DATE NULL,
        [ExpiryDate] DATE NULL,
        [PurchaseOrderNumber] NVARCHAR(50) NULL,
        [InvoiceNumber] NVARCHAR(50) NULL,
        [Cost] DECIMAL(18,2) NULL,
        [CurrencyId] INT NULL,
        [RenewalReminderDays] INT NOT NULL CONSTRAINT DF_tblSoftwareLicense_RenewalReminderDays DEFAULT (60),
        [ContractOwnerEmployeeId] INT NULL,
        [Notes] NVARCHAR(1000) NULL,
        [IsActive] BIT NOT NULL CONSTRAINT DF_tblSoftwareLicense_IsActive DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblSoftwareLicense_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblSoftwareLicense_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblSoftwareLicense_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblSoftwareLicense PRIMARY KEY CLUSTERED ([SoftwareLicenseId]),
        CONSTRAINT CK_tblSoftwareLicense_LicenseNumber CHECK ([LicenseNumber] LIKE N'AMGH-IT-LIC-%'),
        CONSTRAINT CK_tblSoftwareLicense_LicenseType_Domain CHECK ([LicenseType] IN (N'Perpetual', N'Subscription', N'Oem', N'Volume', N'PerUser', N'PerDevice', N'OpenSource', N'Freeware')),
        CONSTRAINT CK_tblSoftwareLicense_TotalSeats CHECK ([TotalSeats] IS NULL OR [TotalSeats] > 0),
        CONSTRAINT CK_tblSoftwareLicense_Cost CHECK ([Cost] IS NULL OR [Cost] >= 0),
        CONSTRAINT CK_tblSoftwareLicense_RenewalReminderDays CHECK ([RenewalReminderDays] BETWEEN 0 AND 365),
        CONSTRAINT CK_tblSoftwareLicense_Rule01 CHECK ([ExpiryDate] IS NULL OR [StartDate] IS NULL OR [ExpiryDate] >= [StartDate]),
        CONSTRAINT CK_tblSoftwareLicense_Rule02 CHECK ([LicenseType] NOT IN (N'Subscription') OR [ExpiryDate] IS NOT NULL)
    );
    PRINT 'Created dbo.tblSoftwareLicense';
END
GO

IF OBJECT_ID(N'dbo.tblSoftwareLicenseAllocation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblSoftwareLicenseAllocation]
    (
        [SoftwareLicenseAllocationId] INT IDENTITY(1,1) NOT NULL,
        [SoftwareLicenseId] INT NOT NULL,
        [AssetId] INT NULL,
        [EmployeeId] INT NULL,
        [AllocatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblSoftwareLicenseAllocation_AllocatedDate DEFAULT (SYSUTCDATETIME()),
        [RevokedDate] DATETIME2(3) NULL,
        [IsCurrent] BIT NOT NULL CONSTRAINT DF_tblSoftwareLicenseAllocation_IsCurrent DEFAULT (1),
        [Notes] NVARCHAR(500) NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblSoftwareLicenseAllocation_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblSoftwareLicenseAllocation_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblSoftwareLicenseAllocation_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblSoftwareLicenseAllocation PRIMARY KEY CLUSTERED ([SoftwareLicenseAllocationId]),
        CONSTRAINT CK_tblSoftwareLicenseAllocation_Rule01 CHECK ([AssetId] IS NOT NULL OR [EmployeeId] IS NOT NULL),
        CONSTRAINT CK_tblSoftwareLicenseAllocation_Rule02 CHECK ([IsCurrent] = 0 OR [RevokedDate] IS NULL),
        CONSTRAINT CK_tblSoftwareLicenseAllocation_Rule03 CHECK ([IsCurrent] = 1 OR [RevokedDate] IS NOT NULL)
    );
    PRINT 'Created dbo.tblSoftwareLicenseAllocation';
END
GO

IF OBJECT_ID(N'dbo.tblAssetQRCode', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAssetQRCode]
    (
        [AssetQRCodeId] INT IDENTITY(1,1) NOT NULL,
        [AssetId] INT NOT NULL,
        [QrVersion] INT NOT NULL,
        [Payload] NVARCHAR(2000) NOT NULL,
        [PayloadHash] CHAR(64) NOT NULL,
        [QrImagePng] VARBINARY(MAX) NOT NULL,
        [IsCurrent] BIT NOT NULL CONSTRAINT DF_tblAssetQRCode_IsCurrent DEFAULT (1),
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblAssetQRCode_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetQRCode_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblAssetQRCode_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblAssetQRCode PRIMARY KEY CLUSTERED ([AssetQRCodeId]),
        CONSTRAINT CK_tblAssetQRCode_QrVersion CHECK ([QrVersion] > 0)
    );
    PRINT 'Created dbo.tblAssetQRCode';
END
GO

IF OBJECT_ID(N'dbo.tblQrLabelPrintLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblQrLabelPrintLog]
    (
        [QrLabelPrintLogId] BIGINT IDENTITY(1,1) NOT NULL,
        [AssetQRCodeId] INT NOT NULL,
        [AssetId] INT NOT NULL,
        [LabelSize] NVARCHAR(30) NOT NULL,
        [LabelDesign] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblQrLabelPrintLog_LabelDesign DEFAULT (N'QrLabel'),
        [Copies] SMALLINT NOT NULL CONSTRAINT DF_tblQrLabelPrintLog_Copies DEFAULT (1),
        [PrintBatchId] UNIQUEIDENTIFIER NOT NULL,
        [IsReprint] BIT NOT NULL CONSTRAINT DF_tblQrLabelPrintLog_IsReprint DEFAULT (0),
        [ReprintReason] NVARCHAR(500) NULL,
        [PrintedBy] NVARCHAR(100) NOT NULL,
        [PrintedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblQrLabelPrintLog_PrintedDate DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_tblQrLabelPrintLog PRIMARY KEY CLUSTERED ([QrLabelPrintLogId]),
        CONSTRAINT CK_tblQrLabelPrintLog_LabelSize_Domain CHECK ([LabelSize] IN (N'Size50x30', N'Size70x40', N'Size100x50')),
        CONSTRAINT CK_tblQrLabelPrintLog_LabelDesign_Domain CHECK ([LabelDesign] IN (N'QrLabel', N'AssetTag')),
        CONSTRAINT CK_tblQrLabelPrintLog_Copies CHECK ([Copies] BETWEEN 1 AND 100),
        CONSTRAINT CK_tblQrLabelPrintLog_Rule01 CHECK ([IsReprint] = 0 OR [ReprintReason] IS NOT NULL)
    );
    PRINT 'Created dbo.tblQrLabelPrintLog';
END
GO

IF OBJECT_ID(N'dbo.tblAssetDocument', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAssetDocument]
    (
        [AssetDocumentId] INT IDENTITY(1,1) NOT NULL,
        [AssetId] INT NOT NULL,
        [DocumentType] NVARCHAR(30) NOT NULL,
        [DocumentTitle] NVARCHAR(200) NOT NULL,
        [OriginalFileName] NVARCHAR(260) NOT NULL,
        [StoredFileName] NVARCHAR(100) NOT NULL,
        [ContentType] NVARCHAR(100) NOT NULL,
        [FileSizeBytes] BIGINT NOT NULL,
        [Sha256Hash] CHAR(64) NOT NULL,
        [ClassificationId] INT NULL,
        [Description] NVARCHAR(500) NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblAssetDocument_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetDocument_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblAssetDocument_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblAssetDocument PRIMARY KEY CLUSTERED ([AssetDocumentId]),
        CONSTRAINT CK_tblAssetDocument_DocumentType_Domain CHECK ([DocumentType] IN (N'Invoice', N'PurchaseOrder', N'DeliveryNote', N'WarrantyCertificate', N'HandoverForm', N'MaintenanceReport', N'DisposalCertificate', N'DataSanitizationCertificate', N'Manual', N'Photo', N'Other')),
        CONSTRAINT CK_tblAssetDocument_FileSizeBytes CHECK ([FileSizeBytes] > 0 AND [FileSizeBytes] <= 26214400)
    );
    PRINT 'Created dbo.tblAssetDocument';
END
GO

IF OBJECT_ID(N'dbo.tblAssetHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblAssetHistory]
    (
        [AssetHistoryId] BIGINT IDENTITY(1,1) NOT NULL,
        [AssetId] INT NOT NULL,
        [EventType] NVARCHAR(30) NOT NULL,
        [FieldName] NVARCHAR(100) NULL,
        [OldValue] NVARCHAR(1000) NULL,
        [NewValue] NVARCHAR(1000) NULL,
        [Remarks] NVARCHAR(1000) NULL,
        [PerformedBy] NVARCHAR(100) NOT NULL,
        [EventDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblAssetHistory_EventDate DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_tblAssetHistory PRIMARY KEY CLUSTERED ([AssetHistoryId]),
        CONSTRAINT CK_tblAssetHistory_EventType_Domain CHECK ([EventType] IN (N'Created', N'Updated', N'StatusChanged', N'Assigned', N'Returned', N'Moved', N'MaintenanceLogged', N'Audited', N'QrGenerated', N'LabelPrinted', N'DocumentAttached', N'DisposalRequested', N'Disposed', N'Deleted', N'Restored', N'LicenseAllocated', N'LicenseRevoked'))
    );
    PRINT 'Created dbo.tblAssetHistory';
END
GO

IF OBJECT_ID(N'dbo.tblNotification', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblNotification]
    (
        [NotificationId] BIGINT IDENTITY(1,1) NOT NULL,
        [UserId] INT NOT NULL,
        [Title] NVARCHAR(200) NOT NULL,
        [Message] NVARCHAR(2000) NOT NULL,
        [NotificationType] NVARCHAR(30) NOT NULL,
        [Severity] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblNotification_Severity DEFAULT (N'Info'),
        [RelatedEntityName] NVARCHAR(100) NULL,
        [RelatedEntityId] NVARCHAR(50) NULL,
        [ActionUrl] NVARCHAR(500) NULL,
        [IsRead] BIT NOT NULL CONSTRAINT DF_tblNotification_IsRead DEFAULT (0),
        [ReadDate] DATETIME2(3) NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblNotification_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblNotification_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblNotification_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblNotification PRIMARY KEY CLUSTERED ([NotificationId]),
        CONSTRAINT CK_tblNotification_NotificationType_Domain CHECK ([NotificationType] IN (N'WarrantyExpiry', N'LicenseExpiry', N'MaintenanceDue', N'AuditDue', N'ApprovalRequired', N'AssignmentAcknowledgement', N'System')),
        CONSTRAINT CK_tblNotification_Severity_Domain CHECK ([Severity] IN (N'Info', N'Warning', N'Critical')),
        CONSTRAINT CK_tblNotification_Rule01 CHECK ([IsRead] = 0 OR [ReadDate] IS NOT NULL)
    );
    PRINT 'Created dbo.tblNotification';
END
GO

IF OBJECT_ID(N'dbo.tblNotificationQueue', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[tblNotificationQueue]
    (
        [NotificationQueueId] BIGINT IDENTITY(1,1) NOT NULL,
        [Channel] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblNotificationQueue_Channel DEFAULT (N'Email'),
        [RecipientAddress] NVARCHAR(256) NOT NULL,
        [RecipientUserId] INT NULL,
        [Subject] NVARCHAR(300) NOT NULL,
        [Body] NVARCHAR(MAX) NOT NULL,
        [IsHtml] BIT NOT NULL CONSTRAINT DF_tblNotificationQueue_IsHtml DEFAULT (1),
        [QueueStatus] NVARCHAR(30) NOT NULL CONSTRAINT DF_tblNotificationQueue_QueueStatus DEFAULT (N'Pending'),
        [AttemptCount] INT NOT NULL CONSTRAINT DF_tblNotificationQueue_AttemptCount DEFAULT (0),
        [MaxAttempts] INT NOT NULL CONSTRAINT DF_tblNotificationQueue_MaxAttempts DEFAULT (5),
        [NextAttemptDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblNotificationQueue_NextAttemptDate DEFAULT (SYSUTCDATETIME()),
        [LastError] NVARCHAR(2000) NULL,
        [SentDate] DATETIME2(3) NULL,
        [DeduplicationKey] NVARCHAR(200) NULL,
        [CreatedBy] NVARCHAR(100) NOT NULL CONSTRAINT DF_tblNotificationQueue_CreatedBy DEFAULT (N'SYSTEM'),
        [CreatedDate] DATETIME2(3) NOT NULL CONSTRAINT DF_tblNotificationQueue_CreatedDate DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] NVARCHAR(100) NULL,
        [ModifiedDate] DATETIME2(3) NULL,
        [DeletedBy] NVARCHAR(100) NULL,
        [DeletedDate] DATETIME2(3) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT DF_tblNotificationQueue_IsDeleted DEFAULT (0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT PK_tblNotificationQueue PRIMARY KEY CLUSTERED ([NotificationQueueId]),
        CONSTRAINT CK_tblNotificationQueue_Channel_Domain CHECK ([Channel] IN (N'Email', N'InApp')),
        CONSTRAINT CK_tblNotificationQueue_QueueStatus_Domain CHECK ([QueueStatus] IN (N'Pending', N'Processing', N'Sent', N'Failed', N'Cancelled')),
        CONSTRAINT CK_tblNotificationQueue_AttemptCount CHECK ([AttemptCount] >= 0),
        CONSTRAINT CK_tblNotificationQueue_MaxAttempts CHECK ([MaxAttempts] BETWEEN 1 AND 20),
        CONSTRAINT CK_tblNotificationQueue_Rule01 CHECK ([QueueStatus] <> N'Sent' OR [SentDate] IS NOT NULL)
    );
    PRINT 'Created dbo.tblNotificationQueue';
END
GO

/* ---------------------------------------------------- UPGRADES OF EXISTING DATABASES
   Columns added after the first release. New installations already have them (CREATE TABLE above). */
-- v1.1: which design was printed (QR label or barcode asset tag)
IF COL_LENGTH(N'dbo.tblQrLabelPrintLog', N'LabelDesign') IS NULL
BEGIN
    ALTER TABLE dbo.[tblQrLabelPrintLog] ADD [LabelDesign] NVARCHAR(30) NOT NULL
        CONSTRAINT DF_tblQrLabelPrintLog_LabelDesign DEFAULT (N'QrLabel') WITH VALUES;
    PRINT 'Added dbo.tblQrLabelPrintLog.LabelDesign';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblQrLabelPrintLog_LabelDesign_Domain')
    ALTER TABLE dbo.[tblQrLabelPrintLog] ADD CONSTRAINT CK_tblQrLabelPrintLog_LabelDesign_Domain
        CHECK ([LabelDesign] IN (N'QrLabel', N'AssetTag'));
GO

/* ---------------------------------------------------- FOREIGN KEYS */
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetCategory_AssetNumberSequenceId')
    ALTER TABLE dbo.[tblAssetCategory] ADD CONSTRAINT FK_tblAssetCategory_AssetNumberSequenceId FOREIGN KEY ([AssetNumberSequenceId])
        REFERENCES dbo.[tblAssetNumberSequence] ([AssetNumberSequenceId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetSubCategory_AssetCategoryId')
    ALTER TABLE dbo.[tblAssetSubCategory] ADD CONSTRAINT FK_tblAssetSubCategory_AssetCategoryId FOREIGN KEY ([AssetCategoryId])
        REFERENCES dbo.[tblAssetCategory] ([AssetCategoryId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblBuilding_LocationId')
    ALTER TABLE dbo.[tblBuilding] ADD CONSTRAINT FK_tblBuilding_LocationId FOREIGN KEY ([LocationId])
        REFERENCES dbo.[tblLocation] ([LocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblFloor_BuildingId')
    ALTER TABLE dbo.[tblFloor] ADD CONSTRAINT FK_tblFloor_BuildingId FOREIGN KEY ([BuildingId])
        REFERENCES dbo.[tblBuilding] ([BuildingId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblRoom_FloorId')
    ALTER TABLE dbo.[tblRoom] ADD CONSTRAINT FK_tblRoom_FloorId FOREIGN KEY ([FloorId])
        REFERENCES dbo.[tblFloor] ([FloorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblDepartment_ParentDepartmentId')
    ALTER TABLE dbo.[tblDepartment] ADD CONSTRAINT FK_tblDepartment_ParentDepartmentId FOREIGN KEY ([ParentDepartmentId])
        REFERENCES dbo.[tblDepartment] ([DepartmentId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblDepartment_HeadEmployeeId')
    ALTER TABLE dbo.[tblDepartment] ADD CONSTRAINT FK_tblDepartment_HeadEmployeeId FOREIGN KEY ([HeadEmployeeId])
        REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblEmployee_DepartmentId')
    ALTER TABLE dbo.[tblEmployee] ADD CONSTRAINT FK_tblEmployee_DepartmentId FOREIGN KEY ([DepartmentId])
        REFERENCES dbo.[tblDepartment] ([DepartmentId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblEmployee_LocationId')
    ALTER TABLE dbo.[tblEmployee] ADD CONSTRAINT FK_tblEmployee_LocationId FOREIGN KEY ([LocationId])
        REFERENCES dbo.[tblLocation] ([LocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblEmployee_ManagerEmployeeId')
    ALTER TABLE dbo.[tblEmployee] ADD CONSTRAINT FK_tblEmployee_ManagerEmployeeId FOREIGN KEY ([ManagerEmployeeId])
        REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblUser_EmployeeId')
    ALTER TABLE dbo.[tblUser] ADD CONSTRAINT FK_tblUser_EmployeeId FOREIGN KEY ([EmployeeId])
        REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblRolePermission_RoleId')
    ALTER TABLE dbo.[tblRolePermission] ADD CONSTRAINT FK_tblRolePermission_RoleId FOREIGN KEY ([RoleId])
        REFERENCES dbo.[tblRole] ([RoleId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblRolePermission_PermissionId')
    ALTER TABLE dbo.[tblRolePermission] ADD CONSTRAINT FK_tblRolePermission_PermissionId FOREIGN KEY ([PermissionId])
        REFERENCES dbo.[tblPermission] ([PermissionId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblUserRole_UserId')
    ALTER TABLE dbo.[tblUserRole] ADD CONSTRAINT FK_tblUserRole_UserId FOREIGN KEY ([UserId])
        REFERENCES dbo.[tblUser] ([UserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblUserRole_RoleId')
    ALTER TABLE dbo.[tblUserRole] ADD CONSTRAINT FK_tblUserRole_RoleId FOREIGN KEY ([RoleId])
        REFERENCES dbo.[tblRole] ([RoleId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblUserPasswordHistory_UserId')
    ALTER TABLE dbo.[tblUserPasswordHistory] ADD CONSTRAINT FK_tblUserPasswordHistory_UserId FOREIGN KEY ([UserId])
        REFERENCES dbo.[tblUser] ([UserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblLoginAudit_UserId')
    ALTER TABLE dbo.[tblLoginAudit] ADD CONSTRAINT FK_tblLoginAudit_UserId FOREIGN KEY ([UserId])
        REFERENCES dbo.[tblUser] ([UserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssetCategoryId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_AssetCategoryId FOREIGN KEY ([AssetCategoryId])
        REFERENCES dbo.[tblAssetCategory] ([AssetCategoryId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssetSubCategoryId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_AssetSubCategoryId FOREIGN KEY ([AssetSubCategoryId])
        REFERENCES dbo.[tblAssetSubCategory] ([AssetSubCategoryId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssetStatusId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_AssetStatusId FOREIGN KEY ([AssetStatusId])
        REFERENCES dbo.[tblAssetStatus] ([AssetStatusId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_ManufacturerId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_ManufacturerId FOREIGN KEY ([ManufacturerId])
        REFERENCES dbo.[tblManufacturer] ([ManufacturerId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_VendorId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_VendorId FOREIGN KEY ([VendorId])
        REFERENCES dbo.[tblVendor] ([VendorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_CurrencyId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_CurrencyId FOREIGN KEY ([CurrencyId])
        REFERENCES dbo.[tblCurrency] ([CurrencyId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssignedEmployeeId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_AssignedEmployeeId FOREIGN KEY ([AssignedEmployeeId])
        REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_DepartmentId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_DepartmentId FOREIGN KEY ([DepartmentId])
        REFERENCES dbo.[tblDepartment] ([DepartmentId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_LocationId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_LocationId FOREIGN KEY ([LocationId])
        REFERENCES dbo.[tblLocation] ([LocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_BuildingId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_BuildingId FOREIGN KEY ([BuildingId])
        REFERENCES dbo.[tblBuilding] ([BuildingId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_FloorId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_FloorId FOREIGN KEY ([FloorId])
        REFERENCES dbo.[tblFloor] ([FloorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_RoomId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_RoomId FOREIGN KEY ([RoomId])
        REFERENCES dbo.[tblRoom] ([RoomId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssetOwnerEmployeeId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_AssetOwnerEmployeeId FOREIGN KEY ([AssetOwnerEmployeeId])
        REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_ClassificationId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_ClassificationId FOREIGN KEY ([ClassificationId])
        REFERENCES dbo.[tblClassification] ([ClassificationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_CriticalityId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_CriticalityId FOREIGN KEY ([CriticalityId])
        REFERENCES dbo.[tblCriticality] ([CriticalityId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_RiskLevelId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_RiskLevelId FOREIGN KEY ([RiskLevelId])
        REFERENCES dbo.[tblRiskLevel] ([RiskLevelId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_AssetId')
    ALTER TABLE dbo.[tblAssetAssignment] ADD CONSTRAINT FK_tblAssetAssignment_AssetId FOREIGN KEY ([AssetId])
        REFERENCES dbo.[tblAsset] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_EmployeeId')
    ALTER TABLE dbo.[tblAssetAssignment] ADD CONSTRAINT FK_tblAssetAssignment_EmployeeId FOREIGN KEY ([EmployeeId])
        REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_DepartmentId')
    ALTER TABLE dbo.[tblAssetAssignment] ADD CONSTRAINT FK_tblAssetAssignment_DepartmentId FOREIGN KEY ([DepartmentId])
        REFERENCES dbo.[tblDepartment] ([DepartmentId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_LocationId')
    ALTER TABLE dbo.[tblAssetAssignment] ADD CONSTRAINT FK_tblAssetAssignment_LocationId FOREIGN KEY ([LocationId])
        REFERENCES dbo.[tblLocation] ([LocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_AssetId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_AssetId FOREIGN KEY ([AssetId])
        REFERENCES dbo.[tblAsset] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_FromLocationId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_FromLocationId FOREIGN KEY ([FromLocationId])
        REFERENCES dbo.[tblLocation] ([LocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_FromBuildingId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_FromBuildingId FOREIGN KEY ([FromBuildingId])
        REFERENCES dbo.[tblBuilding] ([BuildingId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_FromFloorId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_FromFloorId FOREIGN KEY ([FromFloorId])
        REFERENCES dbo.[tblFloor] ([FloorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_FromRoomId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_FromRoomId FOREIGN KEY ([FromRoomId])
        REFERENCES dbo.[tblRoom] ([RoomId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_ToLocationId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_ToLocationId FOREIGN KEY ([ToLocationId])
        REFERENCES dbo.[tblLocation] ([LocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_ToBuildingId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_ToBuildingId FOREIGN KEY ([ToBuildingId])
        REFERENCES dbo.[tblBuilding] ([BuildingId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_ToFloorId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_ToFloorId FOREIGN KEY ([ToFloorId])
        REFERENCES dbo.[tblFloor] ([FloorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_ToRoomId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_ToRoomId FOREIGN KEY ([ToRoomId])
        REFERENCES dbo.[tblRoom] ([RoomId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMaintenance_AssetId')
    ALTER TABLE dbo.[tblAssetMaintenance] ADD CONSTRAINT FK_tblAssetMaintenance_AssetId FOREIGN KEY ([AssetId])
        REFERENCES dbo.[tblAsset] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMaintenance_VendorId')
    ALTER TABLE dbo.[tblAssetMaintenance] ADD CONSTRAINT FK_tblAssetMaintenance_VendorId FOREIGN KEY ([VendorId])
        REFERENCES dbo.[tblVendor] ([VendorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMaintenance_CurrencyId')
    ALTER TABLE dbo.[tblAssetMaintenance] ADD CONSTRAINT FK_tblAssetMaintenance_CurrencyId FOREIGN KEY ([CurrencyId])
        REFERENCES dbo.[tblCurrency] ([CurrencyId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_AssetId')
    ALTER TABLE dbo.[tblAssetAudit] ADD CONSTRAINT FK_tblAssetAudit_AssetId FOREIGN KEY ([AssetId])
        REFERENCES dbo.[tblAsset] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_FoundLocationId')
    ALTER TABLE dbo.[tblAssetAudit] ADD CONSTRAINT FK_tblAssetAudit_FoundLocationId FOREIGN KEY ([FoundLocationId])
        REFERENCES dbo.[tblLocation] ([LocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_FoundRoomId')
    ALTER TABLE dbo.[tblAssetAudit] ADD CONSTRAINT FK_tblAssetAudit_FoundRoomId FOREIGN KEY ([FoundRoomId])
        REFERENCES dbo.[tblRoom] ([RoomId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_FoundEmployeeId')
    ALTER TABLE dbo.[tblAssetAudit] ADD CONSTRAINT FK_tblAssetAudit_FoundEmployeeId FOREIGN KEY ([FoundEmployeeId])
        REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDisposal_AssetId')
    ALTER TABLE dbo.[tblAssetDisposal] ADD CONSTRAINT FK_tblAssetDisposal_AssetId FOREIGN KEY ([AssetId])
        REFERENCES dbo.[tblAsset] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDisposal_DisposalMethodId')
    ALTER TABLE dbo.[tblAssetDisposal] ADD CONSTRAINT FK_tblAssetDisposal_DisposalMethodId FOREIGN KEY ([DisposalMethodId])
        REFERENCES dbo.[tblDisposalMethod] ([DisposalMethodId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDisposal_CurrencyId')
    ALTER TABLE dbo.[tblAssetDisposal] ADD CONSTRAINT FK_tblAssetDisposal_CurrencyId FOREIGN KEY ([CurrencyId])
        REFERENCES dbo.[tblCurrency] ([CurrencyId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblWarranty_AssetId')
    ALTER TABLE dbo.[tblWarranty] ADD CONSTRAINT FK_tblWarranty_AssetId FOREIGN KEY ([AssetId])
        REFERENCES dbo.[tblAsset] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblWarranty_WarrantyStatusId')
    ALTER TABLE dbo.[tblWarranty] ADD CONSTRAINT FK_tblWarranty_WarrantyStatusId FOREIGN KEY ([WarrantyStatusId])
        REFERENCES dbo.[tblWarrantyStatus] ([WarrantyStatusId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblWarranty_ProviderVendorId')
    ALTER TABLE dbo.[tblWarranty] ADD CONSTRAINT FK_tblWarranty_ProviderVendorId FOREIGN KEY ([ProviderVendorId])
        REFERENCES dbo.[tblVendor] ([VendorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblWarranty_CurrencyId')
    ALTER TABLE dbo.[tblWarranty] ADD CONSTRAINT FK_tblWarranty_CurrencyId FOREIGN KEY ([CurrencyId])
        REFERENCES dbo.[tblCurrency] ([CurrencyId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicense_PublisherManufacturerId')
    ALTER TABLE dbo.[tblSoftwareLicense] ADD CONSTRAINT FK_tblSoftwareLicense_PublisherManufacturerId FOREIGN KEY ([PublisherManufacturerId])
        REFERENCES dbo.[tblManufacturer] ([ManufacturerId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicense_VendorId')
    ALTER TABLE dbo.[tblSoftwareLicense] ADD CONSTRAINT FK_tblSoftwareLicense_VendorId FOREIGN KEY ([VendorId])
        REFERENCES dbo.[tblVendor] ([VendorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicense_CurrencyId')
    ALTER TABLE dbo.[tblSoftwareLicense] ADD CONSTRAINT FK_tblSoftwareLicense_CurrencyId FOREIGN KEY ([CurrencyId])
        REFERENCES dbo.[tblCurrency] ([CurrencyId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicense_ContractOwnerEmployeeId')
    ALTER TABLE dbo.[tblSoftwareLicense] ADD CONSTRAINT FK_tblSoftwareLicense_ContractOwnerEmployeeId FOREIGN KEY ([ContractOwnerEmployeeId])
        REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicenseAllocation_SoftwareLicenseId')
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] ADD CONSTRAINT FK_tblSoftwareLicenseAllocation_SoftwareLicenseId FOREIGN KEY ([SoftwareLicenseId])
        REFERENCES dbo.[tblSoftwareLicense] ([SoftwareLicenseId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicenseAllocation_AssetId')
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] ADD CONSTRAINT FK_tblSoftwareLicenseAllocation_AssetId FOREIGN KEY ([AssetId])
        REFERENCES dbo.[tblAsset] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicenseAllocation_EmployeeId')
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] ADD CONSTRAINT FK_tblSoftwareLicenseAllocation_EmployeeId FOREIGN KEY ([EmployeeId])
        REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetQRCode_AssetId')
    ALTER TABLE dbo.[tblAssetQRCode] ADD CONSTRAINT FK_tblAssetQRCode_AssetId FOREIGN KEY ([AssetId])
        REFERENCES dbo.[tblAsset] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblQrLabelPrintLog_AssetQRCodeId')
    ALTER TABLE dbo.[tblQrLabelPrintLog] ADD CONSTRAINT FK_tblQrLabelPrintLog_AssetQRCodeId FOREIGN KEY ([AssetQRCodeId])
        REFERENCES dbo.[tblAssetQRCode] ([AssetQRCodeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblQrLabelPrintLog_AssetId')
    ALTER TABLE dbo.[tblQrLabelPrintLog] ADD CONSTRAINT FK_tblQrLabelPrintLog_AssetId FOREIGN KEY ([AssetId])
        REFERENCES dbo.[tblAsset] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDocument_AssetId')
    ALTER TABLE dbo.[tblAssetDocument] ADD CONSTRAINT FK_tblAssetDocument_AssetId FOREIGN KEY ([AssetId])
        REFERENCES dbo.[tblAsset] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDocument_ClassificationId')
    ALTER TABLE dbo.[tblAssetDocument] ADD CONSTRAINT FK_tblAssetDocument_ClassificationId FOREIGN KEY ([ClassificationId])
        REFERENCES dbo.[tblClassification] ([ClassificationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetHistory_AssetId')
    ALTER TABLE dbo.[tblAssetHistory] ADD CONSTRAINT FK_tblAssetHistory_AssetId FOREIGN KEY ([AssetId])
        REFERENCES dbo.[tblAsset] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblNotification_UserId')
    ALTER TABLE dbo.[tblNotification] ADD CONSTRAINT FK_tblNotification_UserId FOREIGN KEY ([UserId])
        REFERENCES dbo.[tblUser] ([UserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblNotificationQueue_RecipientUserId')
    ALTER TABLE dbo.[tblNotificationQueue] ADD CONSTRAINT FK_tblNotificationQueue_RecipientUserId FOREIGN KEY ([RecipientUserId])
        REFERENCES dbo.[tblUser] ([UserId]);
GO

/* ---------------------------------------------------- UNIQUE CONSTRAINTS / INDEXES */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetNumberSequence_SequenceCode' AND object_id = OBJECT_ID(N'dbo.tblAssetNumberSequence'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetNumberSequence_SequenceCode ON dbo.[tblAssetNumberSequence] ([SequenceCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetCategory_CategoryCode' AND object_id = OBJECT_ID(N'dbo.tblAssetCategory'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetCategory_CategoryCode ON dbo.[tblAssetCategory] ([CategoryCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetCategory_CategoryName' AND object_id = OBJECT_ID(N'dbo.tblAssetCategory'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetCategory_CategoryName ON dbo.[tblAssetCategory] ([CategoryName]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetSubCategory_AssetCategoryId_SubCategoryCode' AND object_id = OBJECT_ID(N'dbo.tblAssetSubCategory'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetSubCategory_AssetCategoryId_SubCategoryCode ON dbo.[tblAssetSubCategory] ([AssetCategoryId], [SubCategoryCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblManufacturer_ManufacturerCode' AND object_id = OBJECT_ID(N'dbo.tblManufacturer'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblManufacturer_ManufacturerCode ON dbo.[tblManufacturer] ([ManufacturerCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblManufacturer_ManufacturerName' AND object_id = OBJECT_ID(N'dbo.tblManufacturer'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblManufacturer_ManufacturerName ON dbo.[tblManufacturer] ([ManufacturerName]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblVendor_VendorCode' AND object_id = OBJECT_ID(N'dbo.tblVendor'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblVendor_VendorCode ON dbo.[tblVendor] ([VendorCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblLocation_LocationCode' AND object_id = OBJECT_ID(N'dbo.tblLocation'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblLocation_LocationCode ON dbo.[tblLocation] ([LocationCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblBuilding_LocationId_BuildingCode' AND object_id = OBJECT_ID(N'dbo.tblBuilding'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblBuilding_LocationId_BuildingCode ON dbo.[tblBuilding] ([LocationId], [BuildingCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblFloor_BuildingId_FloorCode' AND object_id = OBJECT_ID(N'dbo.tblFloor'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblFloor_BuildingId_FloorCode ON dbo.[tblFloor] ([BuildingId], [FloorCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblRoom_FloorId_RoomCode' AND object_id = OBJECT_ID(N'dbo.tblRoom'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblRoom_FloorId_RoomCode ON dbo.[tblRoom] ([FloorId], [RoomCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblDepartment_DepartmentCode' AND object_id = OBJECT_ID(N'dbo.tblDepartment'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblDepartment_DepartmentCode ON dbo.[tblDepartment] ([DepartmentCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblEmployee_EmployeeNumber' AND object_id = OBJECT_ID(N'dbo.tblEmployee'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblEmployee_EmployeeNumber ON dbo.[tblEmployee] ([EmployeeNumber]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblEmployee_Email' AND object_id = OBJECT_ID(N'dbo.tblEmployee'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblEmployee_Email ON dbo.[tblEmployee] ([Email])
        WHERE [Email] IS NOT NULL AND [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblClassification_ClassificationCode' AND object_id = OBJECT_ID(N'dbo.tblClassification'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblClassification_ClassificationCode ON dbo.[tblClassification] ([ClassificationCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblClassification_ClassificationLevel' AND object_id = OBJECT_ID(N'dbo.tblClassification'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblClassification_ClassificationLevel ON dbo.[tblClassification] ([ClassificationLevel]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblRiskLevel_RiskLevelCode' AND object_id = OBJECT_ID(N'dbo.tblRiskLevel'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblRiskLevel_RiskLevelCode ON dbo.[tblRiskLevel] ([RiskLevelCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblCriticality_CriticalityCode' AND object_id = OBJECT_ID(N'dbo.tblCriticality'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblCriticality_CriticalityCode ON dbo.[tblCriticality] ([CriticalityCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblCriticality_CriticalityLevel' AND object_id = OBJECT_ID(N'dbo.tblCriticality'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblCriticality_CriticalityLevel ON dbo.[tblCriticality] ([CriticalityLevel]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetStatus_StatusCode' AND object_id = OBJECT_ID(N'dbo.tblAssetStatus'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetStatus_StatusCode ON dbo.[tblAssetStatus] ([StatusCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblCurrency_CurrencyCode' AND object_id = OBJECT_ID(N'dbo.tblCurrency'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblCurrency_CurrencyCode ON dbo.[tblCurrency] ([CurrencyCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblCurrency_IsBaseCurrency' AND object_id = OBJECT_ID(N'dbo.tblCurrency'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblCurrency_IsBaseCurrency ON dbo.[tblCurrency] ([IsBaseCurrency])
        WHERE [IsBaseCurrency] = 1 AND [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblDisposalMethod_MethodCode' AND object_id = OBJECT_ID(N'dbo.tblDisposalMethod'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblDisposalMethod_MethodCode ON dbo.[tblDisposalMethod] ([MethodCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblWarrantyStatus_StatusCode' AND object_id = OBJECT_ID(N'dbo.tblWarrantyStatus'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblWarrantyStatus_StatusCode ON dbo.[tblWarrantyStatus] ([StatusCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblUser_Username' AND object_id = OBJECT_ID(N'dbo.tblUser'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblUser_Username ON dbo.[tblUser] ([Username]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblUser_Email' AND object_id = OBJECT_ID(N'dbo.tblUser'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblUser_Email ON dbo.[tblUser] ([Email]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblUser_EmployeeId' AND object_id = OBJECT_ID(N'dbo.tblUser'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblUser_EmployeeId ON dbo.[tblUser] ([EmployeeId])
        WHERE [EmployeeId] IS NOT NULL AND [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblRole_RoleName' AND object_id = OBJECT_ID(N'dbo.tblRole'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblRole_RoleName ON dbo.[tblRole] ([RoleName]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblPermission_PermissionCode' AND object_id = OBJECT_ID(N'dbo.tblPermission'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblPermission_PermissionCode ON dbo.[tblPermission] ([PermissionCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblUserPasswordHistory_UserId_CreatedDate' AND object_id = OBJECT_ID(N'dbo.tblUserPasswordHistory'))
    CREATE NONCLUSTERED INDEX IX_tblUserPasswordHistory_UserId_CreatedDate ON dbo.[tblUserPasswordHistory] ([UserId], [CreatedDate]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblLoginAudit_EventDate' AND object_id = OBJECT_ID(N'dbo.tblLoginAudit'))
    CREATE NONCLUSTERED INDEX IX_tblLoginAudit_EventDate ON dbo.[tblLoginAudit] ([EventDate]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblLoginAudit_UsernameAttempted_EventDate' AND object_id = OBJECT_ID(N'dbo.tblLoginAudit'))
    CREATE NONCLUSTERED INDEX IX_tblLoginAudit_UsernameAttempted_EventDate ON dbo.[tblLoginAudit] ([UsernameAttempted], [EventDate]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAuditLog_EntityName_EntityKey_EventDate' AND object_id = OBJECT_ID(N'dbo.tblAuditLog'))
    CREATE NONCLUSTERED INDEX IX_tblAuditLog_EntityName_EntityKey_EventDate ON dbo.[tblAuditLog] ([EntityName], [EntityKey], [EventDate]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAuditLog_EventDate' AND object_id = OBJECT_ID(N'dbo.tblAuditLog'))
    CREATE NONCLUSTERED INDEX IX_tblAuditLog_EventDate ON dbo.[tblAuditLog] ([EventDate]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAuditLog_PerformedBy_EventDate' AND object_id = OBJECT_ID(N'dbo.tblAuditLog'))
    CREATE NONCLUSTERED INDEX IX_tblAuditLog_PerformedBy_EventDate ON dbo.[tblAuditLog] ([PerformedBy], [EventDate]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAsset_AssetNumber' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAsset_AssetNumber ON dbo.[tblAsset] ([AssetNumber]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAsset_ManufacturerId_SerialNumber' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAsset_ManufacturerId_SerialNumber ON dbo.[tblAsset] ([ManufacturerId], [SerialNumber])
        WHERE [SerialNumber] IS NOT NULL AND [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAsset_Hostname' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAsset_Hostname ON dbo.[tblAsset] ([Hostname])
        WHERE [Hostname] IS NOT NULL AND [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_IsDeleted_AssetStatusId_AssetCategoryId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_IsDeleted_AssetStatusId_AssetCategoryId ON dbo.[tblAsset] ([IsDeleted], [AssetStatusId], [AssetCategoryId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_AssetName' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_AssetName ON dbo.[tblAsset] ([AssetName]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_SerialNumber' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_SerialNumber ON dbo.[tblAsset] ([SerialNumber]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_NextAuditDueDate' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_NextAuditDueDate ON dbo.[tblAsset] ([NextAuditDueDate])
        WHERE [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetAssignment_AssetId' AND object_id = OBJECT_ID(N'dbo.tblAssetAssignment'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetAssignment_AssetId ON dbo.[tblAssetAssignment] ([AssetId])
        WHERE [IsCurrent] = 1 AND [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_MovementStatus_RequestedDate' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMovement_MovementStatus_RequestedDate ON dbo.[tblAssetMovement] ([MovementStatus], [RequestedDate])
        WHERE [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMaintenance_MaintenanceStatus_ScheduledDate' AND object_id = OBJECT_ID(N'dbo.tblAssetMaintenance'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMaintenance_MaintenanceStatus_ScheduledDate ON dbo.[tblAssetMaintenance] ([MaintenanceStatus], [ScheduledDate])
        WHERE [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetAudit_AssetId_AuditCycle' AND object_id = OBJECT_ID(N'dbo.tblAssetAudit'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetAudit_AssetId_AuditCycle ON dbo.[tblAssetAudit] ([AssetId], [AuditCycle])
        WHERE [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetDisposal_AssetId' AND object_id = OBJECT_ID(N'dbo.tblAssetDisposal'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetDisposal_AssetId ON dbo.[tblAssetDisposal] ([AssetId])
        WHERE [DisposalStatus] IN (N'Requested', N'Approved', N'Completed') AND [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblWarranty_EndDate' AND object_id = OBJECT_ID(N'dbo.tblWarranty'))
    CREATE NONCLUSTERED INDEX IX_tblWarranty_EndDate ON dbo.[tblWarranty] ([EndDate])
        WHERE [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblSoftwareLicense_LicenseNumber' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicense'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblSoftwareLicense_LicenseNumber ON dbo.[tblSoftwareLicense] ([LicenseNumber]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicense_ExpiryDate' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicense'))
    CREATE NONCLUSTERED INDEX IX_tblSoftwareLicense_ExpiryDate ON dbo.[tblSoftwareLicense] ([ExpiryDate])
        WHERE [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicenseAllocation_SoftwareLicenseId_IsCurrent' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicenseAllocation'))
    CREATE NONCLUSTERED INDEX IX_tblSoftwareLicenseAllocation_SoftwareLicenseId_IsCurrent ON dbo.[tblSoftwareLicenseAllocation] ([SoftwareLicenseId], [IsCurrent])
        WHERE [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetQRCode_AssetId_QrVersion' AND object_id = OBJECT_ID(N'dbo.tblAssetQRCode'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetQRCode_AssetId_QrVersion ON dbo.[tblAssetQRCode] ([AssetId], [QrVersion]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetQRCode_AssetId' AND object_id = OBJECT_ID(N'dbo.tblAssetQRCode'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetQRCode_AssetId ON dbo.[tblAssetQRCode] ([AssetId])
        WHERE [IsCurrent] = 1 AND [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblQrLabelPrintLog_PrintBatchId' AND object_id = OBJECT_ID(N'dbo.tblQrLabelPrintLog'))
    CREATE NONCLUSTERED INDEX IX_tblQrLabelPrintLog_PrintBatchId ON dbo.[tblQrLabelPrintLog] ([PrintBatchId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblQrLabelPrintLog_PrintedDate' AND object_id = OBJECT_ID(N'dbo.tblQrLabelPrintLog'))
    CREATE NONCLUSTERED INDEX IX_tblQrLabelPrintLog_PrintedDate ON dbo.[tblQrLabelPrintLog] ([PrintedDate]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetDocument_StoredFileName' AND object_id = OBJECT_ID(N'dbo.tblAssetDocument'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetDocument_StoredFileName ON dbo.[tblAssetDocument] ([StoredFileName]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetHistory_AssetId_EventDate' AND object_id = OBJECT_ID(N'dbo.tblAssetHistory'))
    CREATE NONCLUSTERED INDEX IX_tblAssetHistory_AssetId_EventDate ON dbo.[tblAssetHistory] ([AssetId], [EventDate]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblNotification_UserId_IsRead_CreatedDate' AND object_id = OBJECT_ID(N'dbo.tblNotification'))
    CREATE NONCLUSTERED INDEX IX_tblNotification_UserId_IsRead_CreatedDate ON dbo.[tblNotification] ([UserId], [IsRead], [CreatedDate])
        WHERE [IsDeleted] = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblNotificationQueue_DeduplicationKey' AND object_id = OBJECT_ID(N'dbo.tblNotificationQueue'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblNotificationQueue_DeduplicationKey ON dbo.[tblNotificationQueue] ([DeduplicationKey])
        WHERE [DeduplicationKey] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblNotificationQueue_QueueStatus_NextAttemptDate' AND object_id = OBJECT_ID(N'dbo.tblNotificationQueue'))
    CREATE NONCLUSTERED INDEX IX_tblNotificationQueue_QueueStatus_NextAttemptDate ON dbo.[tblNotificationQueue] ([QueueStatus], [NextAttemptDate])
        WHERE [IsDeleted] = 0;
GO

/* ---------------------------------------------------- FOREIGN KEY SUPPORT INDEXES */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetCategory_AssetNumberSequenceId' AND object_id = OBJECT_ID(N'dbo.tblAssetCategory'))
    CREATE NONCLUSTERED INDEX IX_tblAssetCategory_AssetNumberSequenceId ON dbo.[tblAssetCategory] ([AssetNumberSequenceId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblDepartment_ParentDepartmentId' AND object_id = OBJECT_ID(N'dbo.tblDepartment'))
    CREATE NONCLUSTERED INDEX IX_tblDepartment_ParentDepartmentId ON dbo.[tblDepartment] ([ParentDepartmentId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblDepartment_HeadEmployeeId' AND object_id = OBJECT_ID(N'dbo.tblDepartment'))
    CREATE NONCLUSTERED INDEX IX_tblDepartment_HeadEmployeeId ON dbo.[tblDepartment] ([HeadEmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblEmployee_DepartmentId' AND object_id = OBJECT_ID(N'dbo.tblEmployee'))
    CREATE NONCLUSTERED INDEX IX_tblEmployee_DepartmentId ON dbo.[tblEmployee] ([DepartmentId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblEmployee_LocationId' AND object_id = OBJECT_ID(N'dbo.tblEmployee'))
    CREATE NONCLUSTERED INDEX IX_tblEmployee_LocationId ON dbo.[tblEmployee] ([LocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblEmployee_ManagerEmployeeId' AND object_id = OBJECT_ID(N'dbo.tblEmployee'))
    CREATE NONCLUSTERED INDEX IX_tblEmployee_ManagerEmployeeId ON dbo.[tblEmployee] ([ManagerEmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblUser_EmployeeId' AND object_id = OBJECT_ID(N'dbo.tblUser'))
    CREATE NONCLUSTERED INDEX IX_tblUser_EmployeeId ON dbo.[tblUser] ([EmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblRolePermission_PermissionId' AND object_id = OBJECT_ID(N'dbo.tblRolePermission'))
    CREATE NONCLUSTERED INDEX IX_tblRolePermission_PermissionId ON dbo.[tblRolePermission] ([PermissionId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblUserRole_RoleId' AND object_id = OBJECT_ID(N'dbo.tblUserRole'))
    CREATE NONCLUSTERED INDEX IX_tblUserRole_RoleId ON dbo.[tblUserRole] ([RoleId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblLoginAudit_UserId' AND object_id = OBJECT_ID(N'dbo.tblLoginAudit'))
    CREATE NONCLUSTERED INDEX IX_tblLoginAudit_UserId ON dbo.[tblLoginAudit] ([UserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_AssetCategoryId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_AssetCategoryId ON dbo.[tblAsset] ([AssetCategoryId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_AssetSubCategoryId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_AssetSubCategoryId ON dbo.[tblAsset] ([AssetSubCategoryId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_AssetStatusId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_AssetStatusId ON dbo.[tblAsset] ([AssetStatusId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_ManufacturerId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_ManufacturerId ON dbo.[tblAsset] ([ManufacturerId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_VendorId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_VendorId ON dbo.[tblAsset] ([VendorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_CurrencyId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_CurrencyId ON dbo.[tblAsset] ([CurrencyId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_AssignedEmployeeId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_AssignedEmployeeId ON dbo.[tblAsset] ([AssignedEmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_DepartmentId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_DepartmentId ON dbo.[tblAsset] ([DepartmentId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_LocationId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_LocationId ON dbo.[tblAsset] ([LocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_BuildingId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_BuildingId ON dbo.[tblAsset] ([BuildingId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_FloorId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_FloorId ON dbo.[tblAsset] ([FloorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_RoomId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_RoomId ON dbo.[tblAsset] ([RoomId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_AssetOwnerEmployeeId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_AssetOwnerEmployeeId ON dbo.[tblAsset] ([AssetOwnerEmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_ClassificationId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_ClassificationId ON dbo.[tblAsset] ([ClassificationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_CriticalityId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_CriticalityId ON dbo.[tblAsset] ([CriticalityId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_RiskLevelId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_RiskLevelId ON dbo.[tblAsset] ([RiskLevelId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAssignment_AssetId' AND object_id = OBJECT_ID(N'dbo.tblAssetAssignment'))
    CREATE NONCLUSTERED INDEX IX_tblAssetAssignment_AssetId ON dbo.[tblAssetAssignment] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAssignment_EmployeeId' AND object_id = OBJECT_ID(N'dbo.tblAssetAssignment'))
    CREATE NONCLUSTERED INDEX IX_tblAssetAssignment_EmployeeId ON dbo.[tblAssetAssignment] ([EmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAssignment_DepartmentId' AND object_id = OBJECT_ID(N'dbo.tblAssetAssignment'))
    CREATE NONCLUSTERED INDEX IX_tblAssetAssignment_DepartmentId ON dbo.[tblAssetAssignment] ([DepartmentId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAssignment_LocationId' AND object_id = OBJECT_ID(N'dbo.tblAssetAssignment'))
    CREATE NONCLUSTERED INDEX IX_tblAssetAssignment_LocationId ON dbo.[tblAssetAssignment] ([LocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_AssetId' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMovement_AssetId ON dbo.[tblAssetMovement] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_FromLocationId' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMovement_FromLocationId ON dbo.[tblAssetMovement] ([FromLocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_FromBuildingId' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMovement_FromBuildingId ON dbo.[tblAssetMovement] ([FromBuildingId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_FromFloorId' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMovement_FromFloorId ON dbo.[tblAssetMovement] ([FromFloorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_FromRoomId' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMovement_FromRoomId ON dbo.[tblAssetMovement] ([FromRoomId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_ToLocationId' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMovement_ToLocationId ON dbo.[tblAssetMovement] ([ToLocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_ToBuildingId' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMovement_ToBuildingId ON dbo.[tblAssetMovement] ([ToBuildingId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_ToFloorId' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMovement_ToFloorId ON dbo.[tblAssetMovement] ([ToFloorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_ToRoomId' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMovement_ToRoomId ON dbo.[tblAssetMovement] ([ToRoomId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMaintenance_AssetId' AND object_id = OBJECT_ID(N'dbo.tblAssetMaintenance'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMaintenance_AssetId ON dbo.[tblAssetMaintenance] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMaintenance_VendorId' AND object_id = OBJECT_ID(N'dbo.tblAssetMaintenance'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMaintenance_VendorId ON dbo.[tblAssetMaintenance] ([VendorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMaintenance_CurrencyId' AND object_id = OBJECT_ID(N'dbo.tblAssetMaintenance'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMaintenance_CurrencyId ON dbo.[tblAssetMaintenance] ([CurrencyId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAudit_AssetId' AND object_id = OBJECT_ID(N'dbo.tblAssetAudit'))
    CREATE NONCLUSTERED INDEX IX_tblAssetAudit_AssetId ON dbo.[tblAssetAudit] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAudit_FoundLocationId' AND object_id = OBJECT_ID(N'dbo.tblAssetAudit'))
    CREATE NONCLUSTERED INDEX IX_tblAssetAudit_FoundLocationId ON dbo.[tblAssetAudit] ([FoundLocationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAudit_FoundRoomId' AND object_id = OBJECT_ID(N'dbo.tblAssetAudit'))
    CREATE NONCLUSTERED INDEX IX_tblAssetAudit_FoundRoomId ON dbo.[tblAssetAudit] ([FoundRoomId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAudit_FoundEmployeeId' AND object_id = OBJECT_ID(N'dbo.tblAssetAudit'))
    CREATE NONCLUSTERED INDEX IX_tblAssetAudit_FoundEmployeeId ON dbo.[tblAssetAudit] ([FoundEmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetDisposal_AssetId' AND object_id = OBJECT_ID(N'dbo.tblAssetDisposal'))
    CREATE NONCLUSTERED INDEX IX_tblAssetDisposal_AssetId ON dbo.[tblAssetDisposal] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetDisposal_DisposalMethodId' AND object_id = OBJECT_ID(N'dbo.tblAssetDisposal'))
    CREATE NONCLUSTERED INDEX IX_tblAssetDisposal_DisposalMethodId ON dbo.[tblAssetDisposal] ([DisposalMethodId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetDisposal_CurrencyId' AND object_id = OBJECT_ID(N'dbo.tblAssetDisposal'))
    CREATE NONCLUSTERED INDEX IX_tblAssetDisposal_CurrencyId ON dbo.[tblAssetDisposal] ([CurrencyId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblWarranty_AssetId' AND object_id = OBJECT_ID(N'dbo.tblWarranty'))
    CREATE NONCLUSTERED INDEX IX_tblWarranty_AssetId ON dbo.[tblWarranty] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblWarranty_WarrantyStatusId' AND object_id = OBJECT_ID(N'dbo.tblWarranty'))
    CREATE NONCLUSTERED INDEX IX_tblWarranty_WarrantyStatusId ON dbo.[tblWarranty] ([WarrantyStatusId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblWarranty_ProviderVendorId' AND object_id = OBJECT_ID(N'dbo.tblWarranty'))
    CREATE NONCLUSTERED INDEX IX_tblWarranty_ProviderVendorId ON dbo.[tblWarranty] ([ProviderVendorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblWarranty_CurrencyId' AND object_id = OBJECT_ID(N'dbo.tblWarranty'))
    CREATE NONCLUSTERED INDEX IX_tblWarranty_CurrencyId ON dbo.[tblWarranty] ([CurrencyId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicense_PublisherManufacturerId' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicense'))
    CREATE NONCLUSTERED INDEX IX_tblSoftwareLicense_PublisherManufacturerId ON dbo.[tblSoftwareLicense] ([PublisherManufacturerId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicense_VendorId' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicense'))
    CREATE NONCLUSTERED INDEX IX_tblSoftwareLicense_VendorId ON dbo.[tblSoftwareLicense] ([VendorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicense_CurrencyId' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicense'))
    CREATE NONCLUSTERED INDEX IX_tblSoftwareLicense_CurrencyId ON dbo.[tblSoftwareLicense] ([CurrencyId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicense_ContractOwnerEmployeeId' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicense'))
    CREATE NONCLUSTERED INDEX IX_tblSoftwareLicense_ContractOwnerEmployeeId ON dbo.[tblSoftwareLicense] ([ContractOwnerEmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicenseAllocation_SoftwareLicenseId' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicenseAllocation'))
    CREATE NONCLUSTERED INDEX IX_tblSoftwareLicenseAllocation_SoftwareLicenseId ON dbo.[tblSoftwareLicenseAllocation] ([SoftwareLicenseId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicenseAllocation_AssetId' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicenseAllocation'))
    CREATE NONCLUSTERED INDEX IX_tblSoftwareLicenseAllocation_AssetId ON dbo.[tblSoftwareLicenseAllocation] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicenseAllocation_EmployeeId' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicenseAllocation'))
    CREATE NONCLUSTERED INDEX IX_tblSoftwareLicenseAllocation_EmployeeId ON dbo.[tblSoftwareLicenseAllocation] ([EmployeeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblQrLabelPrintLog_AssetQRCodeId' AND object_id = OBJECT_ID(N'dbo.tblQrLabelPrintLog'))
    CREATE NONCLUSTERED INDEX IX_tblQrLabelPrintLog_AssetQRCodeId ON dbo.[tblQrLabelPrintLog] ([AssetQRCodeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblQrLabelPrintLog_AssetId' AND object_id = OBJECT_ID(N'dbo.tblQrLabelPrintLog'))
    CREATE NONCLUSTERED INDEX IX_tblQrLabelPrintLog_AssetId ON dbo.[tblQrLabelPrintLog] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetDocument_AssetId' AND object_id = OBJECT_ID(N'dbo.tblAssetDocument'))
    CREATE NONCLUSTERED INDEX IX_tblAssetDocument_AssetId ON dbo.[tblAssetDocument] ([AssetId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetDocument_ClassificationId' AND object_id = OBJECT_ID(N'dbo.tblAssetDocument'))
    CREATE NONCLUSTERED INDEX IX_tblAssetDocument_ClassificationId ON dbo.[tblAssetDocument] ([ClassificationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblNotification_UserId' AND object_id = OBJECT_ID(N'dbo.tblNotification'))
    CREATE NONCLUSTERED INDEX IX_tblNotification_UserId ON dbo.[tblNotification] ([UserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblNotificationQueue_RecipientUserId' AND object_id = OBJECT_ID(N'dbo.tblNotificationQueue'))
    CREATE NONCLUSTERED INDEX IX_tblNotificationQueue_RecipientUserId ON dbo.[tblNotificationQueue] ([RecipientUserId]);
GO

PRINT 'Script 02 completed.';
GO
