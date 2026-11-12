# 0210. Entry exports

- Status: accepted
- Date: 2026-11-07
- Roadmap step: 11.4, its part 11.4 (exports; ADR-0197); builds on [ADR-0003](0003-entry-storage.md) (keyset pages of
  250, the formula guard, the 10,000-entry budget), [ADR-0207](0207-the-entries-list.md) (the list's filter and Export
  selected), [ADR-0145](0145-downloads-and-archives.md) and [ADR-0150](0150-what-a-review-found-going-out.md) (the archive of files and its audit), [ADR-0008](0008-settings-model.md)
  (`jfbUserSetting`) and [ADR-0009](0009-privacy-defaults.md) (what is kept); IA 5.4, 6.1, 6.3 and 6.4, copy.md

## Context

Step 11.4 exports a form's entries as CSV, Excel, JSON, a printable view and a ZIP of the uploaded files, from the
Entries view's Export… and the bulk bar's Export selected…, remembering the user's choices per form, in the background
for large sets, respecting the filters, the permissions and the sensitive-data rules, and audited. A survey of the code
(2026-11-07) found the `export` permission, the list's keyset query with its time budget, a streamed ZIP of a form's
files, the answer texts the summary page lists, `jfbKeyValue` for jobs' records and the file store; no export service,
no XLSX or ZIP dependency (and §4.12's rule to prefer small own code over a dependency), no time-limited backoffice
download and no extension point yet. The owner answers no questions during the run (CLAUDE.md rule 8), so each choice
below is this record's.

## Decisions

1. **One export service over the list's own query.** An export takes the Entries view's filter as the list does
   (ADR-0207) or the ticked entries of Export selected… (at most 500, as the bulk bar's other actions), never drafts
   or test entries, and reads them in keyset pages of 250 entries, each its own gated read under the query budget, so
   a submission waits behind one page at most (ADR-0003). The 10,000-entry CSV export is measured against its 5 s.
2. **Columns.** Entry (its number), the reference (every entry has one) and Received, as the visitor's receipt and
   emails gave them: in the format of the entry's own version and the time zone of the site its page belongs to; State;
   then the chosen fields in the form's order, from the catalogue, so a field only an older version had still
   exports. The order is the version the site serves, or the last one it served once the form was taken offline, never
   a draft's (`FormDocuments.ServedOfAsync`, which the entries list and the entry panel use too): a draft's changes
   decide nothing about entries that came from published versions. An answer is written as the summary page lists it, in its entry's culture: values apart by ", ". A grid
   gives one column per row ("Grid: Row"), a ranking one column per position ("Ranking: 1", the option at that place),
   and a repeatable group one cell with a line per row in CSV and the printable view, an array of rows in JSON, and a
   sheet of its own in Excel: the entry's number, "#" for the row's, and its fields.
3. **Sensitive fields are left out** unless the user holds `viewSensitive` on the form and ticks "Include sensitive
   fields"; a field inside a sensitive one counts as sensitive, and a field is sensitive when its version or the field
   catalogue marks it. The dialog offers the choice whenever the form holds a sensitive field, one inside a repeatable
   group too. Files follow the same rule, and a file held back by
   the scanner is left out, as for a download (ADR-0145).
4. **Formats.**
   - **CSV:** comma, semicolon or tab; labels or aliases as headers; a byte-order mark (on by default, so spreadsheet
     programs read UTF-8); every cell that starts with `=`, `+`, `-`, `@`, a tab or a carriage return gets a leading
     `'`, the formula guard ported from the owner's earlier package. A cell that holds any of the three delimiters is
     quoted, not only the chosen one: a spreadsheet program may split a file on its own delimiter, and `x;=1+1` left
     unquoted in a comma file would then start a cell with a formula.
   - **Excel:** a small own writer of the Office Open XML spreadsheet (a ZIP of a few XML parts, inline strings, a bold
     header row, a frozen first row), with no dependency: the format's needs here are few, and §4.12 prefers a small
     own piece over a package. Tests read the file back with a test-only reader.
   - **JSON:** an array of entries, each `number`, `reference`, `state`, `received` (ISO 8601 UTC) and `answers` by
     alias: a text, an array where a field takes several values, an object for a grid, an array in rank order for a
     ranking, an array of rows for a repeatable group.
   - **Printable view:** one HTML document without scripts, its texts escaped, with a print style sheet: each entry's
     heading and its answers as a table. The entry panel's Print opens it for one entry.
   - **ZIP of the files:** a folder per entry, named by its number, each file under a safe name (ADR-0145).
5. **Small sets stream, large ones go to the background.** Up to 10,000 entries the file streams in the response at
   once. Beyond, and for every ZIP of files, whose size has no bound, the export is queued: a record in `jfbKeyValue` holds its owner, form, request, state and progress;
   the server that queued it runs it at once on a worker of its own, page by page, into the file store, and the
   distributed job `Jiron.FormBuilder.Exports` restarts one whose server stopped and deletes a finished export's file
   and record 24 hours after it was ready. The sidebar says it continues in the background (copy.md); the page that
   started it follows its progress, waiting at the idle gate like every poll (ADR-0209), and its toast links to the
   file when it is ready. Only its owner may download it, and only until it expires. The file downloads through a plain
   link, which the browser saves as it comes, whatever its size, rather than in the page's memory; a link's request
   carries no backoffice token (Umbraco 17 keeps it in a cookie it takes only beside its own header), so the owner is
   issued a ticket for it after the checks a download needs (the owner, the file ready, `viewSensitive` still held when
   sensitive fields went along): protected by Data Protection under a purpose of its own, naming the user, the form and
   the export, for one minute. The download's endpoint is anonymous and declares its ticket
   (`FormBuilderTicketAccessAttribute`), the permission matrix's reviewed exception beside the form picker's, and a
   ticket that does not hold finds nothing. Amended by [ADR-0236](0236-what-the-security-review-changed.md): the download makes the ticket's checks again for its
   user, who must still be enabled and hold `export` (and `viewSensitive` when sensitive fields went along), and answers
   `Referrer-Policy: no-referrer`. A worker holds an export under a lease of 5 minutes that it renews after
   each page and every minute meanwhile, as a page of many files or the storing of a large file can take longer; an
   export that fails is tried three times, any failure counting, a cancellation the worker did not ask for (a file
   store's timeout) among them; when the host stops, what the worker stored goes, and another server takes the export
   over once the lease ran out. A user has at most three exports under way; two asked for at the same moment may pass
   that check together, as it reads the records before it writes one, which bounds a person's exports well enough. The endpoints live under `form/{key}/entries/export`, beside the list, as
   `form/{key}/export` is the form's own JSON (ADR-0046): the view, the plan, the file, and the background exports with
   their status and file.
6. **The dialog remembers** the format, the fields (all, or the ones chosen), the delimiter, the headers, the
   byte-order mark and the sensitive choice per user and form, in `jfbUserSetting` (`entries.export.<form key>`), and
   opens with them (IA 6.4). Its requests say so (`remember`); the entry panel's Print does not, so printing one entry
   leaves the dialog's choices as they were.
7. **Audited and announced.** Every export, streamed or in the background, writes `entries.exported` with its format,
   the number of entries and fields, whether sensitive fields went along and whether it finished; a finished export
   publishes `ExportCompletedNotification`.
8. **An extension point.** `IFormBuilderExportFormat` writes a site's own format from the export's columns and rows
   and opens the entries' files; the built-in formats are its implementations, and a site's formats follow them in the
   dialog.
9. **Where it is offered.** Export… in a form's Entries view, and in the Inbox once the Form filter chose one form;
   Export selected… in the bulk bar's "…" for ticked entries of one form; Print in the entry panel's "…" in every
   state, which prints that entry's printable view without its sensitive fields. All need `export` on the form.
10. **The sidebar and its toasts.** The export sidebar is Umbraco's sidebar: the format, one line naming the entries
    and their number from the plan, the fields ("All fields (12)", or each one), the sensitive fields' choice for a
    user who may see them, and "More options" only for a format that takes a delimiter or headers. Its primary names
    the action and the number ("Export 124 entries", "Print 1 entry"). A file that fits a download is saved at once; a
    printable view goes to the browser's print dialog through a frame that runs nothing, so no pop-up blocker stands in
    the way. A background export is followed outside the screen that started it, through Umbraco's toasts, which
    outlive it: one says how far it is, then one with Download stays until it is closed; every read of its status
    waits at the idle gate. The sidebar lists the user's exports of the form, so a page that loaded again finds them.

## Consequences

- Positive: one query for the list and its export, so an export holds exactly what the list shows.
- Positive: no new runtime dependency; the formats are small, tested, and replaceable through the extension point.
- Negative: a background export that its server stopped starts again from the beginning on another server.
- Negative: a ZIP of a few files waits for the background too, a moment longer than a download would.
- Negative: a download's ticket travels in its link's address, which a server's access log may keep; it holds for
  one minute, for one user's own file, and only while that user may still download it. It may be followed more than once
  in its minute: a ticket used once would need a store every server shares, and the minute and the checks bound it as well.
- Negative: a repeatable group in CSV is one cell with a line per row, which spreadsheet programs show in one cell.
