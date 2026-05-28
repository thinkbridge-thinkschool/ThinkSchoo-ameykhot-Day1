-- ============================================================
-- 03_session1_deadlock.sql  -  SESSION 1  (run in window 1)
-- Classic two-resource deadlock:
--   Session 1 locks AccountA first, then tries AccountB
--   Session 2 locks AccountB first, then tries AccountA
-- ============================================================

USE DeadlockDemo;
GO

BEGIN TRANSACTION;

    -- Step 1: acquire X-lock on AccountA row
    UPDATE AccountA SET Balance = Balance - 100 WHERE Id = 1;

    -- Step 2: pause here so Session 2 can grab AccountB
    WAITFOR DELAY '00:00:05';

    -- Step 3: try to acquire X-lock on AccountB  <-- will DEADLOCK
    UPDATE AccountB SET Balance = Balance + 100 WHERE Id = 1;

COMMIT TRANSACTION;

PRINT 'Session 1 committed successfully (not the victim).';
GO
