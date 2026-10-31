# 0131. Keeping "used on" current

- Status: accepted; extends [ADR-0017](0017-references.md) (how the rebuild runs, and what the endpoint answers)
- Date: 2026-10-31
- Roadmap step: 6.7c (decided and built); the health check's rebuild action (9.1), the Info view and the trash and
  delete confirmations (9.2) use it

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
  fresh install, an upgrade and a replay of the plan ask for one, and a walk under way goes on where it was. The
  health check's action (9.1) asks the same way, from the first document.
- **The walk** (`FormReferenceRebuild`): batches of 100 documents, the recycle bin's included and neither blueprints
  nor media, each item's rows written anew as a save writes them; the place is stored after each batch, and a value
  it cannot read starts the walk again. Then the rows of content that has gone are deleted and the request removed.
  Content saved meanwhile is kept current by the handlers; a batch that writes an item a moment after a handler did
  writes the same rows.
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
- The rebuild reads `umbracoNode` directly. A change of those three columns in a later Umbraco shows in
  `ReferenceRepositoryTests`, which walk real documents, media and a blueprint.
- What is not covered: a form named in a Razor view, a controller or a template, in a member's or a media item's
  properties, or outside Umbraco. The UI says "used on pages".
- The rows keep the property and the culture; the "Used on" sidebar's "where on each page" (Phase 9) reads them,
  and the endpoint can add them to its items without changing their shape.
