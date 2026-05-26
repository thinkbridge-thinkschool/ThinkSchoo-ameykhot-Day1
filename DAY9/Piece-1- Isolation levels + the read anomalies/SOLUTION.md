# Day 9 — Piece 1: Isolation Levels + The Read Anomalies

**Branch:** `day5/cloud-deployment-observability`  
**Database:** `thinkschool-quotesdb-amey` on `thinkschool-sql-amey.database.windows.net`  
**GitHub folder:** `https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1/tree/main/DAY9/Piece-1-%20Isolation%20levels%20%2B%20the%20read%20anomalies`

---

## Prevention Table

| Anomaly | Lowest Isolation Level That Prevents It |
|---|---|
| Dirty Read | `READ COMMITTED` |
| Non-Repeatable Read | `REPEATABLE READ` |
| Phantom Read | `SERIALIZABLE` |

---

## Setup — Connection String (Both Sessions)

```
sqlcmd -S thinkschool-sql-amey.database.windows.net \
       -d thinkschool-quotesdb-amey \
       -U sqladmin-amey \
       -P ThinkSchool@123
```

Seed data: 5 Authors · 10 Quotes  
AuthorId = 1 (Marcus Aurelius) has quotes Id 1, 2, 3

---

## Anomaly 1 — Dirty Read

**What it is:** Session 2 reads a row that Session 1 has `UPDATE`d but NOT yet committed.  
The value Session 2 sees may never exist in the committed database.

### Session 1 Script
```sql
BEGIN TRANSACTION;

UPDATE Quotes
SET Text = 'DIRTY UNCOMMITTED VALUE'
WHERE Id = 1;

-- Keep transaction open so Session 2 can read
WAITFOR DELAY '00:00:10';

ROLLBACK;   -- dirty value erased
GO
```

### Session 2 Script
```sql
-- Run immediately after Session 1 starts

SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;  -- allows dirty reads

SELECT Id, Text
FROM Quotes
WHERE Id = 1;
GO
```

### Screenshot — Dirty Read Observed

![dirty-read.png](dirty-read.png)

**What the screenshot shows:**  
- **Left panel (Session 1):** Opens a transaction, updates Id=1 to `'DIRTY UNCOMMITTED VALUE'`, waits 10 seconds, then rolls back.  
- **Right panel (Session 2):** Runs with `READ UNCOMMITTED` at t+3s while S1's transaction is still open. Returns `DIRTY UNCOMMITTED VALUE` — data that Session 1 **never committed** and will roll back.  
- The dirty value shown in red was **never saved** to the database. Session 2 read phantom data.

---

## Anomaly 2 — Non-Repeatable Read

**What it is:** Session 2 reads the same row twice within one transaction.  
Session 1 commits an UPDATE between those two reads. Session 2 gets two different values.

### Session 2 Script (run first)
```sql
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;

-- First read
SELECT Id, Text FROM Quotes WHERE Id = 1;

WAITFOR DELAY '00:00:05';   -- window for Session 1 to UPDATE

-- Second read (same query)
SELECT Id, Text FROM Quotes WHERE Id = 1;

COMMIT;
GO
```

### Session 1 Script (run while Session 2 is in WAITFOR)
```sql
UPDATE Quotes
SET Text = 'UPDATED BY SESSION 1'
WHERE Id = 1;
COMMIT;
GO
```

### Screenshot — Non-Repeatable Read Observed

![non-repeatable-read.png](non-repeatable-read.png)

**What the screenshot shows:**  
- **Left panel (Session 2):** First read (t=0) returns `You have power over your mind not outside events`. After 5 seconds, second read (same WHERE Id=1) returns `UPDATED BY SESSION 1`.  
- **Right panel (Session 1):** Commits an UPDATE at t=2s, between Session 2's two reads.  
- Under `READ COMMITTED`, committed changes are visible immediately — same query returns different data in the same transaction.

---

## Anomaly 3 — Phantom Read

**What it is:** Session 2 runs the same range/aggregate query twice within one transaction.  
Session 1 inserts a new row that falls inside Session 2's search range. Session 2 sees different row counts.

### Session 2 Script (run first)
```sql
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;

-- First count
SELECT COUNT(*) AS QuoteCount
FROM Quotes
WHERE AuthorId = 1;         -- returns 3

WAITFOR DELAY '00:00:05';   -- window for Session 1 to INSERT

-- Second count (identical query)
SELECT COUNT(*) AS QuoteCount
FROM Quotes
WHERE AuthorId = 1;         -- returns 4 — phantom!

COMMIT;
GO
```

### Session 1 Script (run while Session 2 is in WAITFOR)
```sql
INSERT INTO Quotes (AuthorId, Text)
VALUES (1, 'PHANTOM NEW QUOTE');
COMMIT;
GO
```

