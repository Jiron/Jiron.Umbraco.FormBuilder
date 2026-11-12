# 0148. What uploads may hold, and who clears them

- Status: accepted; amends [ADR-0144](0144-cleanup-and-the-scanner.md) (the cleanup's timer, the order of the scans, their
  index) and [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md) (the upload policy's defaults); a post
  without JavaScript checks the share and the room before its first file, and counts what it holds while it is read, since
  16.1 ([ADR-0236](0236-what-the-security-review-changed.md))
- Date: 2026-11-01
- Roadmap step: 7.4d

## Context

Phase 7's review read the upload endpoint as someone who wants the disk. Each of its limits holds on its own, and together
they still leave four gaps:

- **Nothing caps the bytes.** A file has a limit and an address has a limit per minute, but thirty 50 MiB files a minute from
  one address add up to about 2 TiB in the 24 hours and 5 minutes before an unclaimed upload may go.
- **The cleanup can fall behind.** It was a part of the hourly retention job, with a share of that job's few seconds, while
  the site-wide limit lets in 72,000 uploads an hour.
- **Unclaimed uploads hold real entries' files back.** Scans went by id, so a flood of uploads sent before a visitor's post
  came before that post's files.
- **A file that cannot be opened stalled the queue.** The store's exception ended the run, and the next one began at the
  same file.

## Options

**The cap:**

1. **A count per address.** It has no answer for many addresses, and it adds a table.
2. **A free-space check.** A store need not be a disk, and no store interface says how much room it has.
3. **A host option capping the bytes that wait for an entry, summed from the table.** It holds across servers and stores,
   and it leaves files an entry took alone, which are the site's to keep.

**How often to read the sum:** on each upload it would cost a query that grows with what waits, which is the wrong cost in a
flood. Once in ten seconds per server, with what the server stores itself added in between, costs almost nothing.

**A post without JavaScript past the cap:**

1. **An error at each file field.** It would have to say the file is fine and the site is not, and it would repeat at each field.
2. **A notice in the summary's place,** with every answer kept, as for a post over a rate limit. The page answers `200`, as for
   the post too large to read, since a proxy may replace a page that answers with a `5xx` and take the answers with it.

**The cleanup:** a larger share of the retention job would still wait for the hour. Its own timer is what the scanner has.

**The order of the scans:**

1. **What an entry holds first, in one sorted list.** `ORDER BY` on a nullable column reads every waiting row in a flood.
2. **Two lists over one index on `(scanState, entryId, id)`.** The uploads come in order from the index, and what entries hold
   skips them: the search for those costs what they are, whatever waits behind them.

## Decision

- **`Files:MaxWaitingBytes`:** 10 GiB by default, 0 leaves it out. An upload past it stores nothing, checked after the name and
  the first bytes and before the store. `WaitingUploads` holds the count, read at most every ten seconds on each server. Reaching
  the cap logs one warning per reading.
- **The answers:** the upload endpoint answers `503 uploads_full` with `Retry-After: 300`. A post without JavaScript shows "Your
  answers weren't sent" with its answers, and asks to choose the files again. A typed signature counts as a file.
- **The upload policy's defaults** gain `PerAddressPerHour: 200` and `PerFormPerMinute: 600`.
- **An address's share:** `Files:MaxWaitingBytesPerAddress`, 1 GiB by default and 0 leaves it out, holds one address to that many bytes in
  the day an upload waits, counted on each server. Past it, the upload endpoint answers `429 rate_limited` with `Retry-After`, and a
  post without JavaScript comes back with its answers and when to send again. The review of 7.4 found that without it two addresses
  could fill the cap in about ten minutes and shut every form's uploads for a day; now it takes eleven, for the whole day.
  Amended by [ADR-0236](0236-what-the-security-review-changed.md): an IPv6 address's share is its /64's, and the /48 around it holds four shares
  together, as a whole /48 (65,536 /64 networks) is what a site is usually given, and is free from tunnel brokers.
  Past the limiter's bound of tracked clients (`Limits:MaxTrackedClients`), a new address counts in the overflow window it
  shares with others, as its requests do, and is held to that window's share; until the review of step 16.5 the check read
  only an address's own window, so an address counted in an overflow window had no share.
- **The cleanup** is `OrphanFileJob`, on the server that schedules, every minute, for at most 30 seconds. It is no longer a
  part of the retention job.
- **A scan:** what an entry holds goes first. Uploads are looked at in batches of ten, so a new entry's file waits for at most
  those. Each list goes on from where it stopped. The entries' list starts over in the run after it reached its end, which also
  finds a file an entry took behind where it stood. A file that cannot be opened stays waiting and its turn passes.
- **Migration step 6** (`jfb-006-file-scan-order`) replaces `IX_jfbEntryFile_scanState_id` with
  `IX_jfbEntryFile_scanState_entryId_id`.
- **`PhysicalFileStore` keeps its blocking flush.** .NET has no asynchronous way to flush a file to disk, and wrapping it in
  `Task.Run` would block another thread instead. It is what makes the rename safe, once per file, and the upload limits bound how
  often it runs.

## Consequences

- A flood from many addresses can still fill the cap, and the site then takes no new uploads until entries take what waits or a day has passed. That
  is the point: a form stops taking files, and the disk, the site and every entry stay safe. The warning tells the operator,
  who can raise the cap or clear what waits.
- Other servers' uploads count at the next reading, so the sum can pass the cap by what they take in ten seconds. The rate limits
  bound that, and it is why the option's description says to set it to what the disk can spare.
- A file the cleanup cannot delete keeps its place at the head of the list. Two hundred such files would stop the cleanup until the
  store recovers, but a store that refuses that many refuses all of them, and each run logs a warning per file.
- **Revisit** if a store comes to report its free space, or if a site needs a cap per form.
