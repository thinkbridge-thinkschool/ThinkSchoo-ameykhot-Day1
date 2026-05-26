-- =============================================================
-- Day 8 — Piece 2: Covering Indexes + Included Columns
-- Database: ThinkSchoolCoveringIdx (localhost\SQLEXPRESS)
-- =============================================================

-- ---------------------------------------------------------------
-- SETUP: Create database, table, and seed data
-- ---------------------------------------------------------------
IF DB_ID('ThinkSchoolCoveringIdx') IS NOT NULL
    DROP DATABASE ThinkSchoolCoveringIdx;
GO
CREATE DATABASE ThinkSchoolCoveringIdx;
GO
USE ThinkSchoolCoveringIdx;
GO

CREATE TABLE Quotes (
    QuoteId   INT IDENTITY(1,1) PRIMARY KEY CLUSTERED,
    AuthorId  INT NOT NULL,
    Text      NVARCHAR(500) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

INSERT INTO Quotes (AuthorId, Text, CreatedAt) VALUES
(1, N'The only way to do great work is to love what you do.',          '2024-01-01'),
(2, N'In the middle of every difficulty lies opportunity.',             '2024-01-02'),
(1, N'Life is what happens when you are busy making other plans.',     '2024-01-03'),
(3, N'The future belongs to those who believe in their dreams.',       '2024-01-04'),
(1, N'Spread love everywhere you go.',                                 '2024-01-05'),
(2, N'When you reach the end of your rope, tie a knot and hang on.',  '2024-01-06'),
(1, N'Always remember that you are absolutely unique.',                '2024-01-07'),
(4, N'Do not go where the path may lead; go where there is no path.', '2024-01-08'),
(1, N'You will face many defeats, but never let yourself be defeated.','2024-01-09'),
(3, N'The greatest glory in living lies not in never falling.',        '2024-01-10'),
(2, N'In the end, it is not the years in your life that count.',       '2024-01-11'),
(1, N'Never let the fear of striking out keep you from playing.',      '2024-01-12'),
(5, N'Money and success do not change people.',                        '2024-01-13'),
(1, N'Your time is limited, so do not waste it living someone else.',  '2024-01-14'),
(4, N'Not all those who wander are lost.',                             '2024-01-15'),
(1, N'You miss 100 percent of the shots you do not take.',             '2024-01-16'),
(2, N'Whether you think you can or you cannot, you are right.',        '2024-01-17'),
(1, N'I have not failed. I found 10000 ways that will not work.',      '2024-01-18'),
(3, N'A person who never made a mistake never tried anything new.',    '2024-01-19'),
(1, N'The secret of getting ahead is getting started.',                '2024-01-20');
GO

-- Insert 5000 extra rows so the optimizer chooses index seek + key lookup
DECLARE @i INT = 1;
WHILE @i <= 5000
BEGIN
    INSERT INTO Quotes (AuthorId, Text, CreatedAt)
    VALUES (
        (@i % 100) + 2,
        CONCAT(N'Filler quote number ', @i),
        DATEADD(day, @i, '2024-01-01')
    );
    SET @i = @i + 1;
END;
GO

-- ---------------------------------------------------------------
-- STEP 1: BEFORE — Narrow non-clustered index (no INCLUDE)
--         Causes key lookup for Text and CreatedAt columns
-- ---------------------------------------------------------------
CREATE INDEX IX_Quotes_AuthorId_Narrow
ON Quotes (AuthorId);
GO

-- Update statistics so optimizer has fresh cardinality data
UPDATE STATISTICS Quotes;
GO

-- Run the query and observe STATISTICS IO output
SET STATISTICS IO ON;
GO

SELECT AuthorId, Text, CreatedAt
FROM Quotes
WHERE AuthorId = 1;
GO

SET STATISTICS IO OFF;
GO

-- Show BEFORE execution plan — expect Nested Loops + Index Seek + Key Lookup
SET SHOWPLAN_TEXT ON;
GO
SELECT AuthorId, Text, CreatedAt
FROM Quotes
WHERE AuthorId = 1;
GO
SET SHOWPLAN_TEXT OFF;
GO

-- BEFORE logical reads: 22
-- BEFORE plan shows:
--   Nested Loops(Inner Join)
--     |--Index Seek(IX_Quotes_AuthorId_Narrow, AuthorId=1)
--     |--Clustered Index Seek(...LOOKUP...)   <-- KEY LOOKUP


-- ---------------------------------------------------------------
-- STEP 2: Add covering index — replace narrow index with INCLUDE
-- ---------------------------------------------------------------
DROP INDEX IX_Quotes_AuthorId_Narrow ON Quotes;
GO

CREATE INDEX IX_Quotes_AuthorId_Covering
ON Quotes (AuthorId)
INCLUDE (Text, CreatedAt);
GO


-- ---------------------------------------------------------------
-- STEP 3: AFTER — Covering index serves query entirely from index
--         No key lookup needed
-- ---------------------------------------------------------------
SET STATISTICS IO ON;
GO

SELECT AuthorId, Text, CreatedAt
FROM Quotes
WHERE AuthorId = 1;
GO

SET STATISTICS IO OFF;
GO

-- Show AFTER execution plan — expect single Index Seek, no key lookup
SET SHOWPLAN_TEXT ON;
GO
SELECT AuthorId, Text, CreatedAt
FROM Quotes
WHERE AuthorId = 1;
GO
SET SHOWPLAN_TEXT OFF;
GO

-- AFTER logical reads: 2
-- AFTER plan shows:
--   Index Seek(IX_Quotes_AuthorId_Covering, AuthorId=1)   <-- covering, no lookup


-- ---------------------------------------------------------------
-- RESULT SUMMARY
-- ---------------------------------------------------------------
-- Metric              BEFORE (narrow)    AFTER (covering)   Delta
-- -----------------   ----------------   ----------------   -----
-- Logical Reads       22                 2                  -20 (91% less)
-- Plan operator       Key Lookup         Index Seek only    lookup eliminated
-- I/O round-trips     2 per matching row 0 extra            eliminated
