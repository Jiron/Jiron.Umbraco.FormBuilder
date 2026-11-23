# 0040. The outbox repositories

- Status: accepted
- Date: 2026-10-24
- Roadmap step: 4.3e (decided and built); 8.1 (the action processor and the retry API), 8.2 (the mail queue and the
  limits for visitor mail), 10.x (undo and deleting entries), 11.x (the deliveries log)

## Context

[ADR-0007](0007-background-work-outbox-and-email.md) decided the state machine of `jfbActionRun` and `jfbEmail`: a
claim per row with a token of its own, leases, back-off, `Uncertain` when a lease runs out or an attempt is cut off,
the late finisher, the order of an entry's runs and "stop the rest". Its spike showed that a claim without its state
condition, a look followed by a claim, and an expired lease put back to `Pending` each deliver twice. Step 4.3e builds
the repositories that 8.1 and 8.2 run on. ADR-0007 leaves these questions open:

- **The due list.** A run that waits for an earlier run is due but cannot be claimed. A pass that reads the 25 oldest
  due runs could find only such runs and claim nothing, while other entries wait.
- **Which runs "earlier" and "the rest" mean.** An entry gets runs from more than one change: its submission (or the
  confirmation of its address), a user's approval, a rule's or an action's change of state. ADR-0007 and section 4.4
  speak of "the entry's" runs, written with one batch in mind. Ordered across all of them, a stopper of the submission
  left `Uncertain` would hold back the approval's mail until someone retries it, a stopper of the approval that fails
  would skip submission runs that are still retrying, and whether an approval's runs are skipped would depend on
  whether the user approved before or after the submission's stopper failed.
- **The token.** If a server used one token for all its claims, the late finisher of a run's first attempt would
  match the same server's second claim of that run after a retry from the log, and record over it.
- **What the processor passes.** Times, the lease, the back-off and how long mail is kept come from a clock, the
  settings and the retry policy.
- **Errors.** The columns hold a code of 64 and a message of 1,000 characters for a run, and one text of 1,000 for a
  mail. Error texts come from exceptions and remote servers.
- **A retry from the deliveries log** (section 4.4): what it resets, and from which states.
- **Skipped runs.** "Stop the rest" (ADR-0007) and undo (ADR-0023) both end runs as `Skipped`; the log has to say why,
  and a retry of the run that stopped the rest has to know which runs it stopped.
- **Mail of a deleted entry.** "The entry, its values, files, notes, events, action runs and queued emails go
  together" (section 4.4), while sent mail stays in the log until its purge.

## Options

**The due list:**

1. **The oldest due runs, as they are.** Simple, but blocked runs can fill a pass, as above.
2. **The oldest due runs that the order lets through,** with the claim's own order condition. A pass then proposes
   only runs it can claim, unless another server claims or starts one in between, which the claim notices.

**The scope of the order and of "stop the rest":**

1. **All of an entry's runs,** by position and id. The cross-effects above, and a hold that passes from one batch to
   the next through runs that wait behind a stopper.
2. **All of an entry's runs by queue order, with "stop the rest" per batch.** The hold still passes from batch to batch
   through the runs that wait behind an undecided stopper.
3. **The batch:** the runs one change queued for one entry run in their order, and a run that stops the rest stops the
   rest of its batch. Batches of an entry run independently.

**The token:**

1. **The caller's,** with a contract that it is new for every claim. Nothing would catch a caller that reuses one.
2. **The repository's:** each claim writes the claimant's name and 128 new random bits, and returns them.

**The back-off:**

1. **The repository knows the policy** (five attempts, after 1, 3, 9 and 27 minutes). Then a change of policy is a
   change of the persistence layer, and tests of the policy need a database.
2. **The processor decides;** the repository records "again after this delay" or "failed for good". The attempts the
   decision rests on cannot change meanwhile: only the claim's holder changes the row, and a new claim needs `Pending`.

**Error texts that do not fit:**

1. **Refuse them,** as ADR-0036 refuses what a column cannot hold. The outcome of an attempt would then be lost over its
   message.
