# 0047. Trash, delete and folders

- Status: accepted; amends [ADR-0033](0033-schema-v1.md) (the trash key of folders and forms),
  [ADR-0036](0036-database-gate-units-of-work-and-repositories.md) (folders are created and moved only below folders
  outside the trash, and a folder in the trash does not move) and [ADR-0044](0044-form-service.md) (the folder service
  and the trash's notifications)
- Date: 2026-10-24
- Roadmap step: 4.5d (decided and built); 4.7 maps it to the API, 6.1 answers a form in the trash as missing, 6.7
  writes the references, 9.2 shows the trash, 13.1 sweeps what a failed file delete leaves

## Context

The repositories of 4.3a mark one row at a time: trashing a folder marks the folder, restoring it brings back only
the folder, and a form is deleted only once it has no entries (ADR-0036). Roadmap step 4.5d asks for more:

- **Trashing a folder takes the folders and forms in it with it, and restoring it brings back what went with it.** A
  form that was trashed on its own before must stay in the trash when its folder comes back, and so must a subfolder
  that went to the trash before its parent.
- **A permanent delete removes everything of the form:** its entries with their values, files (bytes included),
  events, notes and action runs, its versions and catalogue, its grants, the references of content to it and its daily
  statistics. References and statistics name the form without a foreign key (ADR-0033), and the delete removes the
  form's row before its statistics in one unit, so a count that waited for the row counts nothing (ADR-0041).
- **Size.** A form can have 100,000 entries with dozens of values each. One unit for all of them would hold its locks
  for minutes, and on SQL Server a statement that locks more than about 5,000 rows locks the whole table (ADR-0041).
- **Entries keep arriving** until the website learns that the form is gone, and an editor may restore the form while
  its delete runs.
- **Folders have no service yet,** and 4.7 maps "forms and folders (CRUD, trash)" onto the 4.5 services.

## Options

**What went with a folder:**

1. **The same trash time:** everything a folder's trash marks gets the folder's `trashedUtc`. Two trashes at one
   moment are told apart only by the clock: SQL Server's `DATETIME` rounds to about 3 ms, and a test clock stands
   still.
2. **Trash by ancestry:** only the folder's row is marked, and whatever is below a trashed folder counts as trashed.
   Every check of the trash (the website, submissions, the API, the repositories' own guards) would then walk up the
   tree instead of reading one column.
3. **A trash key:** each trash operation gives the rows it marks one new key, kept beside `trashedUtc`. Restoring a
   folder brings back the rows below it that carry its key.

**Deleting entries:**

1. **One unit for the whole delete.**
2. **Entries in batches,** each a unit of its own that holds the form's row while the form is in the trash at the
   version the editor confirmed, then one unit for the form's own rows.

**Notifications of a folder's contents:**

1. **One pair per form and per folder** that the folder's trash, restore or delete touches.
2. **One pair for the folder, naming everything it touches,** as Umbraco's own content tree tells of a branch.

## Decision

Option 3, option 2 and option 2. In detail:

### The trash key

- `jfbFolder` and `jfbForm` gain `trashKey` (`uniqueidentifier`, null), set together with `trashedUtc` and cleared
  with it. Schema v1 is not frozen until 4.8, so `CreateSchemaV1` takes the column; no index, because the walks below
  reach the rows through their parents.
- **Trashing a form** gives it a key of its own. **Trashing a folder** gives the folder, every folder below it that is
  outside the trash and every form in them one new key, in one unit, with walks that lock what they read.
- **Restoring a folder** returns it to its parent, or to the root when the parent is in the trash, and brings back the
  folders below it that carry its key, reached only through folders that carry it, and the forms with its key in
  them. A folder trashed with its parent and restored on its own goes to the root with what went with it below it.
  **Restoring a form** brings back only the form, to its folder or to the root.
- **Nothing lives below a trashed folder:** a folder is created and moved only below a folder outside the trash, and a
  folder in the trash does not move, each checked in the statement with a locking read (this amends ADR-0036). With
  the form repository's rules (ADR-0044) and the walks above, everything below a trashed folder is in the trash.

### Forms

The form service (ADR-0044) gains `TrashAsync`, `RestoreAsync` and `DeleteAsync`, each with the concurrency version
the caller read, so a stale one is refused with `concurrency_conflict`:

- **Trash:** a form outside the trash; one in the trash already stays as it is and raises nothing. Its published
  version stays: the website answers a trashed form as missing (6.1), and a restore brings it back as it was.
  `FormTrashing` → one unit (the row, `form.trashed`) → `FormTrashed`.
- **Restore:** `FormRestoring` → one unit (`form.restored` with the folder it went back to) → `FormRestored`, which
  names that folder. A form outside the trash stays as it is.
- **Delete permanently,** only from the trash (the new conflict `not_trashed` otherwise), after `FormDeleting`:
  1. **Entries in batches** of 100, each a unit: it holds the form's row while the form is in the trash at the
     confirmed version (otherwise the delete stops with `concurrency_conflict` and keeps what is left), reads the
     batch's ids and files, holds the entries and deletes their mail that was not sent (sent mail stays in the log
     without the link, ADR-0040), and deletes the entries, whose values, files, events, notes and action runs go by
     the cascades.
  2. **Uploads that wait for an entry** (files of the form without one), each with the delete of 4.3c.
  3. **The form's own rows** in one unit: the form with its versions, catalogue and option places (the delete refuses
     while entries are left, so a late entry sends the delete back to step 1, at most three times), then its grants,
     the references to it and its daily statistics, and `form.deleted` with the counts of entries and files.
  4. **The bytes** of the deleted files, after each unit committed (ADR-0041): a unit that rolls back never loses a
     file. A file whose bytes cannot be deleted is logged without its path and left for 13.1's sweep.
  5. `FormDeleted`.

  A delete that stops part way leaves the form in the trash with fewer entries; deleting it again finishes the job.
  An upload that arrives between the last batch of uploads and the form's own rows has no foreign key to the form
  (ADR-0033) and outlives it; the sweep of uploads that never got an entry (ADR-0006) deletes it with its bytes. 6.1
  refuses uploads to a form in the trash, so only one already in flight can do that.

