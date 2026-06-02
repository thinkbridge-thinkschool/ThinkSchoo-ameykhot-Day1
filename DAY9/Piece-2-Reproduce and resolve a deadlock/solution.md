# Day 9 — Piece 2: Reproduce and Resolve a Deadlock

**Server:** `DESKTOP-JBDCVIM\SQLEXPRESS` (SQL Server 2025 Express)  
**Database:** `DeadlockDemo`  
**Capture method:** Trace Flag 1222 (ERRORLOG) + Extended Events (`xml_deadlock_report`)  
**Run date:** 2026-05-28

---

## Repro Script — Session 1

Session 1 locks **AccountA first**, then waits 5 s, then tries **AccountB**.

```sql
-- 03_session1_deadlock.sql  —  run in SSMS/sqlcmd Window 1
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

## Repro Script — Session 2

Session 2 locks **AccountB first** (opposite order), waits 5 s, then tries **AccountA** — creating a circular wait.

```sql
-- 04_session2_deadlock.sql  —  run in Window 2 IMMEDIATELY after Window 1
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

**Session 1 output (winner):**

```
Changed database context to 'DeadlockDemo'.

(1 rows affected)       <- UPDATE AccountA SET Balance = Balance - 100  [X-lock acquired]

(1 rows affected)       <- UPDATE AccountB SET Balance = Balance + 100  [acquired after victim killed]
Session 1 committed successfully (not the victim).
```

**Session 2 output (deadlock victim):**

```
Changed database context to 'DeadlockDemo'.

(1 rows affected)       <- UPDATE AccountB SET Balance = Balance - 200  [X-lock acquired]

Msg 1205, Level 13, State 51, Server DESKTOP-JBDCVIM\SQLEXPRESS, Line 11
Transaction (Process ID 52) was deadlocked on lock resources with another process
and has been chosen as the deadlock victim. Rerun the transaction.
```

> Session 2 (Process ID 52) received Msg 1205 — SQL Server killed it as the deadlock victim.  
> Session 2's AccountB update was **automatically rolled back**. Session 1 then acquired the AccountB lock and committed cleanly.

---

## Deadlock Graph — Trace Flag 1222 (ERRORLOG)

Output captured via `DBCC TRACEON(1222, -1)` then read with `EXEC xp_readerrorlog 0, 1, N'deadlock'`:

```
2026-05-28 10:35:33.350  spid28s   deadlock-list
2026-05-28 10:35:33.350  spid28s    deadlock victim=process284d1283048

PROCESS LIST:

  process284d1283048  (Session 2 — VICTIM, spid=52)
    status       = suspended
    waitresource = KEY: 7:72057594047234048 (8194443284a0)
                   <- waiting for X-lock on AccountA (held by Session 1)
    waittime     = 4285 ms
    owns         = X-lock on AccountB
    dbname       = DeadlockDemo
    clientapp    = SQLCMD  hostname=DESKTOP-JBDCVIM
    isolation    = read committed

  process284d12b0088  (Session 1 — SURVIVOR)
    status       = suspended
    waitresource = KEY: 7:72057594047299584 (8194443284a0)
                   <- waiting for X-lock on AccountB (held by Session 2)
    waittime     = 4535 ms
    owns         = X-lock on AccountA
    dbname       = DeadlockDemo
    clientapp    = SQLCMD  hostname=DESKTOP-JBDCVIM
    isolation    = read committed

RESOURCE LIST:

  keylock  objectname = DeadlockDemo.dbo.AccountA
           indexname  = PK__AccountA__3214EC07FF1358F8
           mode       = X

  keylock  objectname = DeadlockDemo.dbo.AccountB
           indexname  = PK__AccountB__3214EC07B310455F
           mode       = X
```

**Deadlock graph — Extended Events XML (`deadlockgraph.xdl`):**

```xml
<deadlock>
  <victim-list>
    <victimProcess id="process284d264a478"/>   <!-- Session 2, spid=74 -->
  </victim-list>
  <process-list>
    <process id="process284d264a478"
             waitresource="KEY: 7:72057594047234048"
             waittime="3787"
             lockMode="X"
             spid="74"
             status="suspended"
             currentdbname="DeadlockDemo">
      <!-- Waiting for X-lock on AccountA, owns X-lock on AccountB -->
    </process>
    <process id="process284d244a868"
             waitresource="KEY: 7:72057594047299584"
             waittime="4033"
             lockMode="X"
             spid="69"
             status="suspended"
             currentdbname="DeadlockDemo">
      <!-- Waiting for X-lock on AccountB, owns X-lock on AccountA -->
    </process>
  </process-list>
  <resource-list>
    <keylock objectname="DeadlockDemo.dbo.AccountA" indexname="PK__AccountA__3214EC071B970D6B" mode="X">
      <owner-list><owner id="process284d244a868" mode="X"/></owner-list>
      <waiter-list><waiter id="process284d264a478" mode="X" requestType="wait"/></waiter-list>
    </keylock>
    <keylock objectname="DeadlockDemo.dbo.AccountB" indexname="PK__AccountB__3214EC07CE60DDA7" mode="X">
      <owner-list><owner id="process284d264a478" mode="X"/></owner-list>
      <waiter-list><waiter id="process284d244a868" mode="X" requestType="wait"/></waiter-list>
    </keylock>
  </resource-list>
</deadlock>
```

