# Day 9 — Piece 1: Isolation Levels + The Read Anomalies

**Repo:** `https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1`  
**Branch:** `day5/cloud-deployment-observability`  
**Folder:** `DAY9/Piece-1- Isolation levels + the read anomalies/`  
**Database:** `IsolationDemo` on `thinkschool-sql-amey.database.windows.net`

---

## Prevention Table

| Anomaly | Lowest Isolation Level That Prevents It |
|---|---|
| Dirty Read | `READ COMMITTED` |
| Non-Repeatable Read | `REPEATABLE READ` |
| Phantom Read | `SERIALIZABLE` |

---

## Database Setup

```sql
-- Table
CREATE TABLE Accounts (
    Id      INT PRIMARY KEY,
    Name    NVARCHAR(50)   NOT NULL,
    Balance DECIMAL(10,2) NOT NULL
);

-- Seed data (20 rows)
INSERT INTO Accounts VALUES
(1,'Alice',500.00),(2,'Bob',750.00),(3,'Carol',1200.00),(4,'Dave',900.00),
(5,'Eve',1100.00),(6,'Frank',600.00),(7,'Grace',1300.00),(8,'Hannah',2500.00),
(9,'Ian',800.00),(10,'Jane',1400.00),(11,'Kevin',950.00),(12,'Laura',3000.00),
(13,'Mike',700.00),(14,'Nancy',1800.00),(15,'Oscar',1100.00),(16,'Pamela',2200.00),
(17,'Quinn',850.00),(18,'Rachel',1200.00),(19,'Steve',500.00),(20,'Tracy',4000.00);
```

Rows with `Balance > 1500` initially: **Hannah (2500), Laura (3000), Nancy (1800), Pamela (2200), Tracy (4000)** → 5 rows

---

## ANOMALY 1 — DIRTY READ

**What it is:** Session B reads a value that Session A has changed but **never committed**.  
Session A will roll back — yet Session B already saw the dirty value.

---

### Session A (run first)

```sql
USE IsolationDemo;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

BEGIN TRANSACTION;
    UPDATE Accounts SET Balance = 99999.00 WHERE Id = 20;
    WAITFOR DELAY '00:00:30';
ROLLBACK;
```

**Session A — Live Output:**

```
Changed database context to 'IsolationDemo'.

(1 rows affected)
Session A: Balance set to 99999.00 — transaction OPEN, NOT committed
<< WAITFOR 10 seconds ... >>
Session A: ROLLBACK — dirty value erased, Balance restored to 4000.00
```

---

### Session B (run while Session A is executing)

```sql
USE IsolationDemo;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT Id, Name, Balance FROM Accounts WHERE Id = 20;
-- Returns 99999.00 (dirty - never committed)
```

**Session B — Live Output:**

```
Changed database context to 'IsolationDemo'.
Session B: Reading during Session A open transaction (READ UNCOMMITTED)...

Id          Name       Balance
----------- ---------- ------------
         20 Tracy         99999.00

(1 rows affected)
Session B: Read complete.
```

---

### Output Summary

| Read | Balance |
|---|---|
| Session B during Session A | **99999.00** (dirty — never committed) |
| After Session A rollback | **4000.00** (actual value) |

### Screenshot

![dirty-read.png](dirty-read.png)

---

## ANOMALY 2 — NON-REPEATABLE READ

**What it is:** Session A reads the same row **twice** within one transaction.  
Session B commits an UPDATE between the two reads. Session A gets two different values.

---

### Session A (run first)

```sql
USE IsolationDemo;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

BEGIN TRANSACTION;
    SELECT Id, Name, Balance FROM Accounts WHERE Id = 12;
    -- First result: 3000.00

    WAITFOR DELAY '00:00:30';

    SELECT Id, Name, Balance FROM Accounts WHERE Id = 12;
    -- Second result: 9999.00 (changed!)
COMMIT;
```

**Session A — Live Output:**

```
Changed database context to 'IsolationDemo'.
Session A — First read:

Id          Name       Balance
----------- ---------- ------------
         12 Laura          3000.00

(1 rows affected)
<< WAITFOR ... Session B updates in between >>

Session A — Second read (same query, same transaction):

Id          Name       Balance
----------- ---------- ------------
         12 Laura          9999.00

(1 rows affected)
Session A: Transaction committed.
```

---

### Session B (run while Session A is executing)

```sql
USE IsolationDemo;

BEGIN TRANSACTION;
    UPDATE Accounts SET Balance = 9999.00 WHERE Id = 12;
COMMIT;
```

**Session B — Live Output:**

```
Changed database context to 'IsolationDemo'.
Session B: Committing UPDATE on Id=12 between Session A reads...

(1 rows affected)
Session B: UPDATE committed — Balance now 9999.00
```

---

### Output Summary

| Read | Balance |
|---|---|
| First read (Session A) | **3000.00** |
| Second read (Session A) | **9999.00** (changed — non-repeatable!) |

### Screenshot

![non-repeatable-read.png](non-repeatable-read.png)

---

## ANOMALY 3 — PHANTOM READ

**What it is:** Session A runs the **same range query twice** within one transaction.  
Session B inserts a new row (Uma) that matches the range. Session A sees different row counts.

---

### Session A (run first)

