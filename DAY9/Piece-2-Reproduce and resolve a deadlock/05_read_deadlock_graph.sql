-- ============================================================
-- 05_read_deadlock_graph.sql
-- Read the captured deadlock graph AFTER the repro has fired
-- ============================================================

USE DeadlockDemo;
GO

-- ----------------------------------------------------------------
-- Option A: Read from Extended Events ring_buffer
-- ----------------------------------------------------------------
SELECT
    xdr.value('@timestamp',          'datetime2')  AS deadlock_time,
    xdr.value('(victim-list/victimProcess/@id)[1]','varchar(50)') AS victim_spid,
    xdr.query('.')                                  AS deadlock_graph_xml
FROM (
    SELECT CAST(target_data AS XML) AS ring_data
    FROM   sys.dm_xe_sessions        AS s
    JOIN   sys.dm_xe_session_targets AS t
           ON s.address = t.event_session_address
    WHERE  s.name       = 'CaptureDeadlocks'
    AND    t.target_name = 'ring_buffer'
) AS rb
CROSS APPLY ring_data.nodes('//RingBufferTarget/event[@name="xml_deadlock_report"]') AS x(xdr);
GO

-- ----------------------------------------------------------------
-- Option B: Check SQL Server ERRORLOG for trace-flag 1222 output
-- ----------------------------------------------------------------
EXEC xp_readerrorlog 0, 1, N'deadlock';
GO