2. **Cut the message** to the column, never inside a surrogate pair, and refuse only codes that are not codes. A code
   is the caller's constant; a message is data.

**A retry from the log:**

1. **One more attempt,** keeping the attempts. A run that failed five times fails for good after one more try.
2. **A new series:** the attempts start over at 0, so a run whose receiver was down gets its retries with back-off
   again.

## Decision

Option 2 for the due list, the token, the back-off, error texts and the retry from the log; option 3 for the scope.

**For both tables:**

- **Every state change is one conditional statement** that names the row and the state it must be in, and for an
  attempt's outcome its token. A change that finds the row in another state, or held by another token, changes nothing
  and returns false, so the late finisher of a run that was retried meanwhile records nothing. No state change reads
  first and then writes: a claim reads its row back after the write. A second write in the same unit (stopping the rest, bringing
  it back) names the state the first one left in its own condition, so each statement guards itself.
- **The time** of every statement is `FormBuilderDatabase.UtcNow`, the server's `TimeProvider`; the servers' clocks
  have to agree to well within a lease. The caller passes durations: the wait of a new batch, the lease, the delay
  before the next attempt and how long a mail is kept.
- **The token** is made by the claim: the claimant's name (8.1 and 8.2: the server and its worker, 1 to 200 characters
  without control characters), a slash and 32 hex digits of new random bits. The claim returns it in `LockedBy`, and
  the outcomes take it back. No two claims share one.
- **Errors** are a `DeliveryError`: a code of 1 to 64 characters of `a-z`, `0-9`, `_`, `.` and `-`, refused otherwise,
  and an optional message, which the caller sanitises (8.1) and the repository cuts to its column. A mail stores
  `code: message` in its one column and reads it back the same way. A success clears the error of earlier attempts;
  a retry from the log keeps it until the next attempt records its own.
- **The due list** holds the ids of up to `take` (1 to 500) due rows, the longest due first.
- **A lease that ran out** turns `Running` (`Sending`) into `Uncertain` with the code `lease_expired` and keeps the
  token, so the late finisher can still record its result. Nothing claims an `Uncertain` row.
- **A retry from the log** names the state the user saw, `Failed` or `Uncertain`, and puts the row back as due now with
  its attempts at 0, its token and lease cleared. A row in another state by then is a `FormBuilderConcurrencyException`
  (409), as an entry's state change is (ADR-0038). That an `Uncertain` row needs a confirmation first is the API's
  (8.1).

**Action runs:**