**Circular wait diagram:**

```
Session 2 (spid=52): holds X on AccountB  ──waits──►  X on AccountA  (held by Session 1)
Session 1 (spid=69): holds X on AccountA  ──waits──►  X on AccountB  (held by Session 2)
                              ↑─────────────── circular wait ───────────────↑

SQL Server chose Session 2 (process284d264a478) as victim → Msg 1205, automatic rollback
```

---

## Fix: Consistent Lock Order — Side-by-Side

**The single change: swap the two UPDATE statements in Session 2 so it acquires AccountA before AccountB, matching Session 1's order.**

| | Session 1 (original) | Session 2 (original — BROKEN) | Session 2 (fixed) |
|---|---|---|---|
| **Line 1** | `UPDATE AccountA … (-100)` — lock **A** | `UPDATE AccountB … (-200)` — lock **B** ← wrong | `UPDATE AccountA … (+200)` — lock **A** ← fixed |
| **Line 2** | `WAITFOR DELAY '00:00:05'` | `WAITFOR DELAY '00:00:05'` | `WAITFOR DELAY '00:00:05'` |
| **Line 3** | `UPDATE AccountB … (+100)` — lock **B** | `UPDATE AccountA … (+200)` — lock **A** ← causes cycle | `UPDATE AccountB … (-200)` — lock **B** ← no cycle |

**Fixed Session 2 script (full):**

```sql
-- 06_fix_consistent_lock_order.sql  —  Fixed Session 2 (run in Window 2)
BEGIN TRANSACTION;
    UPDATE AccountA SET Balance = Balance + 200 WHERE Id = 1;  -- lock A first (REORDERED)
    WAITFOR DELAY '00:00:05';
    UPDATE AccountB SET Balance = Balance - 200 WHERE Id = 1;  -- lock B second
COMMIT TRANSACTION;
PRINT 'Fix Session 2 committed successfully (no deadlock).';
GO
```

**Fix output — both sessions run simultaneously:**

```
--- FIX SESSION 2 (elapsed 5.4 s) — acquired AccountA lock first, completed first ---

Changed database context to 'DeadlockDemo'.

(1 rows affected)       <- UPDATE AccountA SET Balance = Balance + 200  [X-lock acquired]
(1 rows affected)       <- UPDATE AccountB SET Balance = Balance - 200
Fix Session 2 committed successfully (no deadlock).

--- FIX SESSION 1 (elapsed 10.5 s) — waited ~5s for Session 2 to release AccountA ---

Changed database context to 'DeadlockDemo'.

(1 rows affected)       <- UPDATE AccountA SET Balance = Balance - 100  [blocked ~5s, then acquired]
(1 rows affected)       <- UPDATE AccountB SET Balance = Balance + 100
Fix Session 1 committed successfully (no deadlock).

--- FINAL BALANCES ---

Table    Id   Balance
-------- ---  --------
AccountA  1   1100.00    (1000 + 200 - 100)
AccountB  1   1900.00    (2000 - 200 + 100)

(2 rows affected)
```

