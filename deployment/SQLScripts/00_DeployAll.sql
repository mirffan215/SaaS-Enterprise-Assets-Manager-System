/* =====================================================================
   AMGH Inventory Management System v1.0
   Manual deployment from SSMS (Query > SQLCMD Mode) or sqlcmd.
   Preferred method: deployment\PowerShell\Deploy-Database.ps1, which
   generates the admin password hash and keeps secrets off disk.

   If running manually:
     1. Set ScriptPath to the folder containing these scripts.
     2. Set AdminPasswordHash to a hash produced by Deploy-Database.ps1
        (deployment\PowerShell\New-PasswordHash.ps1) - NEVER a plain password.
     3. Set MasterKeyPassword, then delete it from this file after running.
   ===================================================================== */
:on error exit
:setvar ScriptPath        "C:\AMGH\deployment\SQLScripts"
:setvar DatabaseName      "AMGH_ITInventory"
:setvar MasterKeyPassword "<set-strong-password>"
:setvar AppLogin          "IIS APPPOOL\AMGHITInventory"
:setvar AdminUsername     "admin"
:setvar AdminEmail        "it.admin@amgh.local"
:setvar AdminDisplayName  "System Administrator"
:setvar AdminPasswordHash "<paste-v3-hash>"

:r $(ScriptPath)\01_CreateDatabase.sql
:r $(ScriptPath)\02_Tables.sql
:r $(ScriptPath)\03_Functions.sql
:r $(ScriptPath)\04_Views.sql
:r $(ScriptPath)\05_StoredProcedures.sql
:r $(ScriptPath)\06_Triggers.sql
:r $(ScriptPath)\07_SeedData.sql
:r $(ScriptPath)\08_DatabaseSecurity.sql
