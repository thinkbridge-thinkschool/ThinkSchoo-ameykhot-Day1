-- ============================================================
-- 04_session2_deadlock.sql  -  SESSION 2  (run in window 2)
-- Run this IMMEDIATELY after starting Session 1 (within 5 s).
-- ============================================================

USE DeadlockDemo;
GO

BEGIN TRANSACTION;

    -- Step 1: acquire X-lock on AccountB row  (opposite order to Session 1)
    UPDATE AccountB SET Balance = Balance - 200 WHERE Id = 1;

    -- Step 2: pause mirrors Session 1
    WAITFOR DELAY '00:00:05';

    -- Step 3: try to acquire X-lock on AccountA  <-- circular wait = DEADLOCK
    UPDATE AccountA SET Balance = Balance + 200 WHERE Id = 1;

COMMIT TRANSACTION;

PRINT 'Session 2 committed successfully (not the victim).';
GO
