# 0131. Keeping "used on" current

- Status: accepted; extends [ADR-0017](0017-references.md) (how the rebuild runs, and what the endpoint answers)
- Date: 2026-10-31
- Roadmap step: 6.7c (decided and built), amended by 6.8f (what asks for a rebuild, and how the walk writes); the health
  check's rebuild action (9.1), the Info view and the trash and delete confirmations (9.2) use it

## Context

ADR-0017 chose an index of our own, `jfbReference`, filled from Umbraco's reference walk by content handlers, with a
full rebuild "in the package migration that creates the table" and on the health check. Building it raised three
questions ADR-0017 left open:

- **Where the rebuild runs.** The table came with schema v1 (step 1), which has shipped to every test and demo
  database and is never edited. A walk over all content needs Umbraco's content service and property editors, which a
  migration should not call (step 3 writes Umbraco's tables with SQL for the same reason), and on a large site it
  takes longer than a start-up should wait.
- **How long one piece of it may take.** Umbraco's runner of distributed jobs holds every other job of the server while
  one runs (ADR-0007): a job returns within ten seconds.
- **What a page outside the user's start nodes looks like,** and what the endpoint says about it.

## Options

- **Where:** (a) the migration walks the content itself; (b) the migration asks for a rebuild and a job does it;
  (c) a start-up handler walks the content once the migrations ran. (a) calls services inside a migration and holds
  the migration's transaction for the whole walk; (c) slows every start-up after an upgrade and runs on every server
  of a cluster. (b) is chosen.
- **How it walks:** (a) Umbraco's paged content queries by page number, where a page created or deleted meanwhile
  shifts the pages and the walk skips an item; (b) document ids after the last one done, read from `umbracoNode`
  (its `id`, `uniqueId` and `nodeObjectType` columns, unchanged since Umbraco 7), then the items through the content
  service. (b) is chosen: it never skips, and its place is one number.
- **Rows of content that has gone:** a delete while the package was disabled leaves rows behind. One statement at the
  end of the walk deletes the rows whose key is no document in `umbracoNode`.

## Decision

- **The request:** a `jfbKeyValue` row `references-rebuild` whose value is where the walk goes on: `0` from the first
  document, the last document id done, or `orphans` once every document is done. Step 4 of the plan,
  `jfb-004-request-reference-rebuild` (`RequestReferenceRebuildV1`), adds it with `0` where it is missing, so a
  fresh install, an upgrade and a replay of the plan ask for one, and a walk under way goes on where it was. A request
  made later, by the handler below or the health check's action (9.1), writes a value of its own (`0-` and a new
  key), which reads as no place, so the walk starts from the first document and can tell the request from its own.
- **What asks for one** (`FormReferenceTypeHandler`, 6.8f): stored values can change, or come to name a form, with no
  content notification. So a document or element type changed or deleted asks, as removing a picker property or giving
  a property the picker's data type does; a data type deleted, which takes its properties away; and a data type given
  another editor; and a language deleted, which takes its values away. A type's change is any but a new type's: Umbraco counts a property's data type as a change of the
  type's "other" details, so narrowing it to structural changes would miss it. A new type or data type has no content,
  and a data type's other settings leave what its values name as it was, so neither asks. Many changes in a row cost
  one walk, as each only sets the row. A request that fails is logged as a warning and never fails the change; the
  health check's rebuild repairs it.
- **The walk** (`FormReferenceRebuild`): batches of 100 documents, the recycle bin's included and neither blueprints
  nor media, each item's rows written anew as a save writes them. Then the rows of content that has gone are deleted
  and the request removed. Content saved meanwhile is kept current by the handlers.