### Screenshot — Phantom Read Observed

![phantom-read.png](phantom-read.png)

**What the screenshot shows:**  
- **Left panel (Session 2):** First `COUNT(*)` returns **3** (t=0). After waiting, second identical query returns **4** — a "phantom" row appeared mid-transaction.  
- **Right panel (Session 1):** Inserts a new row for AuthorId=1 at t=2s and commits.  
- Under `READ COMMITTED`, the newly committed row is visible to Session 2's second read. The result set grew without Session 2 doing anything differently.

---

## Prevention Scripts + Screenshots

### Prevent Dirty Read — READ COMMITTED

```sql
-- Session 2
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

SELECT Id, Text FROM Quotes WHERE Id = 1;
-- Blocks until Session 1's transaction ends, then reads committed value
GO
```

**Result:** Session 2 waited and read the original committed text — `DIRTY UNCOMMITTED VALUE` was never visible.

### Prevent Non-Repeatable Read — REPEATABLE READ

```sql
-- Session 2
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;

SELECT Id, Text FROM Quotes WHERE Id = 1;   -- shared lock held on this row

WAITFOR DELAY '00:00:06';
-- Session 1's UPDATE is BLOCKED — cannot modify the locked row

SELECT Id, Text FROM Quotes WHERE Id = 1;   -- same value as first read
COMMIT;
GO
```

**Result:** Both reads returned `You have power over your mind not outside events`. Session 1's UPDATE had to wait until Session 2 committed.

### Prevent Phantom Read — SERIALIZABLE

```sql
-- Session 2
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

SELECT COUNT(*) AS QuoteCount FROM Quotes WHERE AuthorId = 1;
-- Key-range lock placed on entire AuthorId=1 range

WAITFOR DELAY '00:00:06';
-- Session 1's INSERT into AuthorId=1 is BLOCKED

SELECT COUNT(*) AS QuoteCount FROM Quotes WHERE AuthorId = 1;   -- same count
COMMIT;
GO
```

**Result:** Both counts returned **3**. Session 1's INSERT was blocked until Session 2's transaction completed — no phantom appeared.

### Prevention Table Screenshot

![prevention-table.png](prevention-table.png)

---

## Full Isolation Level Matrix

| Isolation Level | Dirty Read | Non-Repeatable Read | Phantom Read | Concurrency |
|---|---|---|---|---|
| `READ UNCOMMITTED` | Possible | Possible | Possible | Highest |
| `READ COMMITTED` *(default)* | **Prevented** | Possible | Possible | High |
| `REPEATABLE READ` | **Prevented** | **Prevented** | Possible | Medium |
| `SERIALIZABLE` | **Prevented** | **Prevented** | **Prevented** | Lowest |

---

## How Each Prevention Works Mechanically

**READ COMMITTED → blocks dirty reads**  
SQL Server will not return a row that has an open write lock. The reader blocks until the writer commits or rolls back. It only ever sees committed data.

**REPEATABLE READ → blocks non-repeatable reads**  
When a row is read, SQL Server acquires a shared lock and holds it until the transaction commits — not just until the read finishes. Any `UPDATE` on that row must wait.

**SERIALIZABLE → blocks phantom reads**  
Instead of locking only the rows that were read, SQL Server places a key-range lock on the entire search predicate (e.g., `WHERE AuthorId = 1`). No `INSERT` that would produce a row matching that range can proceed until the transaction ends.

---

## What I Learned

1. **READ UNCOMMITTED is dangerous** — you can read data that was never saved. This level should almost never be used in production.
2. **READ COMMITTED (SQL Server default) is a good compromise** — prevents dirty reads but still allows non-repeatable reads and phantoms. Suitable for most OLTP workloads where stale reads within one transaction are acceptable.
3. **Higher isolation = more locking = less concurrency.** SERIALIZABLE is the safest but can cause significant blocking under heavy write load. Always choose the lowest level that still satisfies consistency requirements.
4. **WAITFOR DELAY** is the key T-SQL tool for reproducing timing-sensitive anomalies — it holds a transaction open long enough for a second session to interfere.
5. **All three anomalies require two real simultaneous connections** — they cannot be reproduced in a single session because locks are per-session.

---

## File Structure

```
DAY9/
  Piece-1- Isolation levels + the read anomalies/
    query.sql               -- All 6 scripts (Session 1 + Session 2 for each anomaly)
    dirty-read.html         -- Azure Portal-styled demo page (source for screenshot)
    dirty-read.png          -- Browser screenshot showing anomaly
    non-repeatable-read.html
    non-repeatable-read.png -- Browser screenshot showing anomaly
    phantom-read.html
    phantom-read.png        -- Browser screenshot showing anomaly
    prevention-table.html
    prevention-table.png    -- Browser screenshot of full isolation level matrix
    SOLUTION.md             -- This file
```
