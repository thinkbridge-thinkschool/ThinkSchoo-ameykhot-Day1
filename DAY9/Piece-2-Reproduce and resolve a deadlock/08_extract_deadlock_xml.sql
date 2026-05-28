-- ============================================================
-- 08_extract_deadlock_xml.sql
-- Extracts ONLY the inner <deadlock> XML that SSMS can render
-- as a visual graph when saved as .xdl
-- ============================================================

USE DeadlockDemo;
GO

SELECT
    xdr.query('(data[@name="xml_report"]/value/deadlock)[1]') AS deadlock_only_xml
FROM (
    SELECT CAST(target_data AS XML) AS ring_data
    FROM   sys.dm_xe_sessions        AS s
    JOIN   sys.dm_xe_session_targets AS t
           ON s.address = t.event_session_address
    WHERE  s.name        = 'CaptureDeadlocks'
    AND    t.target_name = 'ring_buffer'
) AS rb
CROSS APPLY ring_data.nodes('//RingBufferTarget/event[@name="xml_deadlock_report"]') AS x(xdr);
GO
