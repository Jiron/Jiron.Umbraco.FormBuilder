# 0145. Downloads and archives

- Status: accepted; builds on [ADR-0006](0006-file-storage-and-uploads.md), [ADR-0013](0013-permissions.md) and
  [ADR-0144](0144-cleanup-and-the-scanner.md)
- Date: 2026-11-01
- Roadmap step: 7.3c

## Context

ADR-0006 settled that files go out only through the management API, as attachments, with permission checks. Section 4.10 adds that
ZIP exports stay safe against path traversal, and that no SVG or HTML is ever rendered inline. Open were where a download hangs in
the API, what a sensitive field's file needs, what happens to a file a scanner has not answered about, what the names in a ZIP may
be, and how a ZIP streams through a server that refuses synchronous writes.

## Decision

- **A file:** `GET form/{key}/files/{fileKey}` needs `viewEntries` on the form. It answers only for a file an entry of that form
  holds; an upload still waiting for an entry, another form's file and bytes that are gone are the same `404 file_not_found`.
- **Every answer is an attachment,** whatever the stored type says: `Content-Disposition: attachment` with the cleaned name,
  `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`, and `Content-Security-Policy: sandbox; default-src 'none'`, so that
  even a page opened by hand runs nothing. A site that unblocks `svg` or `html` changes what it takes, never how it is served.
- **What a scanner says holds for everyone,** administrators too: a quarantined file is `409 file_quarantined`, and one that waits for
  a site's scanner `409 file_not_scanned`. On a site with no scanner, nothing can answer for a `pending` file, so it is served.
- **A sensitive field's file** also needs `viewSensitive`, or the answer names that permission (`403 forbidden`). Its read is audited
  (`entryFile.sensitiveDownloaded`, with the file's, the form's and the field's key and never a name), as ADR-0013 asks of an unmasked
  read. A field inside a sensitive one counts as sensitive, and so does one the catalogue does not know.
- **The archive:** `GET form/{key}/files/archive` needs `export` and streams a ZIP of the files of the entries that count: not Spam,
  drafts or test entries. It leaves out what a file's own request would not give this user, and audits the counts
  (`entryFile.archived`: `files`, `leftOut`, and whether sensitive files were open to them). It reads a page of 250 at a time, so
  submissions wait behind one page at most. Filters on the archive belong to the exports of Phase 11.
- **Names in a ZIP** (`FileArchiveNames`): `entry-<id>/<name>`, with a number on a name its folder holds already, also in other
  letters. A stored name is cleaned at upload, and this does not count on that: it cleans again to the last path segment, replaces what
  any system refuses or reads as a drive or a stream (`< > : " | ? * /`), trims points and spaces at both ends (so a dot file cannot
  hide), and prefixes the names Windows keeps for devices (`CON`, `NUL`, `COM1` and so on, with any extension). What is left empty is
  `file`. A test unpacks the worst names with .NET's own unpacker and finds every file inside its folder.
- **Held writes:** .NET's ZIP writer writes some small records synchronously even from its asynchronous calls, which a request refuses.
  `HeldWritesStream` keeps those in memory until the next asynchronous write, in their place. The files themselves stream, so a large
  archive stays small in memory, and nothing blocks a thread. Allowing synchronous writes for the request would have blocked them.
- **Written by a result, not the action:** the archive is an `ActionResult`, so the result filters run before its first byte. Writing
  from the action made a filter touch headers after the response had started.

## Consequences

- The screens come later: 10.x links a file on an entry through the API with the user's token, and 11.x offers the archive with its
  filters. Both find the permissions and the refusals here.
- An archive that fails midway ends the response: a client sees a broken download, as nothing can be said once the first byte is out.
  Its audit entry is written last, so a failed one leaves none.
- `Range` requests are not honoured, since a file is read whole by the people who download it, and each read of a sensitive file
  should be one audit entry.

## Alternatives considered

- **Buffering the ZIP to a temp file:** simple, and it copies visitors' files to a place nothing cleans on a crash. Rejected.
- **`AllowSynchronousIO` for this request:** two lines, and a blocked thread per record of a large archive. Rejected.
- **Leaving a quarantined file to administrators:** the point of a quarantine is that nobody on this site opens it by accident. A site
  that needs one has its bytes in the store.
