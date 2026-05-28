-- ============================================================
-- Day 9 | Piece 1 — Isolation Levels + The Read Anomalies
-- Database : IsolationDemo
-- Server   : thinkschool-sql-amey.database.windows.net
-- ============================================================


-- ===========================================================
-- SETUP — run once before all demos
-- ===========================================================

CREATE TABLE Accounts (
    Id      INT PRIMARY KEY,
    Name    NVARCHAR(50)   NOT NULL,
    Balance DECIMAL(10,2) NOT NULL
);
GO

INSERT INTO Accounts VALUES
(1,'Alice',500.00),(2,'Bob',750.00),(3,'Carol',1200.00),(4,'Dave',900.00),
(5,'Eve',1100.00),(6,'Frank',600.00),(7,'Grace',1300.00),(8,'Hannah',2500.00),
(9,'Ian',800.00),(10,'Jane',1400.00),(11,'Kevin',950.00),(12,'Laura',3000.00),
(13,'Mike',700.00),(14,'Nancy',1800.00),(15,'Oscar',1100.00),(16,'Pamela',2200.00),
(17,'Quinn',850.00),(18,'Rachel',1200.00),(19,'Steve',500.00),(20,'Tracy',4000.00);
GO


-- ===========================================================
-- ANOMALY 1 — DIRTY READ
-- Session B reads Balance = 99999.00 from Session A's open
-- (uncommitted) transaction. After rollback it never existed.
-- ===========================================================

-- SESSION A (run first)
USE IsolationDemo;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

BEGIN TRANSACTION;
    UPDATE Accounts SET Balance = 99999.00 WHERE Id = 20;
    WAITFOR DELAY '00:00:30';
ROLLBACK;
GO

-- SESSION B (run while Session A is executing)
USE IsolationDemo;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT Id, Name, Balance FROM Accounts WHERE Id = 20;
-- Returns 99999.00 (dirty - never committed)
GO


-- ===========================================================
-- ANOMALY 2 — NON-REPEATABLE READ
-- Session A reads Id=12 twice. Session B updates Balance
-- between the two reads. Session A gets two different values.
-- ===========================================================

-- SESSION A (run first)
USE IsolationDemo;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

BEGIN TRANSACTION;
    SELECT Id, Name, Balance FROM Accounts WHERE Id = 12;
    -- First result: 3000.00

    WAITFOR DELAY '00:00:30';

    SELECT Id, Name, Balance FROM Accounts WHERE Id = 12;
    -- Second result: 9999.00 (changed!)
COMMIT;
GO

-- SESSION B (run while Session A is executing)
USE IsolationDemo;

BEGIN TRANSACTION;
    UPDATE Accounts SET Balance = 9999.00 WHERE Id = 12;
COMMIT;
GO


-- ===========================================================
-- ANOMALY 3 — PHANTOM READ
-- Session A queries Balance > 1500 twice. Session B inserts
-- Uma between the two reads. Session A sees 5 then 6 rows.
-- ===========================================================

-- SESSION A (run first)
USE IsolationDemo;
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

BEGIN TRANSACTION;
    SELECT * FROM Accounts WHERE Balance > 1500.00;
    -- First result: 5 rows (Hannah, Laura, Nancy, Pamela, Tracy)

    WAITFOR DELAY '00:00:30';

    SELECT * FROM Accounts WHERE Balance > 1500.00;
    -- Second result: 6 rows (Uma appears as phantom!)
COMMIT;
GO

-- SESSION B (run while Session A is executing)
USE IsolationDemo;

BEGIN TRANSACTION;
    INSERT INTO Accounts VALUES (21, 'Uma', 5000.00);
COMMIT;
GO


-- ===========================================================
-- PREVENTION SCRIPTS
-- ===========================================================

-- Prevent Dirty Read → READ COMMITTED
-- Session B blocks until Session A commits or rolls back
USE IsolationDemo;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
SELECT Id, Name, Balance FROM Accounts WHERE Id = 20;
GO

-- Prevent Non-Repeatable Read → REPEATABLE READ
-- Shared lock held on Id=12 until Session A commits
USE IsolationDemo;
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
    SELECT Id, Name, Balance FROM Accounts WHERE Id = 12;
    WAITFOR DELAY '00:00:10';
    SELECT Id, Name, Balance FROM Accounts WHERE Id = 12;  -- same value
COMMIT;
GO

-- Prevent Phantom Read → SERIALIZABLE
-- Key-range lock on Balance > 1500 — no INSERT can match until commit
USE IsolationDemo;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
    SELECT * FROM Accounts WHERE Balance > 1500.00;
    WAITFOR DELAY '00:00:10';
    SELECT * FROM Accounts WHERE Balance > 1500.00;  -- same rows
COMMIT;
GO