- **A page is written only while its copy is current** (6.8f). A batch reads the documents' current versions from
  `umbracoContentVersion` (its `id`, `nodeId`, `versionDate` and `current` columns, unchanged since Umbraco 8) before
  it reads their copies. A save moves a version's date and a publish starts a new version, so a page whose copy is not
  of the version read, or whose version has changed by the time its rows are written, is skipped: a save's handler
  writes those rows. The write unit reads the version's row with a locking read first, so a save either shows there
  or waits for the unit and writes its own rows after it. The unit holds that row, and on SQL Server the key range its
  read takes, for one item's few statements; should it ever meet a save in a deadlock it is the unit that runs again.
  Rows alone could not tell, since a save that removes the last pick leaves none.
- **One document cannot stop the walk** (6.8g): a document whose rows cannot be worked out, as after a data type got an
  editor its values do not suit, is logged as a warning, keeps its rows and is passed by. A batch that cannot be read
  at all is an error the next run meets again, as the whole site is then in trouble.
- **The job holds a request just made for one run** (6.8g), turning its value into `0`, so a walk starts at least a
  minute after a type's change. On another server of a cluster the copies still hold the old type's properties until
  that server has heard of the change, with the same version, and no save would repair what the walk wrote from them.
- **The place moves on only from the value read** (6.8f): after a batch the value is replaced only if it is still the
  one read, and the request is removed only if it is still `orphans`; a value it cannot read starts the walk again. A
  request made meanwhile stands, and the next round starts from it.
- **The job** (`FormReferenceRebuildJob`, `Jiron.FormBuilder.References`): a distributed job every minute, so one
  server of a cluster walks at a time, within 8 seconds a run, only while Umbraco runs and no migration is pending.
  With nothing requested a run reads one row. A failure is logged as an error and the next run goes on.
- **The handlers** run for content saved, published, unpublished, moved to the recycle bin, moved (a restore is a
  move), copied and deleted, once Umbraco's unit has completed. A write that fails is logged as a warning and never
  fails the editor's save; the rebuild repairs it.
- **One item's write** reads the row of each form it names first, with a locking read (ADR-0038) and in key order,
  then replaces the item's rows. A form deleted meanwhile gets no row (ADR-0047); a form's delete takes the form's row
  before its references, so the two units wait for each other in one order and never each for the other. The
  rebuild's last statement reads `umbracoNode` without a lock: a page's rows are written after its node is committed,
  so a plain read sees every node a row names, and a lock would hold Umbraco's own saves (`SqlGuardTests` lists it).
- **The endpoint** `GET form/{key}/referenced-by?skip&take` needs to see the form. It answers `total` (the pages the
  user may see), `items` (a page of them by name, each shaped like Umbraco's document reference with
  `$type: "DocumentReferenceResponseModel"`, its id, name, published state, document type and variants, from
  Umbraco when asked) and `notShown` (how many more pages use the form outside the user's content start nodes, by
  Umbraco's own path check). A page the index names that Umbraco no longer holds is left out.

## Consequences

- Right after an install or an upgrade the list can lack pages for a minute or so on a large site, until the job has
  walked them; pages saved meanwhile are there at once.
- The rebuild reads `umbracoNode` and `umbracoContentVersion` directly. A change of those columns in a later Umbraco
  shows in `ReferenceRepositoryTests`, which walk real documents, media and a blueprint, and save and publish them.
- Saving any document type starts a walk, which on a large site takes minutes in the background at 8 seconds a minute.
  Changing types is rare, and a wrong "used on" costs more: an editor would trust it when deleting a form.
- In a cluster, a server whose cache has not yet heard of a plain save, for the few seconds until it does, could read
  the older copy of the same version. The walk would then write that copy's rows over the handler's. This needs a walk
  and a save of that page within those seconds on different servers; the next save or rebuild repairs it. A publish
  does not have the gap, as its copy's version differs.
- What is not covered: a form named in a Razor view, a controller or a template, in a member's or a media item's
  properties, or outside Umbraco. The UI says "used on pages".
- The rows keep the property and the culture; the "Used on" sidebar's "where on each page" (Phase 9) reads them,
  and the endpoint can add them to its items without changing their shape.
