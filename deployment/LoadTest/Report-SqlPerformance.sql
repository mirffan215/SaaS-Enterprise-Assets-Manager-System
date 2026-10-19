/* =====================================================================
   AMGH IT Inventory - Release 2.6: SQL Server performance report for the load-test copy.
   Variables: DatabaseName, SinceUtc (start of the load test, yyyy-MM-ddTHH:mm:ss).
   Read-only: uses the server's own statistics (no plan cache is cleared, so production is not affected).
   ===================================================================== */
USE [$(DatabaseName)];
SET NOCOUNT ON;
DECLARE @since DATETIME2 = CAST(N'$(SinceUtc)' AS DATETIME2);

PRINT '=== 1. Slowest statements during the test (by average duration) ===';
SELECT TOP (20)
    CAST(qs.total_elapsed_time / qs.execution_count / 1000.0 AS DECIMAL(12, 1)) AS avg_ms,
    CAST(qs.max_elapsed_time / 1000.0 AS DECIMAL(12, 1))                         AS max_ms,
    qs.execution_count                                                            AS runs,
    qs.total_logical_reads / qs.execution_count                                   AS avg_reads,
    LEFT(REPLACE(REPLACE(SUBSTRING(st.text, qs.statement_start_offset / 2 + 1,
        (CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END - qs.statement_start_offset) / 2 + 1),
        CHAR(13), ' '), CHAR(10), ' '), 300)                                     AS statement
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
WHERE st.dbid = DB_ID() AND qs.last_execution_time >= @since
ORDER BY qs.total_elapsed_time / qs.execution_count DESC;

PRINT '=== 2. Statements with the most total work (duration x runs) ===';
SELECT TOP (10)
    CAST(qs.total_elapsed_time / 1000.0 AS DECIMAL(14, 0)) AS total_ms, qs.execution_count AS runs,
    CAST(qs.total_elapsed_time / qs.execution_count / 1000.0 AS DECIMAL(12, 1)) AS avg_ms,
    LEFT(REPLACE(REPLACE(SUBSTRING(st.text, qs.statement_start_offset / 2 + 1,
        (CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END - qs.statement_start_offset) / 2 + 1),
        CHAR(13), ' '), CHAR(10), ' '), 300) AS statement
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
WHERE st.dbid = DB_ID() AND qs.last_execution_time >= @since
ORDER BY qs.total_elapsed_time DESC;

PRINT '=== 3. Indexes SQL Server reports as missing (review before creating: send this section to the developer) ===';
SELECT TOP (15)
    CAST(s.avg_total_user_cost * s.avg_user_impact * (s.user_seeks + s.user_scans) AS DECIMAL(14, 0)) AS benefit,
    s.user_seeks + s.user_scans AS uses, CAST(s.avg_user_impact AS DECIMAL(5, 1)) AS impact_pct,
    OBJECT_NAME(d.object_id) AS [table], d.equality_columns, d.inequality_columns, d.included_columns
FROM sys.dm_db_missing_index_details d
JOIN sys.dm_db_missing_index_groups g ON g.index_handle = d.index_handle
JOIN sys.dm_db_missing_index_group_stats s ON s.group_handle = g.index_group_handle
WHERE d.database_id = DB_ID()
ORDER BY benefit DESC;

PRINT '=== 4. Data volume ===';
SELECT t.name AS [table], SUM(p.rows) AS [rows]
FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.name IN (N'tblAsset', N'tblAssetAssignment', N'tblWarranty', N'tblAssetMaintenance', N'tblAssetMovement', N'tblAssetAudit',
                 N'tblAuditLog', N'tblAssetHistory', N'tblNotification', N'tblEmployee')
GROUP BY t.name ORDER BY SUM(p.rows) DESC;
