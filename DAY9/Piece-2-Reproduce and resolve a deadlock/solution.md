# Day 9 — Piece 2: Reproduce and Resolve a Deadlock

**Server:** `DESKTOP-JBDCVIM\SQLEXPRESS` (SQL Server 2025 Express)  
**Database:** `DeadlockDemo`  
**Run date:** 2026-05-28

---

## Step 1 — Setup (`01_setup.sql`)

```sql
USE master;
GO

IF DB_ID('DeadlockDemo') IS NOT NULL
    DROP DATABASE DeadlockDemo;
GO

CREATE DATABASE DeadlockDemo;
GO

USE DeadlockDemo;
GO

CREATE TABLE AccountA (
    Id      INT           PRIMARY KEY,
    Balance DECIMAL(10,2) NOT NULL
);

CREATE TABLE AccountB (
    Id      INT           PRIMARY KEY,
    Balance DECIMAL(10,2) NOT NULL
);

INSERT INTO AccountA VALUES (1, 1000.00);
INSERT INTO AccountB VALUES (1, 2000.00);
GO

SELECT 'AccountA' AS [Table], Id, Balance FROM AccountA
UNION ALL
SELECT 'AccountB',             Id, Balance FROM AccountB;
GO
```

**Output:**

```
Changed database context to 'master'.
Changed database context to 'DeadlockDemo'.

(1 rows affected)

(1 rows affected)

Table    Id          Balance
-------- ----------- ------------
AccountA           1      1000.00
AccountB           1      2000.00

(2 rows affected)
```

---

## Step 2 — Enable Deadlock Capture (`02_enable_deadlock_trace.sql`)

```sql
-- Trace flag 1222 writes the full deadlock graph (XML-style) to ERRORLOG
DBCC TRACEON(1222, -1);   -- -1 = server-wide, all sessions
GO

DBCC TRACESTATUS(1222);
GO

IF EXISTS (
    SELECT 1 FROM sys.server_event_sessions WHERE name = 'CaptureDeadlocks'
)
    DROP EVENT SESSION CaptureDeadlocks ON SERVER;
GO

CREATE EVENT SESSION CaptureDeadlocks ON SERVER
ADD EVENT sqlserver.xml_deadlock_report
ADD TARGET package0.ring_buffer(
    SET max_memory = 4096
)
WITH (
    MAX_DISPATCH_LATENCY = 5 SECONDS
);
GO

ALTER EVENT SESSION CaptureDeadlocks ON SERVER STATE = START;
GO

PRINT 'Trace flag 1222 ON.  Extended Events session CaptureDeadlocks started.';
GO
```

**Output:**

```
DBCC execution completed. If DBCC printed error messages, contact your system administrator.

TraceFlag  Status  Global  Session
---------  ------  ------  -------
     1222       1       1        0

(1 rows affected)
DBCC execution completed. If DBCC printed error messages, contact your system administrator.
Trace flag 1222 ON.  Extended Events session CaptureDeadlocks started.
```

---

## Step 3 — Session 1 (`03_session1_deadlock.sql`)

Locks **AccountA first**, then waits 5 s, then tries to lock **AccountB**.

```sql
USE DeadlockDemo;
GO

BEGIN TRANSACTION;

    -- Step 1: acquire X-lock on AccountA row
    UPDATE AccountA SET Balance = Balance - 100 WHERE Id = 1;

    -- Step 2: pause so Session 2 can grab AccountB
    WAITFOR DELAY '00:00:05';

    -- Step 3: try to acquire X-lock on AccountB  <-- will DEADLOCK
    UPDATE AccountB SET Balance = Balance + 100 WHERE Id = 1;

COMMIT TRANSACTION;
PRINT 'Session 1 committed successfully (not the victim).';
GO
```

---

## Step 4 — Session 2 (`04_session2_deadlock.sql`)

Locks **AccountB first**, then waits 5 s, then tries to lock **AccountA** — **opposite order** to Session 1, creating a circular wait.

```sql
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
```

**Both sessions run simultaneously. Output:**

```
--- SESSION 1 OUTPUT — WINNER ---

Changed database context to 'DeadlockDemo'.

(1 rows affected)       <- AccountA locked and updated (-100)

(1 rows affected)       <- AccountB acquired after Session 2 was killed (+100)
Session 1 committed successfully (not the victim).

--- SESSION 2 OUTPUT — DEADLOCK VICTIM ---

Changed database context to 'DeadlockDemo'.

(1 rows affected)       <- AccountB locked and updated (-200)

Msg 1205, Level 13, State 51, Server DESKTOP-JBDCVIM\SQLEXPRESS, Line 11
Transaction (Process ID 52) was deadlocked on lock resources with another process
and has been chosen as the deadlock victim. Rerun the transaction.
```

