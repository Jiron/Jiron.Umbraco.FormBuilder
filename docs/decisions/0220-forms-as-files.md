# 0220. Forms as files: the replace import and mirror mode

- Status: accepted; builds on [ADR-0046](0046-copies-templates-and-import.md) (copies, templates and import),
  [ADR-0078](0078-lists-from-an-import.md) (lists from an import), [ADR-0144](0144-cleanup-and-the-scanner.md) (jobs on
  timers of their own), [ADR-0165](0165-the-section-shell.md) (the health checks) and D25 (forms as files); changes D8,
  which counted the file mirror among the distributed jobs
- Date: 2026-11-08
- Roadmap step: 12.4, its part 12.5 (ADR-0197)

## Context

Part 12.5 asks for the import UI with "new copy" or "replace", and for mirror mode (D25): with
`JironFormBuilder:Definitions:Mirror` on, each published form is also written to a JSON file, files changed in source
control are imported as new versions when the site starts, and a change on both sides is a conflict that keeps the
database and warns in the health check. "Import a form…" creates a new draft copy with new keys (ADR-0046), which left
"replace" to this step: ADR-0046 names it as the place for forms that must keep their keys.

Open questions:

- **What replace replaces,** and what the form keeps.
- **Whose keys** a replaced or mirrored form carries.
- **What the mirror writes and when,** and what it remembers to tell its own change from a developer's.
- **What a pass does** with a changed file, a new file, a file without a change, a stale copy and a change on both
  sides; on which server.
- **How a conflict ends,** for either side.

## Decision

### Replace

1. **"Replace from a file…"** in a form's "…" (`design` on the form) opens the import sidebar for that form. The file's
   definition becomes the form's **new draft**: the form keeps its key, name, alias, folder, access, entries and
   History, and the published version stays until someone publishes. The sidebar says so in one line, with what the
   file holds and what it can't import, as for a new copy; in the form's own workspace with changes not saved yet it
   adds that those are discarded. Its primary button is **Replace the draft**. The form's workspace, wherever it is
   open, reads the new draft (Umbraco's entity-updated event), and the form's screen opens.
2. **Keys are the file's.** A copy renews them so two forms never share one (ADR-0046); a replace keeps them, so a file
   exported from this form, or from its twin on another site, keeps its answers mapped to the same fields. Themes,
   option lists and actions the site lacks are handled as for a copy (ADR-0046, ADR-0078).
3. It is a draft save at the version the sidebar read: a change meanwhile refuses it (`409 concurrency_conflict`), the
   setting locks hold as in every save, and a file that holds the draft as it is writes nothing. A replace that changes
   the draft's actions needs `manageActions` on the form, as a draft save does. It is audited as `form.replaced`, with
   the version, the file's form and its package version.

### Mirror mode

4. **Settings:** `JironFormBuilder:Definitions:Mirror` (off by default) and `Folder`
   (`~/umbraco/JironFormBuilder/forms`, under the content root, or an absolute path; one inside the web root stops the
   site from starting, as the file store's does, and only while the mirror is on). The files are the export format,
   one per form, `<alias>.json`.
5. **What the mirror remembers** is in `jfbKeyValue`: per form, under `mirror/<form key>`, the hash of the file's
   **definition** as the versions keep theirs, the published version it matched and the file's name. Definitions are
   compared, never bytes, so a file a developer reformats, or one exported at another time, is no change. What it could
   not keep in step is a marker under `mirror-problem/<kind>/…`: a conflict, a file it could not write, a file it could
   not take in (not an export, refused, or a second file of the same form), and a changed file whose publish was
   refused.
6. **On publish** a handler of the published notification writes the published version to its file, atomically (a
   temporary file, then a move), and remembers it. **A file that changed since the mirror last saw it is never
   overwritten by a publish:** it is a conflict, so a developer's change is never lost to an editor's publish; a file
   that already holds the version is only remembered; a file that holds an older published version is a stale copy and
   is written. A file it cannot write is logged and named by the health check. Publishing never fails for any of it.
7. **The pass** is `FormMirrorJob`, every minute from 20 seconds after the start, once Umbraco runs and no migration is
   pending, on the server that schedules: the folder is that server's own and the backoffice publishes there. It is a
   timer of its own, as the file scans' is (ADR-0144), not a distributed job, because one server of the cluster at
   random might hold a folder no publish writes to. For each `*.json` file:
   - it is the file as the mirror last saw it and the form was not published since: nothing happens; the form was
     published since, as while the mirror was off: the file is written again;
   - it holds the form's published version: it is remembered;
   - it holds an older published version: it is a **stale copy** and is written again, never taken in; History restores
     an older version;
   - it changed while the published version is still the one remembered: it is **taken in** — it replaces the draft,
     with the file's keys, and is published as Jiron Form Builder's own work with the comment "Published from its
     file.", and its hash and the new version are remembered without rewriting the file, so the developer's file stays
     as committed;
   - it changed and the form was published since (**both changed**): a **conflict**, which keeps the form and the
     file;
   - it names no form on the site: the form is created with the file's key, alias and name, and published; a form the
     mirror knew and that was deleted here leaves its file alone;
   - a form in the Recycle Bin, or one taken offline, leaves its file alone until it is published again.

   Then a published form without a file gets its file. Someone changing a form during the pass refuses that file's
   change, which the next pass tries again.
8. **The health check** "Forms as files" has a probe of its own (ADR-0165): off, every published form in step with its
   file, or one finding per problem, a conflict first, each naming the file and what to do. A conflict ends either way:
   **the file wins** by Replace from a file… and a publish, after which the file holds the published version and is
   only remembered; **the site wins** by the check's action on Umbraco's Health Check dashboard, "Write the files
   again", which writes the published version of each form in conflict to its file.
9. Deleting a file deletes nothing: deleting forms is the site's to do, and a file whose form is gone or in the
   Recycle Bin is left alone.

## Consequences

- A replace keeps keys, so a file from another form can bring fields whose keys match none of this form's entries'
  answers; those answers stay readable as answers to fields no longer in the form, as after any edit.
- A mirrored site's files change only when a form is published, so a draft never reaches source control.
- A developer who reverts a file to an older version's content sees it written again; restoring a version is the
  History's job, which keeps the trail.
- On a load-balanced site the files that matter are the scheduling server's; the others' folders are never read.
- **Revisit** if teams want drafts mirrored, a file's deletion to trash its form, or the pass on every server.
