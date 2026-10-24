# 0041. The file store, the audit log and daily statistics

- Status: accepted; amended in 4.4a (`Files` is bound as part of the host options tree,
  [ADR-0042](0042-host-configuration-and-start-up-checks.md))
- Date: 2026-10-24
- Roadmap step: 4.3f (decided and built); 4.5 (deleting a form), 6.1 (uploads), 10.x, 12.x and 13.x (the actions the
  log records), 11.x (insights), 12.4b (the log's viewer), 13.1 (retention), 15.2 (the blob sample)

## Context

[ADR-0006](0006-file-storage-and-uploads.md) decided the file store's contract: `SaveAsync(id, stream)` stores all of
it or nothing, `OpenReadAsync(id)` returns a stream or null, `DeleteAsync(id)`; ids are 128 random bits as 32 lowercase
hex characters; the physical default writes `<id>.partial` and renames it when complete, and refuses to start when its
root lies inside the web root. Section 3.3 ports the audit log and daily statistics of the owner's chat package: audit
details built only from values that cannot carry content, entries written in the unit of the change they record, and
counters that stay right when several servers count at once. Section 4.5 fixes the tables, which the frozen schema
(ADR-0033) already has. Step 4.3f builds all three. These questions are open:

- **Where the root may be.** "Not inside the web root" needs a rule for what counts, which forms the setting takes, and
  when the check runs.
- **Failures the store cannot clean up after.** "Deletes the partial file on any failure" must not delete the partial
  file of another save of the same id. A process that dies during a save runs no clean-up at all.
- **No field values in audit details.** The identifier rule (letters, digits, `.`, `_`, `-`, `[`, `]`) refuses a space,
  `@`, a comma, a plus sign and a line break, so a full name, an address, a phone number or a message never fits. One
  word does: a first name, a postcode, a date or an answer such as `yes` is a valid identifier.
- **The log's own columns.** The action and the entity type are text too, and a combination of flags prints as
  `Read, Write`.
- **Counters and the frozen schema.** `jfbDailyStat` names its form without a foreign key, so a late increment could
  bring back the statistics of a deleted form. Its key includes the metric and the dimension, which SQL Server compares
  by the database's collation (case-insensitive by default) and SQLite by code units. On SQL Server, units that lock
  a form's row and its values in different orders can deadlock, and one statement that locks more than about 5,000
  rows locks the whole table instead.
- **Retention in batches** (13.1): the log keeps 365 days and statistics 3 years (ADR-0009), and the job deletes in
  batches.

## Options

**The root setting:**

1. **Any path,** relative paths resolved against the working directory. Where the files land would then depend on how
   the site was started.
2. **`~/` and a path relative to the content root** (as Umbraco's own `~/umbraco/Data`), **or an absolute path,** for
   example a shared folder of a load-balanced site. Anything else is refused.

**The web-root rule:**

1. **Refuse a root inside the web root.**
2. **Also refuse a root that contains the web root,** such as the content root itself or a drive. The two-character
   shard folders could not be served from there, but such a root is a mistake, and it would scatter folders among the
   site's own files.
3. **Option 2, with the paths compared both as written and with their links followed,** since a junction or a
   symbolic link can carry a root that looks outside into the web root, and the web serves a folder the web root holds
   as written wherever its links lead.

**When the rule is checked:**

1. **At the first use.** The site starts, and the first upload fails.
2. **At start-up,** as a validation of the options that runs on start: the site does not start, and the log says why.

**Keeping field values out of the details:**

1. **The identifier rule alone.** It refuses most values but not a single word.
2. **Keys for everything about an entry:** the details take keys and lists of keys, and actions about entries name
   fields, files and entries only by key, with numbers, flags, enum values and times; never a string read from an entry.
   Each step that records such an action tests that none of the entry's values appear anywhere in the log.
3. **A list of allowed identifiers per action.** A settings save names the paths it changed, which grow with the
   settings.

**The increment:**

1. **Update, insert on the day's first increment, and run the whole unit again on a key violation** (the chat package's
   way). A nested unit joins its caller's transaction (ADR-0036), so the repository cannot rerun only its own part.
2. **Update; when that changed nothing, insert only where the form exists and the row does not, both as locking reads;
   when that inserted nothing, update again.** Each statement guards itself, but the form's lock comes in the middle of
   a batch: two units counting for one form, or one counting and one deleting the form, can wait for each other in a
   circle.
3. **Hold the form's row first,** with an update that changes nothing as the unit's first write, then update each
   count, or insert it when the update changed nothing. Every unit that writes a form's values locks the form before
   them, as the form's delete does, so units of one form take turns and none waits for the form while holding a value.

## Decision

Option 2 in each case, except option 3 for the web-root rule and for the increment.

**The file store** (`Jiron.Umbraco.FormBuilder.Core.Files`, public, an extension point of section 4.9):

- **`IFormBuilderFileStore`:**
  - Every method refuses an id that is not 32 lowercase hex characters, so an id can never name a path.
  - `SaveAsync(id, content)` reads the stream from its position to its end and leaves it open: the caller owns it. It
    stores all of it, or nothing when anything fails: the stream, the disk or the cancellation token. The exception then
    reaches the caller.
  - A save never replaces a file. An id that is stored already, or that another save is writing, is an `IOException`,
    and the other file stays as it is.
  - `OpenReadAsync(id)` returns a stream from the first byte, which the caller disposes, or null when nothing is stored.
    It never returns a file that a save is still writing.
  - `DeleteAsync(id)` deletes the file, and the id reads as missing at once; an id with nothing stored is no error.
- **`FormBuilderFileId`:**
  - `New()` makes an id from 128 new random bits;
  - `IsValid(id)` is the rule above.
- **The physical store** is the default:
  - **The root** is `JironFormBuilder:Files:Root` (`FormBuilderFilesOptions`, part of the host options tree since
    4.4a, default `~/umbraco/Data/JironFormBuilder/Uploads`). `~/` is the content root, as Umbraco maps it, and the
    path after it is relative to it (`..` may lead out); otherwise the path must be absolute. On Windows, device paths (`\\?\`, `\\.\`,
    `\??\`, also written with slashes) and a colon after the drive, which names a stream of a file, are refused.
  - **The web-root rule:** the full paths are compared ignoring case, and a root inside the web root or containing it is
    refused, also when `..` leads there. They are compared as written and again with the links among their existing
    folders followed (junctions and symbolic links, at most 32 in a row), and a link that cannot be followed is
    refused. The check cannot see through an administrative share, a drive letter made with `subst`, a short 8.3 name
    or a link inside the web root that points to the root, so the option's documentation asks for the folder's own
    path. The options are validated on start, so such a site does not start. The other settings of section 4.8's
    `Files` come with the upload steps.
  - **The site's folders** come from Web, because only ASP.NET Core's host environment knows the web root and Core does
    not reference ASP.NET Core (ADR-0001); Umbraco's own path mapping in Core is obsolete in 17. Without a configured
    web root, the web root is `wwwroot` under the content root, as for ASP.NET Core and Umbraco.
  - **The layout** is `<root>/<first two characters>/<id>`, without an extension. The shard folders stay when they
    empty.
  - **A save** creates `<id>.partial` as a new file that no reader opens, copies the stream asynchronously into it
    without a second buffer, flushes it to the disk, closes it and renames it to `<id>` without replacing anything. The
    flush (`RandomAccess.FlushToDisk`) is the one blocking call, as the system has no asynchronous one; without it a
    crash could leave a file under its id that misses bytes. On any failure the save deletes the partial file, but only
    one it created itself, so a second save of the same id fails without touching the first; a partial file it cannot
    delete is logged without its path, and the caller hears of the first failure. A rename within a folder is atomic,
    so a reader sees the whole file or none. On a Linux file system without hard links (some network mounts), .NET's
    rename without replacing is a check and then a rename, so two saves of one id could still meet there; ids are
    random, so only a caller's bug could cause that.
  - **Reads** share the file for reading, renaming and deleting, so a delete during a download succeeds and the
    download keeps reading.
  - **A delete** renames `<id>` to `<id>.deleted-<16 random hex characters>` in its shard, then deletes that. The id
    reads as missing at once, also where the system keeps a deleted file that a download holds open under its name
    until the download ends. Bytes it cannot delete (a read-only file) are logged without the path and left for the
    sweep.
- **What a dead process leaves.** A process that dies during a save leaves a `.partial` file, and one that dies during a
  delete a `.deleted-` file. One that dies between the save and the file's row (6.1 saves first, because the row holds
  the size and hash) leaves a file that no row names. None is ever served, since a download opens only `<id>` and only
  for an id that a row names. Retention (13.1) sweeps all three after a day.

**The audit log:**

- **`AuditDetails`** (ported) takes only:
  - keys, and lists of keys;
  - numbers and flags;
  - enum values in camelCase, one defined value each (a combination of flags is refused: record it as a number);
  - UTC times;
  - identifiers matching `^[A-Za-z0-9_.\[\]-]{1,200}$`, at most 200 in a list.

  Its names are identifiers too. It has no way to add any other text, and an empty set of details is stored as `{}`
  (the column is not null). A number, a time or a single word could still hold what a visitor typed, so the rule
  below is what keeps values out, and a test lists every method and field of the type, so a new one gets reviewed.
- **Field values:** details about an entry name fields, files and the entry itself by key, never by alias or value.
  Each step that records an action about entries (10.x, 12.x, 13.x) tests that none of the entry's values appear
  anywhere in the log; a helper in the integration tests does the search. It leaves keys out, so digits never match
  inside one, and finds a value only as written, ignoring case, which is why the rule above stays the guard.
- **`IAuditRepository`:**
  - **Adding** joins the caller's unit, so an entry commits or rolls back with its change. The time is
    `FormBuilderDatabase.UtcNow`, and the entry comes back as stored, read again, since SQL Server rounds the time to
    about 3 ms.
    - The action is `entity.verb` (two camelCase words joined by a dot, at most 64 characters).
    - The entity type is one camelCase word.
    - `UserKey` is null for Jiron Form Builder's own actions.
    - SQL Server compares actions and entity types ignoring case, so none may differ from another in case only.
  - **Once per window:** `AddOnceAsync` adds an entry unless the same user recorded the same action on the same item
    within the window. It is one `INSERT ... SELECT ... WHERE NOT EXISTS` with a locking read, so two servers record one
    view between them. 10.x records views of entries this way, every 15 minutes at most. A window lasts from 1 second,
    well above the rounding of stored times, to 24 hours.
  - **Reading:** one entry by id, or a page filtered by action, entity type, item, user and a time range, newest first
    by time and id, with the total. Its times lie from 1753 to 9999, the range of SQL Server's `DATETIME`.
  - **Retention:** the purge deletes up to `take` entries older than the time the caller keeps them, oldest first.
  - **No update,** because an entry never changes. **No delete by item,** because the log outlives what it names.

**Daily statistics:**

- **`IDailyStatRepository`:**
  - **Counting** adds counts to a form's values of today, the UTC day of `FormBuilderDatabase.UtcNow`, in one write unit
    that joins the caller's.
    - The unit's first write holds the form's row (an update that changes nothing). When the form does not exist, it
      returns false and writes nothing, even where values of the form are left, so a late increment never brings back
      the statistics of a deleted form.
    - The counts are merged by metric and dimension and written in their ordinal order: each an update, or an insert
      when the update changed nothing. The held row keeps every other unit of the form out in between, and only this
      repository writes the table.
    - A count of 0 writes nothing; a count is at most `int.MaxValue`, so the 200 counts of a call never add up past
      what a value holds, and a negative one is refused.
  - **Reading:** a form's values from one day to another, by day, metric and dimension; at most 366 days, from 1753.
  - **Deleting a form's values** is for the delete of 4.5, which deletes the form's row first in the same unit. It holds
    the row itself when the row is still there, reads the form's days, and deletes a day at a time, each a seek on the
    key, so no statement locks more than one form's values of one day.
  - **Retention:** deletes one form's values of the earliest day older than the days the caller keeps. The job repeats
    it until it returns 0, a form and a day at a time.
- **Names:**
  - A metric is one camelCase word of at most 64 characters (section 4.5: `views`, `pageReached` and so on).
  - A dimension is empty, or at most 128 of `A-Z`, `a-z`, `0-9`, `_`, `.`, `:` and `-`: a key, an hour, or a field
    key and an error kind.
  - Neither can hold text that a visitor typed.
  - Both compare as the column does, ignoring case on SQL Server. The callers make them from constants, keys in .NET's
    lowercase form and the error kinds of section 4.4, none of which differ only by case.

## Consequences

- 6.1 saves an upload through `IFormBuilderFileStore` while it hashes the stream, then records the row. Deleting an
  entry and retention delete the stored files once the unit that deleted the rows has committed, so a unit that rolls
  back never loses a file; a file whose delete fails is one that no row names, which the sweep below finds. 15.2's
  blob sample implements the same contract and passes the same contract tests.
- **Load-balanced sites** set `Files:Root` to an absolute path of a shared folder, or use the blob sample.
- **13.1 gains a sweep:** `.partial` and `.deleted-` files, and stored files that no `jfbEntryFile` row names, older
  than a day. The roadmap step says so.
- **4.5** deletes the form's row and then its statistics in one unit. A count that waited for the row finds no form
  and counts nothing, and so does a beacon afterwards.
- **11.x** writes its metrics with these calls and reads averages as a sum divided by a count
  (`completionSeconds` by `submissions`).
- **12.4b** shows the log through the query, and adds its user filter when it needs one.
- **Revisit** if the log must ever hold more than keys and identifiers about an entry (it must not hold values), if a
  dimension needs case to tell values apart, or if a site needs its files outside both the content root and an absolute
  path.
