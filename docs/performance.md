# Performance

What Jiron Form Builder's budgets are (ROADMAP §4.12), how each is measured, and what the last measurement found, on
the machine it ran on. The numbers are a machine's: a slower one may miss a budget that this one meets, so measure on
your own hardware before you rely on them ([how](#how-to-measure)).

## The machine

Measured on 2026-11-12 in one run of the load test, server and clients on the same machine:

- AMD Ryzen 7 5700G (8 cores, 16 threads), 28 GB of memory, a Samsung 990 PRO NVMe drive, Windows 11 Pro;
- .NET SDK 10.0.401, Umbraco 17.7.1 (the measurements were taken on the 17 line), the demo site in Release with a
  fresh SQLite database;
- Node 24 for the load script, Microsoft Edge for the browser parts.

## Server

Each number is what a client waits for, from its request to the end of the answer, ASP.NET Core's pipeline included,
measured over HTTP from Node, in milliseconds. The p95 is the 95th percentile. Rendering, the sends, the list and the
tree leave their first requests out, as a site has them behind it after its first visitors: the first sends after a
start, before .NET has compiled their code, took about 290 ms each. The export and the mail count every run.

| What | Concurrency | Median | p95 | Highest | Budget | Met |
| --- | --- | --- | --- | --- | --- | --- |
| Rendering a 50-field form from its cached definition | 1 at a time, 300 times | 4.8 | 6.8 | 11.8 | ≤ 20 | yes |
| Rendering a 50-field form, 911 a second | 10 at a time, 600 times | 10.2 | 16.2 | 39.6 | | |
| A whole page with a form (the demo's Contact) | 1 at a time, 100 times | 4.2 | 5.8 | 7.1 | | |
| A submission (store plus outbox) | 10 at the same moment, 10 times | 38.2 | 56.1 | 61.1 | ≤ 150 at p95 | yes |
| The first mail after a submission | 1 at a time, 5 times | 8.7 | 9.9 | 9.9 | ≤ 10,000 | yes |
| An upload, 200 submissions a minute for 3 minutes | 600 visitors, none failed | 12.1 | 13.9 | 72.1 | | |
| The submission after its upload, at that rate | 600 visitors, none failed | 10.2 | 15.8 | 26 | ≤ 150 at p95 | yes |
| The mail after each submission, at that rate | 600 mails | 8.7 | 10.9 | 167.4 | ≤ 10,000 | yes |
| The entries list over 100,000 entries: the first page, newest first | 1 at a time | 10.7 | 22.6 | 22.6 | ≤ 300 | yes |
| … the first page with the count of all 100,000 | 1 at a time | 82.8 | 90.2 | 90.2 | ≤ 300 | yes |
| … one state | 1 at a time | 4.5 | 7.7 | 7.7 | ≤ 300 | yes |
| … one field's answer | 1 at a time | 11.1 | 12.5 | 12.5 | ≤ 300 | yes |
| … two fields' answers | 1 at a time | 18.6 | 31.7 | 31.7 | ≤ 300 | yes |
| … a search of every answer | 1 at a time | 88 | 162.8 | 162.8 | ≤ 300 | yes |
| … sorted by a number field | 1 at a time | 9.6 | 29.8 | 29.8 | ≤ 300 | yes |
| … the next 19 pages, each by its cursor | 1 at a time | 9.2 | 12.9 | 12.9 | ≤ 300 | yes |
| A CSV export of 10,000 entries, streamed | 1 at a time, 3 times | 1,698 | 2,461 | 2,461 | ≤ 5,000 | yes |

- **The forms:** the 50-field form holds text, email, long text, number, date, phone, web address, checkbox, list and
  choice fields in five groups; the entries form 12 fields (text, email, two lists, numbers, a date, a checkbox, a
  choice and a long text), with 100,000 entries sent through the submission pipeline as visitors leave them; the upload
  form a file field and an email action.
- **A visitor's send** goes as a page sends it: a challenge, the three seconds a person takes at least, the upload, the
  send. The mail goes to the host's pickup folder, so a real mail server adds its own time to the mail's numbers.
- **The list's filters:** the state is the one every seeded entry has, so it keeps all 100,000; one field's answer keeps
  a tenth; the two fields' answers are two that no entry has together, so the list finds none; the search looks for one
  person's name in every answer. The count of all entries is asked for once, as the list does when it opens.

## Where SQLite stops keeping up

SQLite has one writer, which Umbraco shares, so the stores of submissions that arrive together wait for each other.
Bursts of growing size, every send of a burst arriving at the same moment, five bursts of each size:

| Arriving at the same moment | Median | p95 | Highest | Stored a second | Within 150 ms at p95 |
| --- | --- | --- | --- | --- | --- |
| 10 | 39.7 | 56.3 | 62.8 | 172 | yes |
| 20 | 71.2 | 114.4 | 124.6 | 168 | yes |
| 40 | 144.5 | 223.1 | 235.6 | 171 | no |
| 80 | 284.6 | 454 | 493.4 | 168 | no |
| 160 | 614.8 | 1,108 | 1,302 | 155 | no |

The server stores about 155 to 170 submissions a second however many arrive, so each send's time grows with the burst:
on this machine SQLite keeps every send within the budget up to 20 arriving at the same moment, and passes it at 40.
Seeding the 100,000 entries through the pipeline, four at a time inside the server, went at 127 to 140 a second. A
site's visitors rarely arrive that closely together (200 a minute is one every 300 ms, and kept every send under
30 ms), but a busy site, a campaign that sends many people to a form at one moment, or a site on more than one server
belongs on SQL Server ([database.md](database.md)).

## Backoffice

| What | Samples | Median | p95 | Highest | Budget | Met |
| --- | --- | --- | --- | --- | --- | --- |
| The tree: a folder of 1,000 forms opened in Edge, from the click to its first form drawn | 5 | 476 | 510 | 510 | < 1,000 | yes |
| The tree: its first 100 forms through the API, in the database of the 100,000 entries | 9 | 52.5 | 61.3 | 61.3 | < 1,000 | yes |

**The designer with 300 fields** (`specs/budgets/designer.spec.ts`, ADR-0169, ADR-0186, ADR-0242, ADR-0243): forms
of three pages, thirty groups and 300 fields, open in Edge at 1,440 pixels, through selecting by click and by keyboard,
moving, adding, deleting, undoing and redoing a field, scrolling the canvas and moving a field by dragging. The steps
run twice, each on a form of its own in a new page: first in the browser as the test starts it, whose graphics chip
draws each kind of thing for the first time, then in a second page of the same browser, as an editor's browser draws.
The browser's own measures, in milliseconds, the highest of five runs alone (16.5):

| Measure | New browser | Its budget | New page | Its budget | Met |
| --- | --- | --- | --- | --- | --- |
| The slowest interaction, from the input to the next paint (146 to 185 of them over 16 ms) | 88 | < 200 | 56 | < 100 | yes |
| The longest task holding the main thread (none reached 50 ms) | 0 | < 100 | 0 | < 100 | yes |
| The most work of scripts and rendering in one frame | 1 | < 100 | 1 | < 100 | yes |
| The most rendering in one frame | 1 | < 50 | 1 | < 50 | yes |
| The longest frame, the wait for the browser's own drawing included | 91 | reported | 90 | reported | |

The slowest interaction in the new page takes 48 to 56 ms. In the new browser it takes 64 to 88 ms: the first click,
which opens the inspector, and the first Delete, which shows its Undo toast, wait there for the browser to compile its
graphics programs for the inspector's controls and the toast's shadow, which a browser keeps in its profile from then
on (ADR-0243). A move by the keyboard takes 32 to 56 ms and a drag's first moves 24 to 32 ms since the canvas's dashed
lines run along square corners (ADR-0242).

## Files

Checked by `scripts/size-check.mjs` on every client build, which fails the build when a budget is missed. Sizes are
gzip, in kilobytes of 1,024 bytes, as the build of 2026-11-15 measured them.

| What | Size | Budget |
| --- | --- | --- |
| The website runtime's core: the entry and its static imports | 2.80 | ≤ 16 |
| The runtime with every feature, and the largest of the seven languages' words | 63.54 | ≤ 100 (ADR-0251) |
| The base rules every form loads (`base.css`) | 1.98 | ≤ 10 |
| The default theme (`default.css`) | 8.06 | ≤ 10 |
| The Bootstrap 5 theme (`bootstrap5.css`) | 4.73 | ≤ 10 |
| What every backoffice session loads at its start: the entry, its static imports, and two languages' words | 44.39 | ≤ 64 |
| The backoffice's largest file (the designer) | 32.45 | ≤ 64 |
| The whole backoffice bundle (56 files) | 505.56 | ≤ 512 |

A page loads only the runtime's core and the features its forms use, each when it is first needed; every file is served
compressed, under a fingerprint, with a year's caching (ADR-0026, ADR-0092), and nothing loads from another host.

## Lighthouse

Lighthouse 12.8.2 in Edge, its mobile settings (a slow 4G network, 150 ms round trips, and a processor four times
slower), the medians of three runs of each page on the demo's layout: the Contact page, which holds the contact form and
the layout's newsletter form, and the Privacy page without the newsletter (`?newsletter=off`), which holds no form and
loads nothing of Jiron Form Builder.

| Page | Throttling | Performance | Accessibility | Best practices | SEO | FCP | LCP | TBT | CLS |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| With forms (Contact) | applied to a real load | 100 | 100 | 100 | 80 | 1,336 | 1,336 | 23 | 0 |
| Without a form (Privacy) | applied to a real load | 100 | 100 | 100 | 80 | 1,318 | 1,318 | 0 | 0 |
| With forms (Contact) | simulated | 100 | 100 | 100 | 80 | 1,304 | 1,558 | 0 | 0 |
| Without a form (Privacy) | simulated | 100 | 100 | 100 | 80 | 753 | 753 | 0 | 0 |

The times are milliseconds. Both pages score the same, and in a real load on the slow network the forms cost 19 ms of
first paint and 23 ms of blocking time. Lighthouse's simulation, which works a slow phone's load out from this machine's,
puts the first paint of the page with forms 550 ms later, which cost it one point (99) in three of the five runs made
while this was looked into: it counts the two stylesheets the forms bring (the base rules and the theme) as holding the
first paint and serves them one after another on the local server's connection, although they come after the page's
own first content, which a browser draws without waiting for them. A page whose layout loads them in its head with
`<jfb-assets>` makes them hold the first paint for real; left to the forms, they come just before the first form. The
demo sends its pages compressed, as a site's host does; uncompressed, the Contact page needs a second round trip of its
own. The SEO score is the demo's, the same on both pages: it has no meta description and its language links are
relative.

## Query plans

Two integration suites hold the plans of the statements that grow with a site's entries, on SQLite, so a change that
leaves an index fails a test rather than slowing a list down later:

- **`EntryQueryPlanTests`** explains every statement the entries list's query builder writes, more than 300 of them:
  each sort (newest, oldest, by a text, a number or a date, either way, unseen first) with no filter, every filter kind
  alone (one of, none of, starts with, a number range, a date range, checked, unchecked, answered, not answered), all of
  them together, a search, a search with a filter, one and two states, the test entries, and a submission range, each
  from the start and after a cursor, and each count. SQLite's `EXPLAIN QUERY PLAN` of none scans the entries or their
  values, and no walk through a sort sorts in a temporary b-tree, with and without the planner's statistics (`ANALYZE`).
- **`IndexPlanTests`** explains the other statements that grow with a site: an entry's files and the uploads that wait
  for one, the mail limits, the files that wait for a scan, one form's waiting uploads and days for its delete,
  retention's days, the references a content item's save reads and replaces, and the checks of a new form's alias and
  key; each names its index and scans no table.

SQL Server's optimizer chooses its own plans; the same queries' correctness tests run there in CI (ADR-0003, ADR-0039).
Both suites passed on 2026-11-12 (10 tests, the entries shapes explained with and without statistics).

## How to measure

- **The server and the tree:** `npm run load` in `tests/e2e` (ADR-0238). It builds the demo, starts it on port 5523
  with a fresh SQLite database, seeds it through the test endpoints, measures each part one after the other, prints a
  table, writes `.local/load/results.json`, and fails when a budget is missed. `JFB_E2E_SKIP_BUILD=1` reuses the last
  build, `JFB_LOAD_ONLY=render,submit` runs only some parts (render, submit, sqlite, mail, sustained, entries, export,
  tree, lighthouse), and `JFB_LOAD_ENTRIES` and `JFB_LOAD_MINUTES` change the 100,000 entries and the 3 minutes. A whole
  run takes about 25 minutes, half of them the seeding.
- **The designer:** `npx playwright test --project=budgets` in `tests/e2e`, which runs alone after the other projects.
- **The files:** `node scripts/size-check.mjs` after a client build.
- **The query plans:** `EntryQueryPlanTests` and `IndexPlanTests` in the integration tests.
