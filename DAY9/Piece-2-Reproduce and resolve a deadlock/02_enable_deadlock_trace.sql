-- ============================================================
-- 02_enable_deadlock_trace.sql
-- Enable trace flag 1222 (detailed deadlock info in error log)
-- Run as sysadmin BEFORE the repro
-- ============================================================

-- Trace flag 1222 writes the full deadlock graph (XML-style) to ERRORLOG
DBCC TRACEON(1222, -1);   -- -1 = server-wide, all sessions
GO

-- Verify it is active
DBCC TRACESTATUS(1222);
GO

-- ----------------------------------------------------------------
-- OPTIONAL: Extended Events session (richer, captures XML graph)
-- ----------------------------------------------------------------
IF EXISTS (
    SELECT 1 FROM sys.server_event_sessions WHERE name = 'CaptureDeadlocks'
)
    DROP EVENT SESSION CaptureDeadlocks ON SERVER;
GO

CREATE EVENT SESSION CaptureDeadlocks ON SERVER
ADD EVENT sqlserver.xml_deadlock_report       -- full XML deadlock graph
ADD TARGET package0.ring_buffer(
    SET max_memory = 4096             -- KB kept in memory
)
WITH (
    MAX_DISPATCH_LATENCY = 5 SECONDS
);
GO

ALTER EVENT SESSION CaptureDeadlocks ON SERVER STATE = START;
GO

PRINT 'Trace flag 1222 ON.  Extended Events session CaptureDeadlocks started.';
GO