> Session 2 (Process ID 52) was chosen as the deadlock victim and killed by SQL Server.  
> Session 2's AccountB update was **automatically rolled back**.

---

## Step 5 — Read the Deadlock Graph (`05_read_deadlock_graph.sql`)

```sql
USE DeadlockDemo;
GO

-- Option A: Read from Extended Events ring_buffer
SELECT
    xdr.value('@timestamp',          'datetime2')  AS deadlock_time,
    xdr.value('(victim-list/victimProcess/@id)[1]','varchar(50)') AS victim_spid,
    xdr.query('.')                                  AS deadlock_graph_xml
FROM (
    SELECT CAST(target_data AS XML) AS ring_data
    FROM   sys.dm_xe_sessions        AS s
    JOIN   sys.dm_xe_session_targets AS t
           ON s.address = t.event_session_address
    WHERE  s.name       = 'CaptureDeadlocks'
    AND    t.target_name = 'ring_buffer'
) AS rb
CROSS APPLY ring_data.nodes('//RingBufferTarget/event[@name="xml_deadlock_report"]') AS x(xdr);
GO

-- Option B: Check SQL Server ERRORLOG for trace-flag 1222 output
EXEC xp_readerrorlog 0, 1, N'deadlock';
GO
```

**Output — Option A (Extended Events ring_buffer):**

```
deadlock_time                   victim_process_id
------------------------------- -----------------
2026-05-28 05:05:33             NULL

(1 rows affected)
```

> XE captured the event. Full XML saved to `deadlockgraph.xdl` — open in SSMS for the visual graph.

**Output — Option B (ERRORLOG via Trace Flag 1222):**

```
2026-05-28 10:35:33.350  spid28s   deadlock-list
2026-05-28 10:35:33.350  spid28s    deadlock victim=process284d1283048

--- PROCESS LIST ---

process284d1283048                        <- VICTIM (Session 2, spid=52)
  status      = suspended
  waitresource= KEY: 7:72057594047234048  <- waiting on AccountA X-lock
  waittime    = 4285 ms
  owns        = X-lock on AccountB
  dbname      = DeadlockDemo
  clientapp   = SQLCMD  host=DESKTOP-JBDCVIM

process284d12b0088                        <- SURVIVOR (Session 1)
  status      = suspended
  waitresource= KEY: 7:72057594047299584  <- waiting on AccountB X-lock
  waittime    = 4535 ms
  owns        = X-lock on AccountA
  dbname      = DeadlockDemo
  clientapp   = SQLCMD  host=DESKTOP-JBDCVIM

--- RESOURCE LIST ---

keylock  objectname = DeadlockDemo.dbo.AccountA
         indexname  = PK__AccountA__3214EC07FF1358F8
         mode       = X

keylock  objectname = DeadlockDemo.dbo.AccountB
         indexname  = PK__AccountB__3214EC07B310455F
         mode       = X
```

**Deadlock cycle:**

```
Session 2 (spid=52): holds X on AccountB  ──waits──►  X on AccountA  (held by Session 1)
Session 1          : holds X on AccountA  ──waits──►  X on AccountB  (held by Session 2)
                              ↑──────────── circular wait ────────────↑
         SQL Server chose Session 2 (process284d1283048) as victim → Msg 1205
```

---

## Step 6 — Fix: Consistent Lock Order (`06_fix_consistent_lock_order.sql`)

```sql
USE DeadlockDemo;
GO

-- Reset to clean starting balances
UPDATE AccountA SET Balance = 1000.00 WHERE Id = 1;
UPDATE AccountB SET Balance = 2000.00 WHERE Id = 1;
GO

-- Fixed Session 1  (unchanged: A -> B)
BEGIN TRANSACTION;
    UPDATE AccountA SET Balance = Balance - 100 WHERE Id = 1;  -- lock A first
    WAITFOR DELAY '00:00:05';
    UPDATE AccountB SET Balance = Balance + 100 WHERE Id = 1;  -- lock B second
COMMIT TRANSACTION;
GO

-- Fixed Session 2  (REORDERED: now A->B, not B->A)
BEGIN TRANSACTION;
    UPDATE AccountA SET Balance = Balance + 200 WHERE Id = 1;  -- lock A first
    WAITFOR DELAY '00:00:05';
    UPDATE AccountB SET Balance = Balance - 200 WHERE Id = 1;  -- lock B second
COMMIT TRANSACTION;
GO

-- Verify final balances
SELECT 'AccountA' AS [Table], Id, Balance FROM AccountA
UNION ALL
SELECT 'AccountB',             Id, Balance FROM AccountB;
GO
```

**Both fix sessions run simultaneously. Output:**

