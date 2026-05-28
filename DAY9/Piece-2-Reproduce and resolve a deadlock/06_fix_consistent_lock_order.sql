-- ============================================================
-- 06_fix_consistent_lock_order.sql
-- Both sessions acquire locks in the same order (A -> B).
-- Circular wait can never form -> no deadlock.
-- ============================================================

USE DeadlockDemo;
GO

-- Reset to clean starting balances first
UPDATE AccountA SET Balance = 1000.00 WHERE Id = 1;
UPDATE AccountB SET Balance = 2000.00 WHERE Id = 1;
GO

-- -----------------------------------------------
-- Fixed Session 1  (unchanged: A -> B)
-- -----------------------------------------------
BEGIN TRANSACTION;
    UPDATE AccountA SET Balance = Balance - 100 WHERE Id = 1;  -- lock A
    WAITFOR DELAY '00:00:05';
    UPDATE AccountB SET Balance = Balance + 100 WHERE Id = 1;  -- lock B
COMMIT TRANSACTION;
GO

-- -----------------------------------------------
-- Fixed Session 2  (REORDERED: now A->B, not B->A)
-- -----------------------------------------------
BEGIN TRANSACTION;
    -- Acquire AccountA FIRST (same order as Session 1)
    UPDATE AccountA SET Balance = Balance + 200 WHERE Id = 1;  -- lock A
    WAITFOR DELAY '00:00:05';
    UPDATE AccountB SET Balance = Balance - 200 WHERE Id = 1;  -- lock B
COMMIT TRANSACTION;
GO

-- -----------------------------------------------
-- Verify final balances
-- -----------------------------------------------
SELECT 'AccountA' AS [Table], Id, Balance FROM AccountA
UNION ALL
SELECT 'AccountB',             Id, Balance FROM AccountB;
GO