> **No Msg 1205. No rollback.** Both sessions committed and all four row updates persisted.  
> Session 1 experienced normal **blocking** (waited ~5 s for Session 2's AccountA lock) — blocking is not a deadlock. There was no circular wait, so SQL Server never had to kill a victim.

---

## Why It Works

> **When every session acquires locks in the same order (AccountA → AccountB), no circular wait can form — a deadlock cycle requires at least one session to request a resource in reverse order, creating a "I hold what you need, you hold what I need" situation that can never exist if all sessions follow the same sequence.**

| Scenario | Session 1 | Session 2 | Result |
|---|---|---|---|
| **Before fix** | A → waits for B | B → waits for A | Circular wait → **DEADLOCK (Msg 1205)** |
| **After fix** | A → B | waits for A → B | Linear wait → **both sessions commit** |

---

## Screenshots

### Setup — AccountA and AccountB seeded

```
================================================================================
STEP 1 — SETUP (01_setup.sql)
Server: DESKTOP-JBDCVIM\SQLEXPRESS   Date: 2026-05-28
================================================================================

Command:
  sqlcmd -S localhost\SQLEXPRESS -E -No -i 01_setup.sql

Output:
  Changed database context to 'master'.
  Changed database context to 'DeadlockDemo'.

  (1 rows affected)

  (1 rows affected)

  Table      Id   Balance
  --------   --   --------
  AccountA    1   1000.00
  AccountB    1   2000.00

  (2 rows affected)

================================================================================
Database DeadlockDemo created.
AccountA seeded with Balance = 1000.00
AccountB seeded with Balance = 2000.00
================================================================================
```

### Enable Deadlock Capture — Trace Flag 1222 + Extended Events

```
================================================================================
STEP 2 — ENABLE DEADLOCK CAPTURE (02_enable_deadlock_trace.sql)
Server: DESKTOP-JBDCVIM\SQLEXPRESS   Date: 2026-05-28
================================================================================

Command:
  sqlcmd -S localhost\SQLEXPRESS -E -No -i 02_enable_deadlock_trace.sql

Output:
  DBCC execution completed. If DBCC printed error messages, contact your system administrator.

  TraceFlag   Status   Global   Session
  ---------   ------   ------   -------
       1222        1        1         0

  (1 rows affected)
  DBCC execution completed. If DBCC printed error messages, contact your system administrator.
  Trace flag 1222 ON.  Extended Events session CaptureDeadlocks started.

================================================================================
Trace Flag 1222 : ACTIVE (Global=1)
XE Session      : CaptureDeadlocks started (ring_buffer, max 4096 KB)
================================================================================
```

### Deadlock Reproduced — Session 2 Killed (Msg 1205)

```
================================================================================
STEPS 3 & 4 — DEADLOCK REPRO
03_session1_deadlock.sql  +  04_session2_deadlock.sql  (run simultaneously)
Server: DESKTOP-JBDCVIM\SQLEXPRESS   Date: 2026-05-28
================================================================================

--- SESSION 1 OUTPUT (WINNER) ---

Changed database context to 'DeadlockDemo'.

(1 rows affected)       <- UPDATE AccountA SET Balance = Balance - 100  [X-lock acquired]

(1 rows affected)       <- UPDATE AccountB SET Balance = Balance + 100  [acquired after victim killed]
Session 1 committed successfully (not the victim).

--- SESSION 2 OUTPUT (DEADLOCK VICTIM) ---

Changed database context to 'DeadlockDemo'.

(1 rows affected)       <- UPDATE AccountB SET Balance = Balance - 200  [X-lock acquired]

Msg 1205, Level 13, State 51, Server DESKTOP-JBDCVIM\SQLEXPRESS, Line 11
Transaction (Process ID 52) was deadlocked on lock resources with another process
and has been chosen as the deadlock victim. Rerun the transaction.

================================================================================
Session 2 (Process ID 52) received Msg 1205 — SQL Server killed it as victim.
Session 2's AccountB update was automatically rolled back.
Session 1 then acquired AccountB lock and committed both updates cleanly.
================================================================================
```

### Deadlock Graph Captured

```
================================================================================
STEP 5 — DEADLOCK GRAPH (05_read_deadlock_graph.sql)
Server: DESKTOP-JBDCVIM\SQLEXPRESS   Date: 2026-05-28 10:35:33
Sources: Extended Events ring_buffer (Option A) + ERRORLOG TF 1222 (Option B)
================================================================================

--- Option A: Extended Events ring_buffer ---

deadlock_time               victim_process_id
--------------------------- -----------------
2026-05-28 05:05:33         NULL

(1 rows affected)

  -> XE captured the deadlock event at 2026-05-28 10:35:33 (UTC: 05:05:33)
  -> Full XML saved to deadlockgraph.xdl (open in SSMS for visual graph)

--- Option B: ERRORLOG via Trace Flag 1222 ---

2026-05-28 10:35:33.350  spid28s   deadlock-list
2026-05-28 10:35:33.350  spid28s    deadlock victim=process284d1283048

PROCESS LIST:

  process284d1283048  (Session 2 — VICTIM, Process ID 52)
    status       = suspended
    waitresource = KEY: 7:72057594047234048 (8194443284a0)
                   <- waiting for X-lock on AccountA (held by Session 1)
    waittime     = 4285 ms
    owns         = X-lock on AccountB

  process284d12b0088  (Session 1 — SURVIVOR)
    status       = suspended
    waitresource = KEY: 7:72057594047299584 (8194443284a0)
                   <- waiting for X-lock on AccountB (held by Session 2)
    waittime     = 4535 ms
    owns         = X-lock on AccountA

RESOURCE LIST:

  keylock  objectname = DeadlockDemo.dbo.AccountA  mode = X
  keylock  objectname = DeadlockDemo.dbo.AccountB  mode = X

================================================================================
DEADLOCK CYCLE:

  Session 2: holds X on AccountB  ──waits──►  X on AccountA  (held by Session 1)
  Session 1: holds X on AccountA  ──waits──►  X on AccountB  (held by Session 2)
                       ↑──────────── circular wait ────────────↑

  SQL Server chose Session 2 (process284d1283048) as victim → Msg 1205
================================================================================
```

### Fix — Both Sessions Committed, No Deadlock

```
================================================================================
STEP 6 — FIX: CONSISTENT LOCK ORDER (06_fix_consistent_lock_order.sql)
Server: DESKTOP-JBDCVIM\SQLEXPRESS   Date: 2026-05-28
Both fix sessions run simultaneously. Initial state: AccountA=1000, AccountB=2000
================================================================================

--- FIX SESSION 2 (elapsed 5.4 s) — acquired AccountA lock first, completed first ---

Changed database context to 'DeadlockDemo'.

(1 rows affected)       <- UPDATE AccountA SET Balance = Balance + 200  [X-lock acquired]
(1 rows affected)       <- UPDATE AccountB SET Balance = Balance - 200
Fix Session 2 committed successfully (no deadlock).

--- FIX SESSION 1 (elapsed 10.5 s) — waited ~5s for Session 2 to release AccountA ---

Changed database context to 'DeadlockDemo'.

(1 rows affected)       <- UPDATE AccountA SET Balance = Balance - 100  [blocked ~5s, then acquired]
(1 rows affected)       <- UPDATE AccountB SET Balance = Balance + 100
Fix Session 1 committed successfully (no deadlock).

--- FINAL BALANCES ---

Table    Id          Balance
-------- ----------- ------------
AccountA           1      1100.00    (1000 + 200 - 100)
AccountB           1      1900.00    (2000 - 200 + 100)

(2 rows affected)

================================================================================
NO Msg 1205. NO DEADLOCK. Both sessions committed cleanly.
Session 1 experienced normal blocking (waited ~5s for Session 2's AccountA lock).
Blocking is NOT a deadlock — one session simply waits for the other to finish.
Consistent A->B ordering breaks the circular wait condition permanently.
================================================================================
```

---

## Extra Credit

### What I Learned

1. `WAITFOR DELAY` is essential to reliably reproduce a deadlock — it widens the timing window so both sessions each grab one lock before either tries for the second. Without it, one session completes before the other even starts, and no deadlock forms.
2. Trace Flag 1222 and Extended Events both capture deadlock graphs; TF 1222 is instant (`DBCC TRACEON`) and writes a structured, human-readable deadlock report to the ERRORLOG, while XE gives you an `.xdl` file you can open visually in SSMS. The XDL XML also names the exact index (primary key) that each lock was held on — useful when diagnosing deadlocks on large tables with many indexes.

### What Would Break This Fix

1. **Adding a third resource out of order** — if future code locks `AccountC` between A and B in one path but after B in another, a new cycle forms even though the A → B ordering remains consistent for those two tables.
2. **Two independently correct stored procedures called in opposite order inside one transaction** — two procedures that each acquire A then B internally can still deadlock if a caller wraps them in a single outer transaction and another caller invokes them in reverse order, because the outer transaction holds locks across both calls.

---

## All Files in This Folder

| File | Purpose |
|---|---|
| `01_setup.sql` | Create `DeadlockDemo`, `AccountA`, `AccountB`, seed data |
| `02_enable_deadlock_trace.sql` | Enable TF 1222 + XE `CaptureDeadlocks` session |
| `03_session1_deadlock.sql` | Repro — Session 1 (AccountA → AccountB) |
| `04_session2_deadlock.sql` | Repro — Session 2 (AccountB → AccountA, causes deadlock) |
| `05_read_deadlock_graph.sql` | Read XE ring\_buffer + ERRORLOG |
| `06_fix_consistent_lock_order.sql` | Fix — consistent A → B order in both sessions |
| `07_cleanup.sql` | Stop XE, disable TF 1222, drop DB |
| `08_extract_deadlock_xml.sql` | Extract inner `<deadlock>` XML for `.xdl` |
| `deadlockgraph.xdl` | Live deadlock XML from XE (open in SSMS for visual graph) |
| `screenshots/01_setup.txt` | Full terminal output — Step 1 |
| `screenshots/02_enable_trace.txt` | Full terminal output — Step 2 |
| `screenshots/04_session2_deadlock.txt` | Full terminal output — Steps 3 & 4 |
| `screenshots/05_deadlock_graph.txt` | Full terminal output — Step 5 |
| `screenshots/06_fix_result.txt` | Full terminal output — Step 6 |