- **A batch** is what one change queues for one entry, and the change's key is its name: the submission's (or the
  confirmation's, which queues the `submitted` and `verified` actions together), a user's state change (the key undo
  uses, ADR-0023; a bulk change shares it over its entries), a rule's or an action's. `AddAsync` takes the key for the
  whole batch, never empty, and writes it on every run. A change queues one batch for an entry: a second `AddAsync`
  with a key the entry already has is a `FormBuilderConflictException` (`DuplicateKey`), checked after the hold, which
  keeps every other batch of the entry out until the unit ends. The column stays nullable, as the frozen schema has it
  (ADR-0033); a run without a key, which only a hand-written row can be, belongs to no batch.
- **Added** in the submission's unit, after the entry's row is held (ADR-0038): `Pending`, attempts 0, due after the
  batch's wait (none, or the undo window of ADR-0023), inserted by position so the ids follow the order. Null when the
  entry does not exist. The trigger is stored as its definition name (`submitted`, `approved`, `rejected`, `verified`).
- **The order:** an earlier run of the same batch, by `(seq, id)`, holds a run back while it is `Running`, while it is
  `Pending` with no attempt yet, or while it stops the rest and is not final (`Succeeded`, `Failed`, `Skipped`). So a
  retrying run and an `Uncertain` one do not hold the later ones back, unless they stop the rest. The id breaks the
  tie of two runs at one position. Runs of other batches, of the same entry or not, never hold a run back. A retry from
  the log starts a run's series over, so until its new first attempt it holds the later runs of its batch back again.
- **The due list and the claim** use that same condition, as a locking read on SQL Server. The claim is one `UPDATE`
  by id, `Pending`, due, and not held back, that writes `Running`, a new token, the lease's end, one more attempt and
  the first attempt's start; it returns the run, or null.
- **The outcomes,** each for a run that is `Running` or `Uncertain` with the token: succeeded; again after a delay
  (`Pending` with the error, token and lease cleared); failed for good (`Failed`); cut off (`Uncertain`, token kept).
  A final state sets the end, clears the due time and keeps the token of the attempt that reached it, so the log can
  tell which server that was.
- **Stopping the rest:** a run that fails for good and stops the rest turns the later `Pending` runs of its batch into
  `Skipped` with the code `stopped`, in the same unit, also when it fails late after its lease ran out. Inside a batch
  an undecided stopper holds back everything after it, so the stopped runs after a failed stopper are the ones it
  stopped. A retry of that run from the log brings exactly those back as `Pending`, in its unit, so they wait for it
  again; runs skipped for another reason, and runs of other batches, stay skipped.
- **Undo** (ADR-0023) turns a change's runs that are still `Pending` without an attempt into `Skipped` with the code
  `undone`, and says how many. Inside the undo window every run of the change waits, so all of them are undone; a
  retry of a stopper never brings back what undo skipped.

**Mail:**

- **Added** as `Queued` and due at once, after the entry's row is held when the mail belongs to an entry (null when that
  entry does not exist). Addresses, the culture and the recipient key carry no control characters, so no header can be
  smuggled in through them; line breaks in the subject are replaced when the mail is sent (ADR-0007, 8.2). The
  attachments are a list of file keys, stored as a JSON array.
- **The claim** is one `UPDATE` by id, `Queued` and due, with a new token. Mail has no order.
- **The outcomes** are those of a run, with `Sent` for success. Every final or uncertain state sets the purge time to
  now plus the time the caller keeps mail (section 4.5); a retry, by the late finisher or from the log, clears it.
  `Sent` and `Limited` mail is never retried.
- **The purge** deletes up to `take` mails whose purge time has passed, the earliest purge time first. The delete names
  the purge time again for the row itself, because under read-committed snapshot its subquery can read an older version
  of a mail that a retry has just put back.
- **Deleting an entry** first deletes its mail that is not `Sent` (section 4.4), in the unit that then deletes the
  entry, and holds the entry's row before it does, so no mail can be queued for the entry until that unit ends. Sent
  mail stays in the log, and the foreign key then clears its link to the entry.
- **The limits for visitor mail** (ADR-0012) come with 8.2: one `INSERT ... SELECT` on the recipient key that stores a
  mail over a limit as `Limited`, added to this repository. Until then nothing stores `Limited`, and nothing claims it.

**Amended in 4.8b** (the Phase 4 review):

- **The due list reads without locks.** It checks the earlier runs of a batch as the claim does, but holds no lock
  while it reads: it only proposes, and the claim checks again under its locks and decides. Before, it took the
  claim's locking read on SQL Server, so the passes of two servers took turns for it and held range locks over the
  runs they read until the read ended. This replaces "as a locking read" for the due list above, and the consequence
  of the two servers' turns below; a claim can still meet a failure's skip in a deadlock, and is then rerun.
- The position column `seq` is `sortOrder` since 4.8a (ADR-0033).

**Amended in 26.1** (the workflow on SQL Server): the two-processor test met SQL Server's deadlock victim hundreds of
times in one run. The claim's check of the earlier runs took `UPDLOCK, HOLDLOCK` and named the batch by its entry and
change key, so SQL Server read the entry's runs on `IX_jfbActionRun_entryId_sortOrder` up to the last one, the later runs
of the batch included, with key-range locks that also take the key after the range. Two servers claiming two runs of
one batch each held its own run and read the other's: a deadlock. The due list, a plain read, took shared locks on
`IX_jfbActionRun_state_nextAttemptUtc` and the rows behind it, which the claims update in the other order.

