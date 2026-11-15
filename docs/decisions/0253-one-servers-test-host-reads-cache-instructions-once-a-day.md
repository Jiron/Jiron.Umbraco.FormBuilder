# 0253. The browser tests' hosts of one server read and prune cache instructions once a day

- Status: accepted; refines [ADR-0011](0011-testing-strategy.md) and [ADR-0250](0250-the-umbraco-18-line.md) (what
  the SQLite stall is, and one cause of it taken away); corrects the record of the outbox in
  [ADR-0252](0252-the-last-pass.md) (each queue has its own time, as first written)
- Date: 2026-11-15
- Roadmap step: 21.1

## Context

The clean-clone verification of 21.1 failed twice in its browser stage, and most whole runs of the browser suite on
the pass's commits failed the same way: some minutes into the backoffice specs every database call of the test host
waited, Umbraco logged "Cache instruction sync did not complete within 00:01:00 and may be stalled on a hung
database connection", sign-ins hung, and every test after that failed. ADR-0011 knew this as Umbraco's stall on
SQLite and lets the gate run the browser projects once more after it; overlapping sign-ins were taken for its cause,
and they run one at a time since.

They are one cause. The stall that failed the verification is another, and it is now read from stalled hosts
themselves: a memory dump of each, and in it SQLite's own record of who holds what.

- **A request adds a cache instruction as it ends.** Whatever changed something tells the site's other servers
  (`BatchedDatabaseServerMessenger.SendMessages`, an insert into `umbracoCacheInstruction`).
- **Two of Umbraco 17's jobs read that table and then write, in one transaction.** Every five seconds the sync reads
  the instructions and then writes how far it got (`DatabaseServerMessenger.Sync`, an update of
  `umbracoLastSynced`). Every minute the pruning job reads the highest instruction's id and then deletes the old ones
  (`CacheInstructionsPruningJob`).
- **On SQLite's shared cache a job and an insert can wait for each other.** The insert holds the cache's one write
  transaction and waits for the table lock on `umbracoCacheInstruction`; the job holds a read lock on that table,
  from its read, and waits for the write transaction. SQLite answers both "database table is locked".
- **Umbraco tries again inside the open transactions,** so neither lets go. And while the writer waits for a table,
  SQLite's shared cache starts no other transaction, a read included: every sign-in, every page and every job of the
  host waits with them.
- **In the dumps:** the shared cache in a write transaction with its pending flag set; its writer the connection
  whose statement is the insert; a read lock on table root 129, which the site's schema names
  `umbracoCacheInstruction`, held by the connection whose statement is the sync's update in one stall and the pruning
  job's delete, after its `SELECT MAX`, in the next; the other connections each waiting at their first statement,
  none holding a lock. The stacks of an earlier stall show the sync and the insert too, and Umbraco's line about the
  cache instruction sync is in the log of every stalled run, the ones that stalled two minutes after a host started
  among them.

No statement of Jiron Form Builder is in any of these transactions. Every save in the backoffice adds an
instruction, Umbraco's own for a page or a user and the package's for a form, a theme, an option list or a setting,
so the more workers save at the same moment, the likelier a job and an insert meet; chance decides which run.

That chance misled the search twice. Five whole runs on the builds before the pass's backoffice commit happened not
to stall for long (one of them stalled as its host started), and six of the nine after it did, which read as a fault
of the pass. A bisect then ended at the change that gives each outbox queue its own time in a pass, because one run
without it was clean, and that change was taken out (`aa9fddc2`); the next run stalled all the same. A second reading
blamed a test's seeding of a thousand entries; the failed verification's own log shows the stall began long before
that test.

## Decision

- **A browser-test host of one server reads cache instructions, and prunes them, once a day:** `hostEnvironment`
  sets Umbraco's `Umbraco:CMS:Global:DatabaseServerMessenger:TimeBetweenSyncOperations` and
  `TimeBetweenPruneOperations` to `1.00:00:00`, where Umbraco has five seconds and a minute. A site of one server
  has no other server's instructions to read: what it changes reaches its own caches at once, and the instructions
  are for the others. With nothing reading the table during a run, the inserts wait for no one.
- **Hosts that share a database keep Umbraco's intervals** (`severalServers`, which the two-server spec asks for):
  there the instructions are how a form published on one server reaches the other, and the spec checks that.
- **Nothing of the product changes.** The package tells the other servers what changed as before, and the demo
  keeps Umbraco's own settings, as a site has them.
- **The gate's one rerun after a stall stays** (ADR-0011). A host still reads the instructions once, a minute after
  it starts, and Umbraco's update of a signed-in user reads before it writes too (ADR-0250).
- **Each outbox queue has its own time in a pass again,** as ADR-0252 first decided, now with the test it lacked: a
  queue that used up its time leaves the next one all of its own (`OutboxPassTests`, which fails with one deadline for
  the pass).
- **For a site:** STATUS.md names the limit. Umbraco 17 on SQLite, with several editors saving at the same moment,
  can meet this stall whether or not the package is installed; Umbraco 18 no longer uses the shared cache, and SQL
  Server has no such lock. Telling Umbraco HQ is the owner's (`docs/OWNER-TODO.md`).

## How it was read

The .NET diagnostic tools, installed to a tool path under `%TEMP%` and never globally, took a dump of the test host
while it stalled. Its thread stacks show every database thread waiting and none working. The provider's objects
show no transaction left open and no reader left unread: every connection at the first statement of its work, or,
for the two jobs, at the write after their read. What is left is SQLite's own memory: each connection's autocommit
flag and last error, then the shared cache all of them point to, its writer, its flags and its list of table locks
with their holders.

The first dump named the sync. With the sync set to a day, the next whole run stalled ten minutes in, and its dump
named the pruning job in the sync's place.

## Consequences

- With both jobs at a day the whole browser suite passed the first time it ran: 553 tests in 21 minutes, no command at
  its timeout and no locked table in the host's log. With Umbraco's intervals six of nine whole runs had stalled, and
  with only the sync at a day the next one did.
- The stall is no longer "Umbraco's limit, run again" without a name: a run that stalls leaves the hang marker as
  before, and a job that reads `umbracoCacheInstruction` before it writes is the first thing to look for.
- The settings travel to the Umbraco 18 line with the support code. Umbraco 18 has no shared cache, so there they
  only spare the host two jobs it has no use for.
