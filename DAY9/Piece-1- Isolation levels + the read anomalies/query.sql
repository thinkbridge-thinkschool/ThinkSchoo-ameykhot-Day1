-- ============================================================
-- Day 9 | Piece 1 — Isolation Levels + The Read Anomalies
-- ============================================================
-- Run each anomaly with two simultaneous sqlcmd sessions.
-- Connection string (both sessions):
--   sqlcmd -S thinkschool-sql-amey.database.windows.net
--          -d thinkschool-quotesdb-amey
--          -U sqladmin-amey -P ThinkSchool@123
-- ============================================================


-- ===========================================================
-- ANOMALY 1 — DIRTY READ
-- Problem: Session 2 reads data that Session 1 has changed
--          but NOT yet committed.
-- ===========================================================

-- SESSION 1 (run first)
BEGIN TRANSACTION;

UPDATE Quotes
SET Text = 'DIRTY UNCOMMITTED VALUE'
WHERE Id = 1;

PRINT 'S1: Updated (not committed) — dirty value written';

WAITFOR DELAY '00:00:10';   -- keep transaction open for S2 to read

ROLLBACK;
PRINT 'S1: Rolled back — dirty value is gone';
GO

-- SESSION 2 (run immediately after Session 1 starts)
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;  -- allows dirty reads

SELECT Id, Text
FROM Quotes
WHERE Id = 1;
-- Expected: returns 'DIRTY UNCOMMITTED VALUE' — data never committed
GO


-- ===========================================================
-- ANOMALY 2 — NON-REPEATABLE READ
-- Problem: Session 2 reads the same row twice and gets
--          different values because Session 1 changed it
--          in between the two reads.
-- ===========================================================

-- SESSION 2 (run first)
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;

PRINT 'S2 — First read:';
SELECT Id, Text FROM Quotes WHERE Id = 1;  -- original value

WAITFOR DELAY '00:00:05';                   -- window for S1 to update

PRINT 'S2 — Second read (same query):';
SELECT Id, Text FROM Quotes WHERE Id = 1;  -- different value!

COMMIT;
GO

-- SESSION 1 (run while Session 2 is in WAITFOR)
UPDATE Quotes
SET Text = 'UPDATED BY SESSION 1'
WHERE Id = 1;
COMMIT;
GO


-- ===========================================================
-- ANOMALY 3 — PHANTOM READ
-- Problem: Session 2 runs the same range query twice and
--          gets a different row count because Session 1
--          inserted a new row in between.
-- ===========================================================

-- SESSION 2 (run first)
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;

PRINT 'S2 — First count:';
SELECT COUNT(*) AS QuoteCount
FROM Quotes
WHERE AuthorId = 1;             -- e.g. returns 3

WAITFOR DELAY '00:00:05';       -- window for S1 to insert

PRINT 'S2 — Second count (same query):';
SELECT COUNT(*) AS QuoteCount
FROM Quotes
WHERE AuthorId = 1;             -- returns 4 — phantom row appeared!

COMMIT;
GO

-- SESSION 1 (run while Session 2 is in WAITFOR)
INSERT INTO Quotes (AuthorId, Text)
VALUES (1, 'PHANTOM NEW QUOTE');
COMMIT;
GO


-- ===========================================================
-- PREVENTION SCRIPTS
-- ===========================================================

-- Prevent Dirty Read — use READ COMMITTED (SQL Server default)
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
SELECT Id, Text FROM Quotes WHERE Id = 1;
-- S2 will BLOCK until S1's transaction ends; never sees uncommitted data.
GO

-- Prevent Non-Repeatable Read — use REPEATABLE READ
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT Id, Text FROM Quotes WHERE Id = 1;   -- places shared lock
-- S1's UPDATE is BLOCKED until this transaction commits.
WAITFOR DELAY '00:00:05';
SELECT Id, Text FROM Quotes WHERE Id = 1;   -- same value as first read
COMMIT;
GO

-- Prevent Phantom Read — use SERIALIZABLE
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
SELECT COUNT(*) AS QuoteCount FROM Quotes WHERE AuthorId = 1;
-- S1's INSERT is BLOCKED (key-range lock held on AuthorId = 1).
WAITFOR DELAY '00:00:05';
SELECT COUNT(*) AS QuoteCount FROM Quotes WHERE AuthorId = 1;   -- same count
COMMIT;
GO