- **The check reads the earlier runs only.** It names the entry and a position no later than the run's own first
  (`p.sortOrder <= sortOrder`), so the seek stops at the run itself; the order within a position is still decided by
  the id, as before.
- **The claim locks row by row:** `WITH (UPDLOCK, ROWLOCK)` (`SqlNames.QueueCheck`), update locks on the rows read,
  held until the claim commits, without key ranges: a batch is inserted whole, once, so no earlier run can appear, and
  the next key is another claimant's run. A row that does not hold the run back may change meanwhile; none of those
  changes makes it hold the run back for a run that may start already.
- **The due list skips what other units hold:** `WITH (READPAST, READCOMMITTEDLOCK)` (`SqlNames.QueueRead`), as SQL
  Server documents for queue tables; a run being claimed is no candidate, and the claim checks again under its
  locks. `READCOMMITTEDLOCK` is there for a database with `READ_COMMITTED_SNAPSHOT` on, Azure SQL's default, where a
  read sees row versions and SQL Server does not take `READPAST` alone.
- **The checks seek, whatever the table's size.** With the three changes above the two-processor test passed, and
  then a pass of 25 runs left one queued after three deadlocks in a row. The server's own record
  (`scripts/sqlserver-deadlocks.mjs`, which a SQL Server job of the workflow now prints) showed 1,844 deadlocks in
  that one job, every one read in full on `PK_jfbActionRun`: two or three claims each holding an update lock on one
  key of the primary key and waiting for one on another. The table is small in a test, as it is on most sites, so SQL
  Server scanned its primary key for the claim's check instead of seeking the entry's runs, and a scan with update
  locks waits for every row another claim holds. The claim's check (`SqlNames.QueueCheck`) and the check before a
  batch is queued (`SqlNames.LockingSeek`) now name the index and ask for a seek:
  `INDEX(IX_jfbActionRun_entryId_sortOrder), FORCESEEK`. They then lock the entry's own runs and no other's.
- **Measured on the workflow's SQL Server** (this machine runs none): the same job met 1,844 deadlocks before the
  seeks and 12 after, and ran in 772 s where it had taken 1,048 s. The 12 are of three kinds, each tried again:
  two claims of two runs of one batch, one holding its own row in the primary key and wanting the index's key, the
  other the reverse (5); a list of an entry's runs, which reads with shared locks, against the update that skips
  the rest of a batch (3 and 1); the others were not read. The first kind would go with an index that holds every
  column the check reads, which is a migration; the second with a list that reads row versions. Neither is done:
  both are rare, lose nothing, and need a SQL Server to measure on (`docs/OWNER-TODO.md`).
- SQLite takes neither hint, and the statements mean what they meant.

## Consequences

- 8.1 and 8.2 build their processors on these calls and own the policies: the attempts, the back-off, the lease of 5
  minutes, the pause switch and the retention setting. The two-processor test of 8.1 runs against these statements.
- Section 4.4 and ADR-0007 speak of the runs of an entry; their order and "stop the rest" hold within a batch, as
  above. An approval's runs can run while the submission's are still retrying, and a stopper that stays `Uncertain`
  holds back only its own batch.
- On SQL Server the due list takes the claim's locking read, so two servers reading the due runs of one batch at the
  same moment take turns for that read, and a due list or a claim can meet a failure's skip in a deadlock. The losing
  unit is rerun as a lock conflict (ADR-0036); the due list only proposes, and the claim decides.
- A run that stops the rest and ends `Uncertain` holds the later runs of its batch until someone retries it; the
  deliveries log (11.x) shows such runs.
- Deleting an entry loses the record of its unsent mail, which is what "deleting is permanent" asks.
- **Revisit** if the deliveries log needs to skip an `Uncertain` run without retrying it, if a pass of 25 finds its
  due list taken by other servers often enough to matter, or if editors need the actions of a later event to wait for
  an earlier event's actions.
