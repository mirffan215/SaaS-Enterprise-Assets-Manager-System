/* =====================================================================
   Script 10 ROLLBACK - returns the database to the v1.1 single-company schema.
   Only possible while AMGH (CompanyId 1) is the only company: it refuses otherwise.
   Data rows are not touched. Afterwards deploy the v1.1 web application.
   Variables: DatabaseName, AllowMultiCompany=YES
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF N'$(AllowMultiCompany)' <> N'YES' THROW 52010, N'Rollback not enabled (AllowMultiCompany=YES required).', 1;
GO
IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'CompanyIsolation') DROP SECURITY POLICY sec.CompanyIsolation;
GO
IF OBJECT_ID(N'dbo.tblCompany', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.tblCompany WHERE CompanyId <> 1)
    THROW 52011, N'Other companies exist; rollback would merge their data into AMGH. Restore the pre-migration backup instead.', 1;
GO
DROP FUNCTION IF EXISTS sec.fn_CompanyAccess;
GO
IF OBJECT_ID(N'dbo.FK_tblUserRole_Membership', N'F') IS NOT NULL ALTER TABLE dbo.tblUserRole DROP CONSTRAINT FK_tblUserRole_Membership;
IF OBJECT_ID(N'dbo.FK_tblCompanyUser_Employee', N'F') IS NOT NULL ALTER TABLE dbo.tblCompanyUser DROP CONSTRAINT FK_tblCompanyUser_Employee;
GO
/* composite FKs -> original single-column FKs */
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetCategory_AssetNumberSequenceId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetCategory] DROP CONSTRAINT FK_tblAssetCategory_AssetNumberSequenceId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetCategory_AssetNumberSequenceId')
    ALTER TABLE dbo.[tblAssetCategory] ADD CONSTRAINT FK_tblAssetCategory_AssetNumberSequenceId FOREIGN KEY ([AssetNumberSequenceId]) REFERENCES dbo.[tblAssetNumberSequence] ([AssetNumberSequenceId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetSubCategory_AssetCategoryId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetSubCategory] DROP CONSTRAINT FK_tblAssetSubCategory_AssetCategoryId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetSubCategory_AssetCategoryId')
    ALTER TABLE dbo.[tblAssetSubCategory] ADD CONSTRAINT FK_tblAssetSubCategory_AssetCategoryId FOREIGN KEY ([AssetCategoryId]) REFERENCES dbo.[tblAssetCategory] ([AssetCategoryId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblBuilding_LocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblBuilding] DROP CONSTRAINT FK_tblBuilding_LocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblBuilding_LocationId')
    ALTER TABLE dbo.[tblBuilding] ADD CONSTRAINT FK_tblBuilding_LocationId FOREIGN KEY ([LocationId]) REFERENCES dbo.[tblLocation] ([LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblFloor_BuildingId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblFloor] DROP CONSTRAINT FK_tblFloor_BuildingId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblFloor_BuildingId')
    ALTER TABLE dbo.[tblFloor] ADD CONSTRAINT FK_tblFloor_BuildingId FOREIGN KEY ([BuildingId]) REFERENCES dbo.[tblBuilding] ([BuildingId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblRoom_FloorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblRoom] DROP CONSTRAINT FK_tblRoom_FloorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblRoom_FloorId')
    ALTER TABLE dbo.[tblRoom] ADD CONSTRAINT FK_tblRoom_FloorId FOREIGN KEY ([FloorId]) REFERENCES dbo.[tblFloor] ([FloorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblDepartment_ParentDepartmentId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblDepartment] DROP CONSTRAINT FK_tblDepartment_ParentDepartmentId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblDepartment_ParentDepartmentId')
    ALTER TABLE dbo.[tblDepartment] ADD CONSTRAINT FK_tblDepartment_ParentDepartmentId FOREIGN KEY ([ParentDepartmentId]) REFERENCES dbo.[tblDepartment] ([DepartmentId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblDepartment_HeadEmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblDepartment] DROP CONSTRAINT FK_tblDepartment_HeadEmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblDepartment_HeadEmployeeId')
    ALTER TABLE dbo.[tblDepartment] ADD CONSTRAINT FK_tblDepartment_HeadEmployeeId FOREIGN KEY ([HeadEmployeeId]) REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblEmployee_DepartmentId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblEmployee] DROP CONSTRAINT FK_tblEmployee_DepartmentId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblEmployee_DepartmentId')
    ALTER TABLE dbo.[tblEmployee] ADD CONSTRAINT FK_tblEmployee_DepartmentId FOREIGN KEY ([DepartmentId]) REFERENCES dbo.[tblDepartment] ([DepartmentId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblEmployee_LocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblEmployee] DROP CONSTRAINT FK_tblEmployee_LocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblEmployee_LocationId')
    ALTER TABLE dbo.[tblEmployee] ADD CONSTRAINT FK_tblEmployee_LocationId FOREIGN KEY ([LocationId]) REFERENCES dbo.[tblLocation] ([LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblEmployee_ManagerEmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblEmployee] DROP CONSTRAINT FK_tblEmployee_ManagerEmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblEmployee_ManagerEmployeeId')
    ALTER TABLE dbo.[tblEmployee] ADD CONSTRAINT FK_tblEmployee_ManagerEmployeeId FOREIGN KEY ([ManagerEmployeeId]) REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblRolePermission_RoleId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblRolePermission] DROP CONSTRAINT FK_tblRolePermission_RoleId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblRolePermission_RoleId')
    ALTER TABLE dbo.[tblRolePermission] ADD CONSTRAINT FK_tblRolePermission_RoleId FOREIGN KEY ([RoleId]) REFERENCES dbo.[tblRole] ([RoleId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblUserRole_RoleId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblUserRole] DROP CONSTRAINT FK_tblUserRole_RoleId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblUserRole_RoleId')
    ALTER TABLE dbo.[tblUserRole] ADD CONSTRAINT FK_tblUserRole_RoleId FOREIGN KEY ([RoleId]) REFERENCES dbo.[tblRole] ([RoleId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_AssetCategoryId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_AssetCategoryId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssetCategoryId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_AssetCategoryId FOREIGN KEY ([AssetCategoryId]) REFERENCES dbo.[tblAssetCategory] ([AssetCategoryId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_AssetSubCategoryId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_AssetSubCategoryId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssetSubCategoryId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_AssetSubCategoryId FOREIGN KEY ([AssetSubCategoryId]) REFERENCES dbo.[tblAssetSubCategory] ([AssetSubCategoryId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_ManufacturerId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_ManufacturerId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_ManufacturerId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_ManufacturerId FOREIGN KEY ([ManufacturerId]) REFERENCES dbo.[tblManufacturer] ([ManufacturerId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_VendorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_VendorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_VendorId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_VendorId FOREIGN KEY ([VendorId]) REFERENCES dbo.[tblVendor] ([VendorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_AssignedEmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_AssignedEmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssignedEmployeeId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_AssignedEmployeeId FOREIGN KEY ([AssignedEmployeeId]) REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_DepartmentId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_DepartmentId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_DepartmentId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_DepartmentId FOREIGN KEY ([DepartmentId]) REFERENCES dbo.[tblDepartment] ([DepartmentId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_LocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_LocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_LocationId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_LocationId FOREIGN KEY ([LocationId]) REFERENCES dbo.[tblLocation] ([LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_BuildingId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_BuildingId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_BuildingId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_BuildingId FOREIGN KEY ([BuildingId]) REFERENCES dbo.[tblBuilding] ([BuildingId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_FloorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_FloorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_FloorId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_FloorId FOREIGN KEY ([FloorId]) REFERENCES dbo.[tblFloor] ([FloorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_RoomId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_RoomId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_RoomId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_RoomId FOREIGN KEY ([RoomId]) REFERENCES dbo.[tblRoom] ([RoomId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_AssetOwnerEmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_AssetOwnerEmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssetOwnerEmployeeId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_AssetOwnerEmployeeId FOREIGN KEY ([AssetOwnerEmployeeId]) REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_ClassificationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_ClassificationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_ClassificationId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_ClassificationId FOREIGN KEY ([ClassificationId]) REFERENCES dbo.[tblClassification] ([ClassificationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_CriticalityId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_CriticalityId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_CriticalityId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_CriticalityId FOREIGN KEY ([CriticalityId]) REFERENCES dbo.[tblCriticality] ([CriticalityId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_RiskLevelId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_RiskLevelId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_RiskLevelId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_RiskLevelId FOREIGN KEY ([RiskLevelId]) REFERENCES dbo.[tblRiskLevel] ([RiskLevelId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAssignment_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetAssignment] DROP CONSTRAINT FK_tblAssetAssignment_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_AssetId')
    ALTER TABLE dbo.[tblAssetAssignment] ADD CONSTRAINT FK_tblAssetAssignment_AssetId FOREIGN KEY ([AssetId]) REFERENCES dbo.[tblAsset] ([AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAssignment_EmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetAssignment] DROP CONSTRAINT FK_tblAssetAssignment_EmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_EmployeeId')
    ALTER TABLE dbo.[tblAssetAssignment] ADD CONSTRAINT FK_tblAssetAssignment_EmployeeId FOREIGN KEY ([EmployeeId]) REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAssignment_DepartmentId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetAssignment] DROP CONSTRAINT FK_tblAssetAssignment_DepartmentId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_DepartmentId')
    ALTER TABLE dbo.[tblAssetAssignment] ADD CONSTRAINT FK_tblAssetAssignment_DepartmentId FOREIGN KEY ([DepartmentId]) REFERENCES dbo.[tblDepartment] ([DepartmentId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAssignment_LocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetAssignment] DROP CONSTRAINT FK_tblAssetAssignment_LocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_LocationId')
    ALTER TABLE dbo.[tblAssetAssignment] ADD CONSTRAINT FK_tblAssetAssignment_LocationId FOREIGN KEY ([LocationId]) REFERENCES dbo.[tblLocation] ([LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_AssetId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_AssetId FOREIGN KEY ([AssetId]) REFERENCES dbo.[tblAsset] ([AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_FromLocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_FromLocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_FromLocationId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_FromLocationId FOREIGN KEY ([FromLocationId]) REFERENCES dbo.[tblLocation] ([LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_FromBuildingId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_FromBuildingId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_FromBuildingId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_FromBuildingId FOREIGN KEY ([FromBuildingId]) REFERENCES dbo.[tblBuilding] ([BuildingId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_FromFloorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_FromFloorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_FromFloorId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_FromFloorId FOREIGN KEY ([FromFloorId]) REFERENCES dbo.[tblFloor] ([FloorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_FromRoomId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_FromRoomId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_FromRoomId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_FromRoomId FOREIGN KEY ([FromRoomId]) REFERENCES dbo.[tblRoom] ([RoomId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_ToLocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_ToLocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_ToLocationId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_ToLocationId FOREIGN KEY ([ToLocationId]) REFERENCES dbo.[tblLocation] ([LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_ToBuildingId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_ToBuildingId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_ToBuildingId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_ToBuildingId FOREIGN KEY ([ToBuildingId]) REFERENCES dbo.[tblBuilding] ([BuildingId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_ToFloorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_ToFloorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_ToFloorId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_ToFloorId FOREIGN KEY ([ToFloorId]) REFERENCES dbo.[tblFloor] ([FloorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_ToRoomId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_ToRoomId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_ToRoomId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_ToRoomId FOREIGN KEY ([ToRoomId]) REFERENCES dbo.[tblRoom] ([RoomId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMaintenance_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetMaintenance] DROP CONSTRAINT FK_tblAssetMaintenance_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMaintenance_AssetId')
    ALTER TABLE dbo.[tblAssetMaintenance] ADD CONSTRAINT FK_tblAssetMaintenance_AssetId FOREIGN KEY ([AssetId]) REFERENCES dbo.[tblAsset] ([AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMaintenance_VendorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetMaintenance] DROP CONSTRAINT FK_tblAssetMaintenance_VendorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMaintenance_VendorId')
    ALTER TABLE dbo.[tblAssetMaintenance] ADD CONSTRAINT FK_tblAssetMaintenance_VendorId FOREIGN KEY ([VendorId]) REFERENCES dbo.[tblVendor] ([VendorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAudit_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetAudit] DROP CONSTRAINT FK_tblAssetAudit_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_AssetId')
    ALTER TABLE dbo.[tblAssetAudit] ADD CONSTRAINT FK_tblAssetAudit_AssetId FOREIGN KEY ([AssetId]) REFERENCES dbo.[tblAsset] ([AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAudit_FoundLocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetAudit] DROP CONSTRAINT FK_tblAssetAudit_FoundLocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_FoundLocationId')
    ALTER TABLE dbo.[tblAssetAudit] ADD CONSTRAINT FK_tblAssetAudit_FoundLocationId FOREIGN KEY ([FoundLocationId]) REFERENCES dbo.[tblLocation] ([LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAudit_FoundRoomId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetAudit] DROP CONSTRAINT FK_tblAssetAudit_FoundRoomId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_FoundRoomId')
    ALTER TABLE dbo.[tblAssetAudit] ADD CONSTRAINT FK_tblAssetAudit_FoundRoomId FOREIGN KEY ([FoundRoomId]) REFERENCES dbo.[tblRoom] ([RoomId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAudit_FoundEmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetAudit] DROP CONSTRAINT FK_tblAssetAudit_FoundEmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_FoundEmployeeId')
    ALTER TABLE dbo.[tblAssetAudit] ADD CONSTRAINT FK_tblAssetAudit_FoundEmployeeId FOREIGN KEY ([FoundEmployeeId]) REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetDisposal_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetDisposal] DROP CONSTRAINT FK_tblAssetDisposal_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDisposal_AssetId')
    ALTER TABLE dbo.[tblAssetDisposal] ADD CONSTRAINT FK_tblAssetDisposal_AssetId FOREIGN KEY ([AssetId]) REFERENCES dbo.[tblAsset] ([AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblWarranty_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblWarranty] DROP CONSTRAINT FK_tblWarranty_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblWarranty_AssetId')
    ALTER TABLE dbo.[tblWarranty] ADD CONSTRAINT FK_tblWarranty_AssetId FOREIGN KEY ([AssetId]) REFERENCES dbo.[tblAsset] ([AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblWarranty_ProviderVendorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblWarranty] DROP CONSTRAINT FK_tblWarranty_ProviderVendorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblWarranty_ProviderVendorId')
    ALTER TABLE dbo.[tblWarranty] ADD CONSTRAINT FK_tblWarranty_ProviderVendorId FOREIGN KEY ([ProviderVendorId]) REFERENCES dbo.[tblVendor] ([VendorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblSoftwareLicense_PublisherManufacturerId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblSoftwareLicense] DROP CONSTRAINT FK_tblSoftwareLicense_PublisherManufacturerId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicense_PublisherManufacturerId')
    ALTER TABLE dbo.[tblSoftwareLicense] ADD CONSTRAINT FK_tblSoftwareLicense_PublisherManufacturerId FOREIGN KEY ([PublisherManufacturerId]) REFERENCES dbo.[tblManufacturer] ([ManufacturerId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblSoftwareLicense_VendorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblSoftwareLicense] DROP CONSTRAINT FK_tblSoftwareLicense_VendorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicense_VendorId')
    ALTER TABLE dbo.[tblSoftwareLicense] ADD CONSTRAINT FK_tblSoftwareLicense_VendorId FOREIGN KEY ([VendorId]) REFERENCES dbo.[tblVendor] ([VendorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblSoftwareLicense_ContractOwnerEmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblSoftwareLicense] DROP CONSTRAINT FK_tblSoftwareLicense_ContractOwnerEmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicense_ContractOwnerEmployeeId')
    ALTER TABLE dbo.[tblSoftwareLicense] ADD CONSTRAINT FK_tblSoftwareLicense_ContractOwnerEmployeeId FOREIGN KEY ([ContractOwnerEmployeeId]) REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblSoftwareLicenseAllocation_SoftwareLicenseId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] DROP CONSTRAINT FK_tblSoftwareLicenseAllocation_SoftwareLicenseId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicenseAllocation_SoftwareLicenseId')
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] ADD CONSTRAINT FK_tblSoftwareLicenseAllocation_SoftwareLicenseId FOREIGN KEY ([SoftwareLicenseId]) REFERENCES dbo.[tblSoftwareLicense] ([SoftwareLicenseId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblSoftwareLicenseAllocation_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] DROP CONSTRAINT FK_tblSoftwareLicenseAllocation_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicenseAllocation_AssetId')
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] ADD CONSTRAINT FK_tblSoftwareLicenseAllocation_AssetId FOREIGN KEY ([AssetId]) REFERENCES dbo.[tblAsset] ([AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblSoftwareLicenseAllocation_EmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] DROP CONSTRAINT FK_tblSoftwareLicenseAllocation_EmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicenseAllocation_EmployeeId')
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] ADD CONSTRAINT FK_tblSoftwareLicenseAllocation_EmployeeId FOREIGN KEY ([EmployeeId]) REFERENCES dbo.[tblEmployee] ([EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetQRCode_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetQRCode] DROP CONSTRAINT FK_tblAssetQRCode_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetQRCode_AssetId')
    ALTER TABLE dbo.[tblAssetQRCode] ADD CONSTRAINT FK_tblAssetQRCode_AssetId FOREIGN KEY ([AssetId]) REFERENCES dbo.[tblAsset] ([AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblQrLabelPrintLog_AssetQRCodeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblQrLabelPrintLog] DROP CONSTRAINT FK_tblQrLabelPrintLog_AssetQRCodeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblQrLabelPrintLog_AssetQRCodeId')
    ALTER TABLE dbo.[tblQrLabelPrintLog] ADD CONSTRAINT FK_tblQrLabelPrintLog_AssetQRCodeId FOREIGN KEY ([AssetQRCodeId]) REFERENCES dbo.[tblAssetQRCode] ([AssetQRCodeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblQrLabelPrintLog_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblQrLabelPrintLog] DROP CONSTRAINT FK_tblQrLabelPrintLog_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblQrLabelPrintLog_AssetId')
    ALTER TABLE dbo.[tblQrLabelPrintLog] ADD CONSTRAINT FK_tblQrLabelPrintLog_AssetId FOREIGN KEY ([AssetId]) REFERENCES dbo.[tblAsset] ([AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetDocument_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetDocument] DROP CONSTRAINT FK_tblAssetDocument_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDocument_AssetId')
    ALTER TABLE dbo.[tblAssetDocument] ADD CONSTRAINT FK_tblAssetDocument_AssetId FOREIGN KEY ([AssetId]) REFERENCES dbo.[tblAsset] ([AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetDocument_ClassificationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetDocument] DROP CONSTRAINT FK_tblAssetDocument_ClassificationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDocument_ClassificationId')
    ALTER TABLE dbo.[tblAssetDocument] ADD CONSTRAINT FK_tblAssetDocument_ClassificationId FOREIGN KEY ([ClassificationId]) REFERENCES dbo.[tblClassification] ([ClassificationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetHistory_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 2)
    ALTER TABLE dbo.[tblAssetHistory] DROP CONSTRAINT FK_tblAssetHistory_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetHistory_AssetId')
    ALTER TABLE dbo.[tblAssetHistory] ADD CONSTRAINT FK_tblAssetHistory_AssetId FOREIGN KEY ([AssetId]) REFERENCES dbo.[tblAsset] ([AssetId]);
GO
/* per-company uniqueness -> original */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetNumberSequence_SequenceCode' AND object_id = OBJECT_ID(N'dbo.tblAssetNumberSequence'))
    DROP INDEX UX_tblAssetNumberSequence_SequenceCode ON dbo.[tblAssetNumberSequence];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetNumberSequence_SequenceCode ON dbo.[tblAssetNumberSequence] ([SequenceCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetCategory_CategoryCode' AND object_id = OBJECT_ID(N'dbo.tblAssetCategory'))
    DROP INDEX UX_tblAssetCategory_CategoryCode ON dbo.[tblAssetCategory];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetCategory_CategoryCode ON dbo.[tblAssetCategory] ([CategoryCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetCategory_CategoryName' AND object_id = OBJECT_ID(N'dbo.tblAssetCategory'))
    DROP INDEX UX_tblAssetCategory_CategoryName ON dbo.[tblAssetCategory];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetCategory_CategoryName ON dbo.[tblAssetCategory] ([CategoryName]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetSubCategory_AssetCategoryId_SubCategoryCode' AND object_id = OBJECT_ID(N'dbo.tblAssetSubCategory'))
    DROP INDEX UX_tblAssetSubCategory_AssetCategoryId_SubCategoryCode ON dbo.[tblAssetSubCategory];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetSubCategory_AssetCategoryId_SubCategoryCode ON dbo.[tblAssetSubCategory] ([AssetCategoryId], [SubCategoryCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblManufacturer_ManufacturerCode' AND object_id = OBJECT_ID(N'dbo.tblManufacturer'))
    DROP INDEX UX_tblManufacturer_ManufacturerCode ON dbo.[tblManufacturer];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblManufacturer_ManufacturerCode ON dbo.[tblManufacturer] ([ManufacturerCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblManufacturer_ManufacturerName' AND object_id = OBJECT_ID(N'dbo.tblManufacturer'))
    DROP INDEX UX_tblManufacturer_ManufacturerName ON dbo.[tblManufacturer];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblManufacturer_ManufacturerName ON dbo.[tblManufacturer] ([ManufacturerName]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblVendor_VendorCode' AND object_id = OBJECT_ID(N'dbo.tblVendor'))
    DROP INDEX UX_tblVendor_VendorCode ON dbo.[tblVendor];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblVendor_VendorCode ON dbo.[tblVendor] ([VendorCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblLocation_LocationCode' AND object_id = OBJECT_ID(N'dbo.tblLocation'))
    DROP INDEX UX_tblLocation_LocationCode ON dbo.[tblLocation];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblLocation_LocationCode ON dbo.[tblLocation] ([LocationCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblBuilding_LocationId_BuildingCode' AND object_id = OBJECT_ID(N'dbo.tblBuilding'))
    DROP INDEX UX_tblBuilding_LocationId_BuildingCode ON dbo.[tblBuilding];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblBuilding_LocationId_BuildingCode ON dbo.[tblBuilding] ([LocationId], [BuildingCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblFloor_BuildingId_FloorCode' AND object_id = OBJECT_ID(N'dbo.tblFloor'))
    DROP INDEX UX_tblFloor_BuildingId_FloorCode ON dbo.[tblFloor];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblFloor_BuildingId_FloorCode ON dbo.[tblFloor] ([BuildingId], [FloorCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblRoom_FloorId_RoomCode' AND object_id = OBJECT_ID(N'dbo.tblRoom'))
    DROP INDEX UX_tblRoom_FloorId_RoomCode ON dbo.[tblRoom];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblRoom_FloorId_RoomCode ON dbo.[tblRoom] ([FloorId], [RoomCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblDepartment_DepartmentCode' AND object_id = OBJECT_ID(N'dbo.tblDepartment'))
    DROP INDEX UX_tblDepartment_DepartmentCode ON dbo.[tblDepartment];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblDepartment_DepartmentCode ON dbo.[tblDepartment] ([DepartmentCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblEmployee_EmployeeNumber' AND object_id = OBJECT_ID(N'dbo.tblEmployee'))
    DROP INDEX UX_tblEmployee_EmployeeNumber ON dbo.[tblEmployee];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblEmployee_EmployeeNumber ON dbo.[tblEmployee] ([EmployeeNumber]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblEmployee_Email' AND object_id = OBJECT_ID(N'dbo.tblEmployee'))
    DROP INDEX UX_tblEmployee_Email ON dbo.[tblEmployee];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblEmployee_Email ON dbo.[tblEmployee] ([Email]) WHERE [Email] IS NOT NULL AND [IsDeleted] = 0;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblClassification_ClassificationCode' AND object_id = OBJECT_ID(N'dbo.tblClassification'))
    DROP INDEX UX_tblClassification_ClassificationCode ON dbo.[tblClassification];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblClassification_ClassificationCode ON dbo.[tblClassification] ([ClassificationCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblClassification_ClassificationLevel' AND object_id = OBJECT_ID(N'dbo.tblClassification'))
    DROP INDEX UX_tblClassification_ClassificationLevel ON dbo.[tblClassification];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblClassification_ClassificationLevel ON dbo.[tblClassification] ([ClassificationLevel]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblRiskLevel_RiskLevelCode' AND object_id = OBJECT_ID(N'dbo.tblRiskLevel'))
    DROP INDEX UX_tblRiskLevel_RiskLevelCode ON dbo.[tblRiskLevel];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblRiskLevel_RiskLevelCode ON dbo.[tblRiskLevel] ([RiskLevelCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblCriticality_CriticalityCode' AND object_id = OBJECT_ID(N'dbo.tblCriticality'))
    DROP INDEX UX_tblCriticality_CriticalityCode ON dbo.[tblCriticality];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblCriticality_CriticalityCode ON dbo.[tblCriticality] ([CriticalityCode]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblCriticality_CriticalityLevel' AND object_id = OBJECT_ID(N'dbo.tblCriticality'))
    DROP INDEX UX_tblCriticality_CriticalityLevel ON dbo.[tblCriticality];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblCriticality_CriticalityLevel ON dbo.[tblCriticality] ([CriticalityLevel]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblRole_RoleName' AND object_id = OBJECT_ID(N'dbo.tblRole'))
    DROP INDEX UX_tblRole_RoleName ON dbo.[tblRole];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblRole_RoleName ON dbo.[tblRole] ([RoleName]);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAsset_ManufacturerId_SerialNumber' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    DROP INDEX UX_tblAsset_ManufacturerId_SerialNumber ON dbo.[tblAsset];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblAsset_ManufacturerId_SerialNumber ON dbo.[tblAsset] ([ManufacturerId], [SerialNumber]) WHERE [SerialNumber] IS NOT NULL AND [IsDeleted] = 0;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAsset_Hostname' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    DROP INDEX UX_tblAsset_Hostname ON dbo.[tblAsset];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblAsset_Hostname ON dbo.[tblAsset] ([Hostname]) WHERE [Hostname] IS NOT NULL AND [IsDeleted] = 0;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblNotificationQueue_DeduplicationKey' AND object_id = OBJECT_ID(N'dbo.tblNotificationQueue'))
    DROP INDEX UX_tblNotificationQueue_DeduplicationKey ON dbo.[tblNotificationQueue];
CREATE UNIQUE NONCLUSTERED INDEX UX_tblNotificationQueue_DeduplicationKey ON dbo.[tblNotificationQueue] ([DeduplicationKey]) WHERE [DeduplicationKey] IS NOT NULL;
GO
/* CompanyId columns */
IF COL_LENGTH(N'dbo.tblAssetNumberSequence', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAssetNumberSequence_CompanyId_AssetNumberSequenceId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAssetNumberSequence] DROP CONSTRAINT UQ_tblAssetNumberSequence_CompanyId_AssetNumberSequenceId;
    IF OBJECT_ID(N'dbo.FK_tblAssetNumberSequence_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAssetNumberSequence] DROP CONSTRAINT FK_tblAssetNumberSequence_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAssetNumberSequence_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAssetNumberSequence] DROP CONSTRAINT DF_tblAssetNumberSequence_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetNumberSequence_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetNumberSequence')) DROP INDEX IX_tblAssetNumberSequence_CompanyId ON dbo.[tblAssetNumberSequence];
    ALTER TABLE dbo.[tblAssetNumberSequence] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblAssetCategory', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAssetCategory_CompanyId_AssetCategoryId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAssetCategory] DROP CONSTRAINT UQ_tblAssetCategory_CompanyId_AssetCategoryId;
    IF OBJECT_ID(N'dbo.FK_tblAssetCategory_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAssetCategory] DROP CONSTRAINT FK_tblAssetCategory_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAssetCategory_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAssetCategory] DROP CONSTRAINT DF_tblAssetCategory_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetCategory_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetCategory')) DROP INDEX IX_tblAssetCategory_CompanyId ON dbo.[tblAssetCategory];
    ALTER TABLE dbo.[tblAssetCategory] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblAssetSubCategory', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAssetSubCategory_CompanyId_AssetSubCategoryId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAssetSubCategory] DROP CONSTRAINT UQ_tblAssetSubCategory_CompanyId_AssetSubCategoryId;
    IF OBJECT_ID(N'dbo.FK_tblAssetSubCategory_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAssetSubCategory] DROP CONSTRAINT FK_tblAssetSubCategory_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAssetSubCategory_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAssetSubCategory] DROP CONSTRAINT DF_tblAssetSubCategory_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetSubCategory_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetSubCategory')) DROP INDEX IX_tblAssetSubCategory_CompanyId ON dbo.[tblAssetSubCategory];
    ALTER TABLE dbo.[tblAssetSubCategory] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblManufacturer', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblManufacturer_CompanyId_ManufacturerId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblManufacturer] DROP CONSTRAINT UQ_tblManufacturer_CompanyId_ManufacturerId;
    IF OBJECT_ID(N'dbo.FK_tblManufacturer_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblManufacturer] DROP CONSTRAINT FK_tblManufacturer_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblManufacturer_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblManufacturer] DROP CONSTRAINT DF_tblManufacturer_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblManufacturer_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblManufacturer')) DROP INDEX IX_tblManufacturer_CompanyId ON dbo.[tblManufacturer];
    ALTER TABLE dbo.[tblManufacturer] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblVendor', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblVendor_CompanyId_VendorId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblVendor] DROP CONSTRAINT UQ_tblVendor_CompanyId_VendorId;
    IF OBJECT_ID(N'dbo.FK_tblVendor_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblVendor] DROP CONSTRAINT FK_tblVendor_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblVendor_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblVendor] DROP CONSTRAINT DF_tblVendor_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblVendor_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblVendor')) DROP INDEX IX_tblVendor_CompanyId ON dbo.[tblVendor];
    ALTER TABLE dbo.[tblVendor] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblLocation', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblLocation_CompanyId_LocationId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblLocation] DROP CONSTRAINT UQ_tblLocation_CompanyId_LocationId;
    IF OBJECT_ID(N'dbo.FK_tblLocation_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblLocation] DROP CONSTRAINT FK_tblLocation_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblLocation_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblLocation] DROP CONSTRAINT DF_tblLocation_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblLocation_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblLocation')) DROP INDEX IX_tblLocation_CompanyId ON dbo.[tblLocation];
    ALTER TABLE dbo.[tblLocation] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblBuilding', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblBuilding_CompanyId_BuildingId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblBuilding] DROP CONSTRAINT UQ_tblBuilding_CompanyId_BuildingId;
    IF OBJECT_ID(N'dbo.FK_tblBuilding_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblBuilding] DROP CONSTRAINT FK_tblBuilding_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblBuilding_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblBuilding] DROP CONSTRAINT DF_tblBuilding_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblBuilding_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblBuilding')) DROP INDEX IX_tblBuilding_CompanyId ON dbo.[tblBuilding];
    ALTER TABLE dbo.[tblBuilding] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblFloor', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblFloor_CompanyId_FloorId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblFloor] DROP CONSTRAINT UQ_tblFloor_CompanyId_FloorId;
    IF OBJECT_ID(N'dbo.FK_tblFloor_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblFloor] DROP CONSTRAINT FK_tblFloor_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblFloor_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblFloor] DROP CONSTRAINT DF_tblFloor_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblFloor_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblFloor')) DROP INDEX IX_tblFloor_CompanyId ON dbo.[tblFloor];
    ALTER TABLE dbo.[tblFloor] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblRoom', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblRoom_CompanyId_RoomId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblRoom] DROP CONSTRAINT UQ_tblRoom_CompanyId_RoomId;
    IF OBJECT_ID(N'dbo.FK_tblRoom_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblRoom] DROP CONSTRAINT FK_tblRoom_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblRoom_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblRoom] DROP CONSTRAINT DF_tblRoom_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblRoom_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblRoom')) DROP INDEX IX_tblRoom_CompanyId ON dbo.[tblRoom];
    ALTER TABLE dbo.[tblRoom] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblDepartment', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblDepartment_CompanyId_DepartmentId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblDepartment] DROP CONSTRAINT UQ_tblDepartment_CompanyId_DepartmentId;
    IF OBJECT_ID(N'dbo.FK_tblDepartment_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblDepartment] DROP CONSTRAINT FK_tblDepartment_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblDepartment_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblDepartment] DROP CONSTRAINT DF_tblDepartment_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblDepartment_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblDepartment')) DROP INDEX IX_tblDepartment_CompanyId ON dbo.[tblDepartment];
    ALTER TABLE dbo.[tblDepartment] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblEmployee', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblEmployee_CompanyId_EmployeeId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblEmployee] DROP CONSTRAINT UQ_tblEmployee_CompanyId_EmployeeId;
    IF OBJECT_ID(N'dbo.FK_tblEmployee_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblEmployee] DROP CONSTRAINT FK_tblEmployee_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblEmployee_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblEmployee] DROP CONSTRAINT DF_tblEmployee_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblEmployee_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblEmployee')) DROP INDEX IX_tblEmployee_CompanyId ON dbo.[tblEmployee];
    ALTER TABLE dbo.[tblEmployee] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblClassification', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblClassification_CompanyId_ClassificationId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblClassification] DROP CONSTRAINT UQ_tblClassification_CompanyId_ClassificationId;
    IF OBJECT_ID(N'dbo.FK_tblClassification_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblClassification] DROP CONSTRAINT FK_tblClassification_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblClassification_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblClassification] DROP CONSTRAINT DF_tblClassification_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblClassification_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblClassification')) DROP INDEX IX_tblClassification_CompanyId ON dbo.[tblClassification];
    ALTER TABLE dbo.[tblClassification] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblRiskLevel', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblRiskLevel_CompanyId_RiskLevelId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblRiskLevel] DROP CONSTRAINT UQ_tblRiskLevel_CompanyId_RiskLevelId;
    IF OBJECT_ID(N'dbo.FK_tblRiskLevel_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblRiskLevel] DROP CONSTRAINT FK_tblRiskLevel_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblRiskLevel_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblRiskLevel] DROP CONSTRAINT DF_tblRiskLevel_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblRiskLevel_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblRiskLevel')) DROP INDEX IX_tblRiskLevel_CompanyId ON dbo.[tblRiskLevel];
    ALTER TABLE dbo.[tblRiskLevel] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblCriticality', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblCriticality_CompanyId_CriticalityId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblCriticality] DROP CONSTRAINT UQ_tblCriticality_CompanyId_CriticalityId;
    IF OBJECT_ID(N'dbo.FK_tblCriticality_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblCriticality] DROP CONSTRAINT FK_tblCriticality_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblCriticality_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblCriticality] DROP CONSTRAINT DF_tblCriticality_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblCriticality_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblCriticality')) DROP INDEX IX_tblCriticality_CompanyId ON dbo.[tblCriticality];
    ALTER TABLE dbo.[tblCriticality] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblRole', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblRole_CompanyId_RoleId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblRole] DROP CONSTRAINT UQ_tblRole_CompanyId_RoleId;
    IF OBJECT_ID(N'dbo.FK_tblRole_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblRole] DROP CONSTRAINT FK_tblRole_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblRole_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblRole] DROP CONSTRAINT DF_tblRole_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblRole_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblRole')) DROP INDEX IX_tblRole_CompanyId ON dbo.[tblRole];
    ALTER TABLE dbo.[tblRole] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblRolePermission', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblRolePermission_CompanyId_RoleId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblRolePermission] DROP CONSTRAINT UQ_tblRolePermission_CompanyId_RoleId;
    IF OBJECT_ID(N'dbo.FK_tblRolePermission_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblRolePermission] DROP CONSTRAINT FK_tblRolePermission_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblRolePermission_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblRolePermission] DROP CONSTRAINT DF_tblRolePermission_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblRolePermission_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblRolePermission')) DROP INDEX IX_tblRolePermission_CompanyId ON dbo.[tblRolePermission];
    ALTER TABLE dbo.[tblRolePermission] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblUserRole', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblUserRole_CompanyId_UserId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblUserRole] DROP CONSTRAINT UQ_tblUserRole_CompanyId_UserId;
    IF OBJECT_ID(N'dbo.FK_tblUserRole_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblUserRole] DROP CONSTRAINT FK_tblUserRole_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblUserRole_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblUserRole] DROP CONSTRAINT DF_tblUserRole_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblUserRole_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblUserRole')) DROP INDEX IX_tblUserRole_CompanyId ON dbo.[tblUserRole];
    ALTER TABLE dbo.[tblUserRole] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblAsset', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAsset_CompanyId_AssetId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT UQ_tblAsset_CompanyId_AssetId;
    IF OBJECT_ID(N'dbo.FK_tblAsset_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAsset_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT DF_tblAsset_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAsset')) DROP INDEX IX_tblAsset_CompanyId ON dbo.[tblAsset];
    ALTER TABLE dbo.[tblAsset] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblAssetAssignment', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAssetAssignment_CompanyId_AssetAssignmentId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAssetAssignment] DROP CONSTRAINT UQ_tblAssetAssignment_CompanyId_AssetAssignmentId;
    IF OBJECT_ID(N'dbo.FK_tblAssetAssignment_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAssetAssignment] DROP CONSTRAINT FK_tblAssetAssignment_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAssetAssignment_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAssetAssignment] DROP CONSTRAINT DF_tblAssetAssignment_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAssignment_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetAssignment')) DROP INDEX IX_tblAssetAssignment_CompanyId ON dbo.[tblAssetAssignment];
    ALTER TABLE dbo.[tblAssetAssignment] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblAssetMovement', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAssetMovement_CompanyId_AssetMovementId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT UQ_tblAssetMovement_CompanyId_AssetMovementId;
    IF OBJECT_ID(N'dbo.FK_tblAssetMovement_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAssetMovement_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT DF_tblAssetMovement_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement')) DROP INDEX IX_tblAssetMovement_CompanyId ON dbo.[tblAssetMovement];
    ALTER TABLE dbo.[tblAssetMovement] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblAssetMaintenance', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAssetMaintenance_CompanyId_AssetMaintenanceId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAssetMaintenance] DROP CONSTRAINT UQ_tblAssetMaintenance_CompanyId_AssetMaintenanceId;
    IF OBJECT_ID(N'dbo.FK_tblAssetMaintenance_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAssetMaintenance] DROP CONSTRAINT FK_tblAssetMaintenance_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAssetMaintenance_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAssetMaintenance] DROP CONSTRAINT DF_tblAssetMaintenance_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMaintenance_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetMaintenance')) DROP INDEX IX_tblAssetMaintenance_CompanyId ON dbo.[tblAssetMaintenance];
    ALTER TABLE dbo.[tblAssetMaintenance] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblAssetAudit', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAssetAudit_CompanyId_AssetAuditId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAssetAudit] DROP CONSTRAINT UQ_tblAssetAudit_CompanyId_AssetAuditId;
    IF OBJECT_ID(N'dbo.FK_tblAssetAudit_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAssetAudit] DROP CONSTRAINT FK_tblAssetAudit_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAssetAudit_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAssetAudit] DROP CONSTRAINT DF_tblAssetAudit_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAudit_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetAudit')) DROP INDEX IX_tblAssetAudit_CompanyId ON dbo.[tblAssetAudit];
    ALTER TABLE dbo.[tblAssetAudit] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblAssetDisposal', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAssetDisposal_CompanyId_AssetDisposalId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAssetDisposal] DROP CONSTRAINT UQ_tblAssetDisposal_CompanyId_AssetDisposalId;
    IF OBJECT_ID(N'dbo.FK_tblAssetDisposal_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAssetDisposal] DROP CONSTRAINT FK_tblAssetDisposal_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAssetDisposal_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAssetDisposal] DROP CONSTRAINT DF_tblAssetDisposal_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetDisposal_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetDisposal')) DROP INDEX IX_tblAssetDisposal_CompanyId ON dbo.[tblAssetDisposal];
    ALTER TABLE dbo.[tblAssetDisposal] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblWarranty', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblWarranty_CompanyId_WarrantyId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblWarranty] DROP CONSTRAINT UQ_tblWarranty_CompanyId_WarrantyId;
    IF OBJECT_ID(N'dbo.FK_tblWarranty_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblWarranty] DROP CONSTRAINT FK_tblWarranty_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblWarranty_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblWarranty] DROP CONSTRAINT DF_tblWarranty_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblWarranty_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblWarranty')) DROP INDEX IX_tblWarranty_CompanyId ON dbo.[tblWarranty];
    ALTER TABLE dbo.[tblWarranty] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblSoftwareLicense', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblSoftwareLicense_CompanyId_SoftwareLicenseId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblSoftwareLicense] DROP CONSTRAINT UQ_tblSoftwareLicense_CompanyId_SoftwareLicenseId;
    IF OBJECT_ID(N'dbo.FK_tblSoftwareLicense_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblSoftwareLicense] DROP CONSTRAINT FK_tblSoftwareLicense_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblSoftwareLicense_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblSoftwareLicense] DROP CONSTRAINT DF_tblSoftwareLicense_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicense_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicense')) DROP INDEX IX_tblSoftwareLicense_CompanyId ON dbo.[tblSoftwareLicense];
    ALTER TABLE dbo.[tblSoftwareLicense] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblSoftwareLicenseAllocation', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblSoftwareLicenseAllocation_CompanyId_SoftwareLicenseAllocationId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblSoftwareLicenseAllocation] DROP CONSTRAINT UQ_tblSoftwareLicenseAllocation_CompanyId_SoftwareLicenseAllocationId;
    IF OBJECT_ID(N'dbo.FK_tblSoftwareLicenseAllocation_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblSoftwareLicenseAllocation] DROP CONSTRAINT FK_tblSoftwareLicenseAllocation_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblSoftwareLicenseAllocation_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblSoftwareLicenseAllocation] DROP CONSTRAINT DF_tblSoftwareLicenseAllocation_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicenseAllocation_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicenseAllocation')) DROP INDEX IX_tblSoftwareLicenseAllocation_CompanyId ON dbo.[tblSoftwareLicenseAllocation];
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblAssetQRCode', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAssetQRCode_CompanyId_AssetQRCodeId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAssetQRCode] DROP CONSTRAINT UQ_tblAssetQRCode_CompanyId_AssetQRCodeId;
    IF OBJECT_ID(N'dbo.FK_tblAssetQRCode_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAssetQRCode] DROP CONSTRAINT FK_tblAssetQRCode_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAssetQRCode_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAssetQRCode] DROP CONSTRAINT DF_tblAssetQRCode_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetQRCode_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetQRCode')) DROP INDEX IX_tblAssetQRCode_CompanyId ON dbo.[tblAssetQRCode];
    ALTER TABLE dbo.[tblAssetQRCode] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblQrLabelPrintLog', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblQrLabelPrintLog_CompanyId_QrLabelPrintLogId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblQrLabelPrintLog] DROP CONSTRAINT UQ_tblQrLabelPrintLog_CompanyId_QrLabelPrintLogId;
    IF OBJECT_ID(N'dbo.FK_tblQrLabelPrintLog_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblQrLabelPrintLog] DROP CONSTRAINT FK_tblQrLabelPrintLog_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblQrLabelPrintLog_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblQrLabelPrintLog] DROP CONSTRAINT DF_tblQrLabelPrintLog_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblQrLabelPrintLog_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblQrLabelPrintLog')) DROP INDEX IX_tblQrLabelPrintLog_CompanyId ON dbo.[tblQrLabelPrintLog];
    ALTER TABLE dbo.[tblQrLabelPrintLog] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblAssetDocument', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAssetDocument_CompanyId_AssetDocumentId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAssetDocument] DROP CONSTRAINT UQ_tblAssetDocument_CompanyId_AssetDocumentId;
    IF OBJECT_ID(N'dbo.FK_tblAssetDocument_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAssetDocument] DROP CONSTRAINT FK_tblAssetDocument_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAssetDocument_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAssetDocument] DROP CONSTRAINT DF_tblAssetDocument_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetDocument_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetDocument')) DROP INDEX IX_tblAssetDocument_CompanyId ON dbo.[tblAssetDocument];
    ALTER TABLE dbo.[tblAssetDocument] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblAssetHistory', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAssetHistory_CompanyId_AssetHistoryId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAssetHistory] DROP CONSTRAINT UQ_tblAssetHistory_CompanyId_AssetHistoryId;
    IF OBJECT_ID(N'dbo.FK_tblAssetHistory_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAssetHistory] DROP CONSTRAINT FK_tblAssetHistory_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAssetHistory_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAssetHistory] DROP CONSTRAINT DF_tblAssetHistory_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetHistory_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetHistory')) DROP INDEX IX_tblAssetHistory_CompanyId ON dbo.[tblAssetHistory];
    ALTER TABLE dbo.[tblAssetHistory] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblNotification', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblNotification_CompanyId_NotificationId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblNotification] DROP CONSTRAINT UQ_tblNotification_CompanyId_NotificationId;
    IF OBJECT_ID(N'dbo.FK_tblNotification_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblNotification] DROP CONSTRAINT FK_tblNotification_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblNotification_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblNotification] DROP CONSTRAINT DF_tblNotification_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblNotification_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblNotification')) DROP INDEX IX_tblNotification_CompanyId ON dbo.[tblNotification];
    ALTER TABLE dbo.[tblNotification] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblNotificationQueue', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblNotificationQueue_CompanyId_NotificationQueueId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblNotificationQueue] DROP CONSTRAINT UQ_tblNotificationQueue_CompanyId_NotificationQueueId;
    IF OBJECT_ID(N'dbo.FK_tblNotificationQueue_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblNotificationQueue] DROP CONSTRAINT FK_tblNotificationQueue_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblNotificationQueue_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblNotificationQueue] DROP CONSTRAINT DF_tblNotificationQueue_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblNotificationQueue_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblNotificationQueue')) DROP INDEX IX_tblNotificationQueue_CompanyId ON dbo.[tblNotificationQueue];
    ALTER TABLE dbo.[tblNotificationQueue] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblAuditLog', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblAuditLog_CompanyId_AuditLogId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblAuditLog] DROP CONSTRAINT UQ_tblAuditLog_CompanyId_AuditLogId;
    IF OBJECT_ID(N'dbo.FK_tblAuditLog_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblAuditLog] DROP CONSTRAINT FK_tblAuditLog_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblAuditLog_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblAuditLog] DROP CONSTRAINT DF_tblAuditLog_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAuditLog_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAuditLog')) DROP INDEX IX_tblAuditLog_CompanyId ON dbo.[tblAuditLog];
    ALTER TABLE dbo.[tblAuditLog] DROP COLUMN CompanyId;
END
GO
IF COL_LENGTH(N'dbo.tblLoginAudit', N'CompanyId') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.UQ_tblLoginAudit_CompanyId_LoginAuditId', N'UQ') IS NOT NULL ALTER TABLE dbo.[tblLoginAudit] DROP CONSTRAINT UQ_tblLoginAudit_CompanyId_LoginAuditId;
    IF OBJECT_ID(N'dbo.FK_tblLoginAudit_CompanyId', N'F') IS NOT NULL ALTER TABLE dbo.[tblLoginAudit] DROP CONSTRAINT FK_tblLoginAudit_CompanyId;
    IF OBJECT_ID(N'dbo.DF_tblLoginAudit_CompanyId', N'D') IS NOT NULL ALTER TABLE dbo.[tblLoginAudit] DROP CONSTRAINT DF_tblLoginAudit_CompanyId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblLoginAudit_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblLoginAudit')) DROP INDEX IX_tblLoginAudit_CompanyId ON dbo.[tblLoginAudit];
    ALTER TABLE dbo.[tblLoginAudit] DROP COLUMN CompanyId;
END
GO
DROP TABLE IF EXISTS dbo.tblCompanyUser;
GO
IF OBJECT_ID(N'dbo.tblCompany', N'U') IS NOT NULL DROP TABLE dbo.tblCompany;
GO
IF COL_LENGTH(N'dbo.tblUser', N'IsPlatformAdmin') IS NOT NULL
BEGIN
    ALTER TABLE dbo.tblUser DROP CONSTRAINT DF_tblUser_IsPlatformAdmin;
    ALTER TABLE dbo.tblUser DROP COLUMN IsPlatformAdmin;
END
GO
IF OBJECT_ID(N'dbo.tblMigrationLog', N'U') IS NOT NULL
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual, Passed) VALUES (N'MC-1', N'rolled back', N'yes', N'yes', 1);
GO
PRINT 'Rollback complete. Re-run scripts 03-06 from v1.1 and deploy the v1.1 web application.';
GO
