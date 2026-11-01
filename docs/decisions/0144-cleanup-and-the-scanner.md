# 0144. The orphan cleanup and the file scanner

- Status: accepted; builds on [ADR-0006](0006-file-storage-and-uploads.md), [ADR-0007](0007-background-work-outbox-and-email.md)
  and [ADR-0087](0087-the-upload-endpoint.md); schema step 5 follows the freeze of [ADR-0054](0054-schema-freeze.md); the
  cleanup's timer, the order of the scans and their index changed in 7.4d by [ADR-0148](0148-what-uploads-may-hold.md)
- Date: 2026-11-01
- Roadmap step: 7.3b

## Context

Section 4.10 says the scanner hook can quarantine a file, which marks the entry, and uploads without an entry are removed after
a day. ADR-0087 stores every upload as `unscanned`. Open were when a scan runs given that a file exists before its entry, what a
quarantine does to the file and the entry, what happens while a scanner is down, and how the cleanup avoids a request still under
way.

## Decision

- **A public, small hook:** `IFormBuilderFileScanner`, through `builder.JironFormBuilder().FileScanners()`, gets a file's key, name,
  kind, size, hash and bytes and answers `Clean`, `Quarantine` or `Unavailable`. A site has none by default, and the sample of 15.x
  adds one.
- **A scan runs in the background,** never in the visitor's request: a large file on a slow scanner would hold a request that is
  also the visitor's wait, and a scanner that is down must not refuse leads. An upload on a site with a scanner starts as `pending`;
  without one it stays `unscanned`.
- **The states** stay a checked camelCase word in `jfbEntryFile.scanState`, named once in `FileScanStates`: `unscanned`, `pending`,
  `clean`, `quarantined`. ADR-0033 expected an enum here. The column's check and four constants give what it would, and an enum
  would add a mapping through the repository and its tests with nothing to catch that they do not.
- **The answers:** any scanner's quarantine decides at once. All of them clean makes the file clean. Anything else leaves it
  `pending`, and it is asked about again. A scanner that throws or takes over five minutes cannot say, and only the site's stopping
  ends a scan. A file whose bytes are gone goes back to `unscanned`, since nothing can scan it, and a warning says so.
- **Its own timer:** the job is an `IRecurringBackgroundJob`, every 30 seconds on the server that schedules. ADR-0007 measured that a
  long distributed job holds every other distributed job of the server, and a scan is long. It starts no new scan after two minutes.
  A cursor kept in the job means files a scanner cannot answer about do not keep the others waiting. It is a fairness hint and no
  shared state: another server starts from the first file, and the conditional update lets only one answer count.
- **A quarantine marks the entry:** in one unit, the state changes and, for a file an entry holds, the entry gets the timeline event
  `fileQuarantined` with the file's and the field's key. The entry stays in its state, because a file says nothing about the rest of
  it. The bytes stay until the entry goes, since a site may need them for a report, and nobody can download them (7.3c).
- **No entry takes a quarantined file.** A submission's answer that names one is read as no upload, so the field gets its error, and
  the statement that gives an entry its files refuses one, for a quarantine that came between the read and the write.
- **The cleanup** is a retention part. An upload no entry took goes 24 hours and 5 minutes after it came: the token's day and the
  longest a request that read the token may still take. The bytes go first, so one that cannot be deleted keeps its row and comes
  round again. It does not stop the others. Bytes without a row (a crash between the store and the row) cannot be found from here:
  the store has no listing, which stays 13.1's to weigh.
- **The index:** `IX_jfbEntryFile_scanState_id` in schema step 5 (`jfb-005-file-scan-index`), so a job that runs twice a minute does
  not read every file of the site. Schema v1 is frozen, so this is the first step after it. It holds the state's few values, and the
  step is idempotent.

## Consequences

- Files are `pending` until the job reaches them: up to about 30 seconds on a quiet site. A download waits for that (7.3c).
- An email's attachments (8.x) must leave out a file that is quarantined or pending; the step says so.
- Whether the scanner is running is for the settings screen's "scanner status" (§4.8); until then a scanner that is down shows as
  warnings in the log and as files that stay `pending`.
- A site that removes its scanner leaves files `pending`. They are not held back, because nothing can answer for them (7.3c).

## Alternatives considered

- **Scanning in the upload's request:** an instant answer at the field, at the cost of the visitor's wait and of refusing or
  trusting everything while a scanner is down. Rejected. A site that wants it can answer early from its own middleware.
- **A distributed job:** one server at a time for free, and every other job of that server waiting (ADR-0007). Rejected.
- **Deleting a quarantined file's bytes at once:** a false alarm could not be undone, and evidence is gone.
- **Moving the entry to Spam:** the entry's other answers may be a real lead, and Spam runs no actions on a judgement it did not make.
