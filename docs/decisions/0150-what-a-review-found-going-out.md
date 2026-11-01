# 0150. What goes out, and to whom

- Status: accepted; amends [ADR-0013](0013-permissions.md) (who may lower a field's sensitivity),
  [ADR-0145](0145-downloads-and-archives.md) (the archive's audit), [ADR-0143](0143-what-an-uploads-bytes-and-name-may-hide.md) (a ZIP with bytes
  after it), [ADR-0067](0067-the-umbraco-sources-and-the-base-url.md) (`page.*` on a protected page) and
  [ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md) (the form endpoint of a form for members)
- Date: 2026-11-01
- Roadmap step: 7.4f

## Context

Phase 7's review read what leaves the site, and for each it asked who could get it without being meant to:

- A user with `design` and `viewEntries` could publish a field as not sensitive and read its files without `viewSensitive`.
- An archive cut off before its end left no trace in the audit, as the entry was written after the last byte.
- `page.*` merge tags read any published page a request names, those protected from the visitor among them.
- The form endpoint gave anyone the whole definition of a form for members.
- One byte after a ZIP's closing record hid it from the check of [ADR-0143](0143-what-an-uploads-bytes-and-name-may-hide.md), while readers open it anyway.
- A server key's uploads left no trace of the key.

## Decision

- **Lowering a sensitivity takes `viewSensitive`.** Publishing a draft or restoring a version that makes a field no longer sensitive
  answers `403` naming it. That counts a field's own mark and the repeatable group around it, so moving a field out of a sensitive
  group counts, and it compares with every row the catalogue holds, so a field dropped and brought back counts too. A draft may
  hold the change: designing is not reading, and a user with the permission can publish it.
- **An archive has two audit entries:** `entryFile.archiveStarted` before the first byte, whose failure sends nothing, and
  `entryFile.archived` at the end with its counts and whether it finished, written whatever the request's cancellation says.
- **A protected page is no page for `page.*`,** even for a member who may see it, as the same text also renders where no member is
  known, such as a mail. A form on such a page reads its own values through other tags.
- **The form endpoint answers `403 member_required`** for a form for members, unless a member it admits or a server key asks. A server
  signs for its own visitors and decides who they are, and the challenge endpoint still refuses it, so a server key cannot send to one.
- **A ZIP with bytes after it counts** when its record describes one archive on one disk. Without that check any file holding those
  four bytes in its last 64 KiB would be refused, about one in 65,000.
- **A server key's upload is logged** with the key's name, as its submission is recorded.

## Consequences

- A site whose pages are all behind a member login gets empty `page.*` tags. That is the safe reading of a request that names its own
  page, and a site that needs the values can place them in the form's settings.
- The audit has two entries per archive. A reader counts the `entryFile.archived` ones, and a started one without its end marks a server
  that went down.
- **Revisit** if a request's page comes to be trusted beyond the reference index ([ADR-0147](0147-the-site-that-protects-a-form.md)),
  which could tell a page that holds the form from any other.
