-- ============================================================
-- 07_cleanup.sql
-- Stop Extended Events, disable trace flag, drop database
-- ============================================================

-- Stop and drop Extended Events session
IF EXISTS (
    SELECT 1 FROM sys.server_event_sessions WHERE name = 'CaptureDeadlocks'
)
BEGIN
    ALTER EVENT SESSION CaptureDeadlocks ON SERVER STATE = STOP;
    DROP EVENT SESSION CaptureDeadlocks ON SERVER;
    PRINT 'CaptureDeadlocks XE session dropped.';
END
GO

-- Disable trace flag 1222
DBCC TRACEOFF(1222, -1);
DBCC TRACESTATUS(1222);
GO

-- Drop the database
USE master;
GO

IF DB_ID('DeadlockDemo') IS NOT NULL
BEGIN
    ALTER DATABASE DeadlockDemo SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE DeadlockDemo;
    PRINT 'DeadlockDemo database dropped.';
END
GO