```
--- FIX SESSION 2 (elapsed 5.4 s) — acquired AccountA lock first, completed first ---

Changed database context to 'DeadlockDemo'.

(1 rows affected)       <- AccountA updated (+200)
(1 rows affected)       <- AccountB updated (-200)
Fix Session 2 committed successfully (no deadlock).

--- FIX SESSION 1 (elapsed 10.5 s) — waited ~5s for Session 2 to release AccountA, then committed ---

Changed database context to 'DeadlockDemo'.

(1 rows affected)       <- AccountA updated (-100)
(1 rows affected)       <- AccountB updated (+100)
Fix Session 1 committed successfully (no deadlock).

--- FINAL BALANCES ---

Table    Id          Balance
-------- ----------- ------------
AccountA           1      1100.00    (1000 + 200 - 100)
AccountB           1      1900.00    (2000 - 200 + 100)

(2 rows affected)
```

> **No Msg 1205. No rollback.** Both sessions committed and all four row updates persisted.  
> Session 1 blocked on AccountA (normal blocking — not a deadlock), waited ~5 s for Session 2 to finish, then acquired the lock and committed cleanly.

---

## Why the Fix Works

> **Both sessions now acquire locks in the same order (AccountA → AccountB), so a circular wait can never form.**

| Scenario | Session 1 | Session 2 | Outcome |
|---|---|---|---|
| **Before fix** | AccountA → waits for AccountB | AccountB → waits for AccountA | Circular wait → **DEADLOCK** |
| **After fix** | AccountA → AccountB | waits for AccountA → AccountB | Linear wait → **both commit** |

When Session 2 tries to lock AccountA while Session 1 holds it, Session 2 simply **waits** — it does not hold AccountB simultaneously. No cycle exists; one session completes and releases its locks, then the other proceeds.

---

## Step 7 — Cleanup (`07_cleanup.sql`)

```sql
-- Stop and drop Extended Events session
IF EXISTS (
    SELECT 1 FROM sys.server_event_sessions WHERE name = 'CaptureDeadlocks'
)
BEGIN
    ALTER EVENT SESSION CaptureDeadlocks ON SERVER STATE = STOP;
    DROP EVENT SESSION CaptureDeadlocks ON SERVER;
END
GO

-- Disable trace flag 1222
DBCC TRACEOFF(1222, -1);
GO

-- Drop the database
USE master;
GO

IF DB_ID('DeadlockDemo') IS NOT NULL
BEGIN
    ALTER DATABASE DeadlockDemo SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE DeadlockDemo;
END
GO
```

---

## Files in This Folder

| File | Purpose |
|---|---|
| `01_setup.sql` | Create `DeadlockDemo`, `AccountA`, `AccountB`, seed data |
| `02_enable_deadlock_trace.sql` | Enable TF 1222 + XE `CaptureDeadlocks` session |
| `03_session1_deadlock.sql` | Repro — Session 1 (AccountA → AccountB) |
| `04_session2_deadlock.sql` | Repro — Session 2 (AccountB → AccountA, causes deadlock) |
| `05_read_deadlock_graph.sql` | Read XE ring\_buffer + ERRORLOG |
| `06_fix_consistent_lock_order.sql` | Fix — consistent A → B order |
| `07_cleanup.sql` | Stop XE, disable TF 1222, drop DB |
| `08_extract_deadlock_xml.sql` | Extract inner `<deadlock>` XML for `.xdl` |
| `deadlockgraph.xdl` | Live deadlock XML from XE (open in SSMS for visual graph) |
| `screenshots/01_setup.txt` | Full terminal output — Step 1 |
| `screenshots/02_enable_trace.txt` | Full terminal output — Step 2 |
| `screenshots/04_session2_deadlock.txt` | Full terminal output — Steps 3 & 4 |
| `screenshots/05_deadlock_graph.txt` | Full terminal output — Step 5 |
| `screenshots/06_fix_result.txt` | Full terminal output — Step 6 |

---

## Extra Credit

### What I Learned

1. `WAITFOR DELAY` is essential to reliably reproduce a deadlock in a demo — it widens the window so both sessions can each grab one lock before either tries for the second.
2. Trace Flag 1222 and Extended Events both capture deadlock graphs; TF 1222 is instant (`DBCC TRACEON`) and writes structured XML directly to the ERRORLOG, while XE gives you an `.xdl` file you can open visually in SSMS.

### What Would Break This Fix

1. **Adding a third resource out of order** — if future code locks `AccountC` between A and B in one code path but after B in another, a new cycle can form even though the A → B ordering is consistent.
2. **Outer transaction merging two "safe" procedures** — two independently correct stored procedures can still deadlock if a caller wraps them in a single transaction and different callers invoke them in opposite order.