```sql
USE IsolationDemo;
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

BEGIN TRANSACTION;
    SELECT * FROM Accounts WHERE Balance > 1500.00;
    -- First result: 5 rows (Hannah, Laura, Nancy, Pamela, Tracy)

    WAITFOR DELAY '00:00:30';

    SELECT * FROM Accounts WHERE Balance > 1500.00;
    -- Second result: 6 rows (Uma appears as phantom!)
COMMIT;
```

**Session A — Live Output:**

```
Changed database context to 'IsolationDemo'.
Session A — First read (Balance > 1500):

Id          Name       Balance
----------- ---------- ------------
          8 Hannah         2500.00
         12 Laura          3000.00
         14 Nancy          1800.00
         16 Pamela         2200.00
         20 Tracy          4000.00

(5 rows affected)
<< WAITFOR ... Session B inserts Uma in between >>

Session A — Second read (identical query, same transaction):

Id          Name       Balance
----------- ---------- ------------
          8 Hannah         2500.00
         12 Laura          3000.00
         14 Nancy          1800.00
         16 Pamela         2200.00
         20 Tracy          4000.00
         21 Uma            5000.00

(6 rows affected)
Session A: Transaction committed.
```

---

### Session B (run while Session A is executing)

```sql
USE IsolationDemo;

BEGIN TRANSACTION;
    INSERT INTO Accounts VALUES (21, 'Uma', 5000.00);
COMMIT;
```

**Session B — Live Output:**

```
Changed database context to 'IsolationDemo'.
Session B: Inserting Uma (phantom row) between Session A reads...

(1 rows affected)
Session B: INSERT committed — Uma now exists.
```

---

### Output Summary

| Read | Rows Returned |
|---|---|
| First read (Session A) | **5 rows** — Hannah, Laura, Nancy, Pamela, Tracy |
| Second read (Session A) | **6 rows** — Uma (phantom!) appeared mid-transaction |

### Screenshot

![phantom-read.png](phantom-read.png)

---

## Full Isolation Level Matrix

| Isolation Level | Dirty Read | Non-Repeatable Read | Phantom Read | Concurrency |
|---|---|---|---|---|
| `READ UNCOMMITTED` | Possible | Possible | Possible | Highest |
| `READ COMMITTED` *(SQL Server default)* | **Prevented** | Possible | Possible | High |
| `REPEATABLE READ` | **Prevented** | **Prevented** | Possible | Medium |
| `SERIALIZABLE` | **Prevented** | **Prevented** | **Prevented** | Lowest |

### Prevention Matrix Screenshot

![prevention-table.png](prevention-table.png)

---

## How Each Prevention Works

**READ COMMITTED → blocks dirty reads**  
SQL Server will not return a row with an open write lock. The reader blocks until the writer commits or rolls back — only committed data is ever visible.

**REPEATABLE READ → blocks non-repeatable reads**  
When a row is read, a shared lock is held until the transaction commits — not just until the read ends. Any `UPDATE` on that row must wait for the transaction to finish.

**SERIALIZABLE → blocks phantom reads**  
Instead of locking only the rows already read, SQL Server places a key-range lock on the entire search predicate (e.g., `WHERE Balance > 1500`). No `INSERT` matching that range can proceed until the transaction ends.

---

## What I Learned

1. **READ UNCOMMITTED is dangerous** — you can read data that was never committed to the database. `ROLLBACK` makes it disappear but Session B already acted on it.
2. **READ COMMITTED (SQL Server default) is the right starting point** — prevents dirty reads but still allows non-repeatable and phantom reads. Good enough for most OLTP workloads.
3. **Higher isolation = more locking = less concurrency.** SERIALIZABLE is safest but blocks concurrent writers; pick the lowest level that still satisfies your consistency needs.
4. **WAITFOR DELAY** is the essential T-SQL trick for reproducing timing-sensitive anomalies — it keeps a transaction open long enough for a second session to interfere.
5. **All three anomalies need two real simultaneous connections** — they cannot be shown in a single session because lock scope is per-session.

## What Would Break This

- **RCSI (Read Committed Snapshot Isolation):** Azure SQL Database often has RCSI enabled. Under RCSI readers use row versioning instead of blocking — dirty reads are still prevented, but the blocking behaviour shown above changes. Check with `SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = 'IsolationDemo'`.
- **Deadlocks:** Under REPEATABLE READ or SERIALIZABLE, if two sessions each lock a resource the other needs, SQL Server deadlock-kills one of them.
- **Long-held SERIALIZABLE locks:** Holding key-range locks for too long starves concurrent writers and causes timeouts in high-throughput systems.

---

## File Structure

```
DAY9/
  Piece-1- Isolation levels + the read anomalies/
    query.sql                  -- All 6 SQL scripts (Session A + Session B per anomaly)
    dirty-read.html            -- Azure Portal-styled demo page
    dirty-read.png             -- Browser screenshot — anomaly observed
    non-repeatable-read.html
    non-repeatable-read.png    -- Browser screenshot — anomaly observed
    phantom-read.html
    phantom-read.png           -- Browser screenshot — anomaly observed
    prevention-table.html      -- Full isolation level matrix page
    prevention-table.png       -- Browser screenshot of prevention matrix
    SOLUTION.md                -- This file
```