### Folders

A new internal `IFormBuilderFolderService`, on the same rules (its own unit, the checks, a cancelable notification,
the unit with its audit entry, a notification after the commit):

- **Get and list** (the whole tree: a library holds few folders), **create** (in a folder outside the trash or at the
  root; name 1 to 255 characters on one line), **rename**, **move** (into a folder outside the trash or the root;
  `folder_into_itself`, `folder_too_deep`), each with `FolderCreating`/`Created`, `FolderRenaming`/`Renamed`,
  `FolderMoving`/`Moved` and `folder.created`, `folder.renamed`, `folder.moved`. Folders have no concurrency version
  (ADR-0036): the last rename wins, and a trash or a restore that has nothing to do changes nothing. A rename writes
  only the name (a new `IFolderRepository.RenameAsync`), so it never puts back a restriction read before a change
  of it; a folder in the trash is neither renamed nor moved (`trashed`), and the repository deletes a folder only
  from the trash (`not_trashed`).
- **Trash:** `FolderTrashing` names the folders and forms that will go with it, as read before the change;
  `FolderTrashed` names exactly those that went. `folder.trashed` records how many.
- **Restore:** `FolderRestoring` and `FolderRestored` likewise, with the folder it went back to; `folder.restored`.
- **Delete permanently,** only from the trash, refused with `folder_not_empty` while anything below it is outside the
  trash: `FolderDeleting` names what goes; each form is deleted as above (with its own `form.deleted`, without form
  notifications of its own), then the folders from the deepest up with their grants, each with `folder.deleted`; then
  `FolderDeleted`.
- **Empty the trash:** `TrashEmptying` names every folder and form in the trash; the folders at the top of the trash
  go as a folder delete does, then the forms in the trash outside them; `TrashEmptied` names what went.
- A handler that follows every form into the trash or out of the database listens to the folder notifications too,
  which name the forms they took.
- **The order of what they name:** the folders below a folder nearest first, then by sort order and name, and the
  forms by name; a delete's result names the folder before those below it, and emptying the trash names each tree
  in the order it goes, then the forms trashed on their own. The notification before a change and the one after
  name the same things in the same order when nothing changed in between.

### The new statements

- `IEntryRepository` gains the ids of a form's entries (a batch, lowest first), the files of a batch of entries, the
  form's uploads without an entry and the delete of a batch; `IEmailRepository` the delete of a batch's unsent mail,
  holding the entries first; `IFormRepository` the hold of a trashed form at a version, the delete of a trashed form at
  a version, the list of the forms in a set of folders, and the trash and restore of the forms in a folder's tree;
  `IFolderRepository` the rename and the trash and restore of a tree; a new `IReferenceRepository` the delete of a
  form's references (6.7 adds the rest).
- The restore's walk is a query that starts with `WITH`; NPoco puts a `SELECT` of its own before such a query, so it
  starts with a semicolon, which both databases skip.
- Every check of other rows is a locking read (ADR-0038), which `SqlGuardTests` enforces; a batch names at most 100
  ids, below the 1,000 of one statement.

## Consequences

- A folder brings back exactly what went to the trash with it, whatever the clock says.
- Deleting a large form takes as many short units as it has hundreds of entries; no unit holds a table, and the
  website keeps answering other forms meanwhile.
- 4.7 maps these operations and `not_trashed`; 6.1 refuses submissions and uploads to a form in the trash and answers
  it as missing; 6.7's reference writes skip a form that is gone; 9.2 shows the trash with these operations.
- **Revisit** if libraries grow to thousands of folders (the folder list is read whole), or if deletes should run in
  the background with progress.
