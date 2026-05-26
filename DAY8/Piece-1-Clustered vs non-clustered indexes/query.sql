-- ============================================================
-- Day 8 Piece 1 — Clustered vs Non-Clustered Indexes
-- Table: Sales (~100k rows)
-- ============================================================

-- ============================================================
-- STEP 1: Clean up if re-running
-- ============================================================
DROP TABLE IF EXISTS Sales;
GO

-- ============================================================
-- STEP 2: Create table as a HEAP (no clustered index yet)
-- ============================================================
CREATE TABLE Sales (
    SaleId      INT            NOT NULL,
    CustomerId  INT            NOT NULL,
    ProductId   INT            NOT NULL,
    SaleDate    DATETIME       NOT NULL,
    Amount      DECIMAL(10,2)  NOT NULL,
    Region      NVARCHAR(20)   NOT NULL
);
GO

-- ============================================================
-- STEP 3: Generate ~100,000 rows
-- ============================================================
INSERT INTO Sales (SaleId, CustomerId, ProductId, SaleDate, Amount, Region)
SELECT TOP 100000
    ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
    ABS(CHECKSUM(NEWID())) % 10000 + 1,
    ABS(CHECKSUM(NEWID())) % 500  + 1,
    DATEADD(DAY, ABS(CHECKSUM(NEWID())) % 1096, '2022-01-01'),
    CAST((ABS(CHECKSUM(NEWID())) % 99900 + 100) / 100.0 AS DECIMAL(10,2)),
    CHOOSE(ABS(CHECKSUM(NEWID())) % 5 + 1,
           'North','South','East','West','Central')
FROM sys.all_objects a CROSS JOIN sys.all_objects b;
GO

SELECT COUNT(*) AS RowCount FROM Sales;
GO
-- Expected: 100000


-- ============================================================
-- ============================================================
-- PART A — CLUSTERED INDEX
-- ============================================================
-- ============================================================

-- ============================================================
-- A1. BEFORE clustered index (HEAP): range query on SaleId
-- ============================================================
SET STATISTICS IO ON;

SELECT SaleId, Amount
FROM   Sales
WHERE  SaleId BETWEEN 50000 AND 50100;

SET STATISTICS IO OFF;
GO
-- Expected: ~971 logical reads  (full heap scan — no ordering, must read every page)


-- ============================================================
-- A2. Create CLUSTERED INDEX on SaleId
-- ============================================================
CREATE CLUSTERED INDEX CIX_Sales_SaleId
ON Sales (SaleId);
GO


-- ============================================================
-- A3. AFTER clustered index: same range query
-- ============================================================
SET STATISTICS IO ON;

SELECT SaleId, Amount
FROM   Sales
WHERE  SaleId BETWEEN 50000 AND 50100;

SET STATISTICS IO OFF;
GO
-- Expected: ~4 logical reads  (B-tree seek: root→intermediate→leaf, then 1–2 leaf pages)


-- ============================================================
-- ============================================================
-- PART B — NON-CLUSTERED INDEX 1: CustomerId
-- ============================================================
-- ============================================================

-- ============================================================
-- B1. BEFORE NC index on CustomerId: customer filter query
-- ============================================================
SET STATISTICS IO ON;

SELECT SaleId, CustomerId, Amount, Region
FROM   Sales
WHERE  CustomerId = 5000;

SET STATISTICS IO OFF;
GO
-- Expected: ~971 logical reads  (clustered index scan — no NC index, scans all pages)


-- ============================================================
-- B2. Create NON-CLUSTERED INDEX on CustomerId
-- ============================================================
CREATE NONCLUSTERED INDEX IX_Sales_CustomerId
ON Sales (CustomerId);
GO


-- ============================================================
-- B3. AFTER NC index on CustomerId: same query
-- ============================================================
SET STATISTICS IO ON;

SELECT SaleId, CustomerId, Amount, Region
FROM   Sales
WHERE  CustomerId = 5000;

SET STATISTICS IO OFF;
GO
-- Expected: ~33 logical reads
--   NC index seek (3 B-tree levels) + 1 leaf page
--   + ~10 key lookups (Amount and Region not in index, go back to clustered)


-- ============================================================
-- ============================================================
-- PART C — NON-CLUSTERED INDEX 2: SaleDate
-- ============================================================
-- ============================================================

-- ============================================================
-- C1. BEFORE NC index on SaleDate: date range query
-- ============================================================
SET STATISTICS IO ON;

SELECT SaleId, SaleDate
FROM   Sales
WHERE  SaleDate BETWEEN '2024-01-01' AND '2024-01-07';

SET STATISTICS IO OFF;
GO
-- Expected: ~971 logical reads  (clustered scan — no date index)


-- ============================================================
-- C2. Create NON-CLUSTERED INDEX on SaleDate
-- ============================================================
CREATE NONCLUSTERED INDEX IX_Sales_SaleDate
ON Sales (SaleDate);
GO


-- ============================================================
-- C3. AFTER NC index on SaleDate: same date range query
-- ============================================================
SET STATISTICS IO ON;

SELECT SaleId, SaleDate
FROM   Sales
WHERE  SaleDate BETWEEN '2024-01-01' AND '2024-01-07';

SET STATISTICS IO OFF;
GO
-- Expected: ~10 logical reads
--   SaleId (clustered key) is stored in every NC index leaf automatically
--   SaleDate is the index key — so BOTH columns are in the NC leaf → no key lookup needed
--   3 B-tree levels + 7 leaf pages for the week's range


-- ============================================================
-- ============================================================
-- PART D — WRITE-SIDE COST (with 2 NC indexes in place)
-- ============================================================
-- ============================================================

-- D1. INSERT 1000 rows WITH both NC indexes active — measure writes
SET STATISTICS IO ON;

INSERT INTO Sales (SaleId, CustomerId, ProductId, SaleDate, Amount, Region)
SELECT TOP 1000
    100001 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
    ABS(CHECKSUM(NEWID())) % 10000 + 1,
    ABS(CHECKSUM(NEWID())) % 500  + 1,
    DATEADD(DAY, ABS(CHECKSUM(NEWID())) % 1096, '2022-01-01'),
    CAST((ABS(CHECKSUM(NEWID())) % 99900 + 100) / 100.0 AS DECIMAL(10,2)),
    CHOOSE(ABS(CHECKSUM(NEWID())) % 5 + 1,
           'North','South','East','West','Central')
FROM sys.all_objects a CROSS JOIN sys.all_objects b;

SET STATISTICS IO OFF;
GO
-- With 2 NC indexes: every INSERT writes to 3 B-trees (clustered + 2 NC)
-- Logical reads reported by STATISTICS IO include index-maintenance writes
-- Write cost = ~3x compared to a heap with no indexes


-- ============================================================
-- Cleanup (optional)
-- ============================================================
-- DROP TABLE IF EXISTS Sales;
