# Status

Where Jiron Form Builder stands at the end of its first build, on 2026-11-26, after the pass that made its backoffice
simpler to find a way through, the fix that lets a field be called anything, the work that brought the tests from
hours to half an hour, the change that leads an administrator to a user group's permissions, the day its tests
first passed on Linux and on SQL Server and the evening it went public and onto NuGet: what was built, how it is
tested, what the budgets measured, what it does not do yet, and what only you, the owner, can do next.

This report is the last step of [ROADMAP.md](../ROADMAP.md). **The repository is public since 2026-11-25, and
`17.0.0-beta.3` is on nuget.org** (`Jiron.Umbraco.FormBuilder`, `.Core`, `.Web` and `.AI`), with its release on
GitHub. `main` is the Umbraco 17 line and names `17.0.0-beta.3.dev`. The earlier tags have no release:
`17.0.0-alpha.1` and `17.0.0-beta.1` failed their runs, and `17.0.0-beta.2` passed its run but was not published
(below). `v18/main` is the Umbraco 18 line at `18.0.0-alpha.0`, unpublished.

**The simpler backoffice you asked for on 2026-11-16 is built:** Phase 22 of the roadmap, four steps
([ADR-0254](decisions/0254-a-simpler-backoffice.md)). [The concept](ux/simpler-backoffice.md) says what became of each
of its findings, with the numbers before and after.

- **The designer (22.1).** A field is added through a picker that every "Add field" opens; a page is renamed,
  duplicated and deleted at its tab; "After sending" is edited beside what visitors get; and the form has 57 % of a
  1440 px window instead of 39 %, under one row of controls
  ([ADR-0256](decisions/0256-the-designer-in-three-columns.md)).
- **The section's left side (22.2).** Five places come first: Inbox, with its count of unseen entries, Library,
  Settings, Privacy and Activity. The tree of forms follows them. Each place is one screen with tabs, seventeen in
  all, and a user sees only the places and tabs their permissions open. Every screen kept its address, so a link
  in a mail already sent and a bookmark open what they opened
  ([ADR-0257](decisions/0257-five-places-before-the-tree.md)).
- **A form's views (22.3).** A form's Settings list their six groups, each with a line of what is set, and show
  one group at a time; "Add action" asks for the kind first; the inspector has no "More options" any more; Preview,
  Save draft and Publish… show on Design, Settings and Actions only; and the History view is the one way to an
  earlier version ([ADR-0258](decisions/0258-a-forms-views-pick-first.md)).
- **The other screens (22.4).** Entries, their filters and exports, the option list editor, People, Retention,
  Deliveries, the Audit log and Insights were each asked whether there is a second way to do a thing, what could
  wait, what is said twice and whether an empty screen says what to do next. [review-22.md](ux/review-22.md) holds
  every finding, fixed or kept with its reason, and the guides' nineteen pictures were taken again
  ([ADR-0259](decisions/0259-the-other-screens-and-the-walks-count.md)).
- **Measured:** on the 286 shots of the last walk before the phase, the screens held 59,481 words and now hold
  48,455; none shows more than one word more. Twelve of the 78 top tasks take one click more, ten of them the
  click on a tab; none grew by more than one, and none takes more than four.

**A field may be called anything (Phase 23, 2026-11-19).** You said the question the simpler backoffice had left
open could be tested, and it was a fault. A browser answers a property of a form element with the form's control of
that name, and a control is named by its field's alias. So with JavaScript a form with a field called `dispatchEvent`
could not be sent, one with a field called `replaceWith` stored its entry and showed no outcome, and nine other names
sent the form by the post without JavaScript or threw errors on the page. No name is refused for it: the website's
script now reads a form past its controls, and a lint rule, the unit tests' forms and browser tests with a control
for every such name keep it so ([ADR-0260](decisions/0260-a-field-may-be-called-anything.md)). Insights had missed
the check errors of a field called `constructor` or `toString`; that is fixed with it.

**The tests take half an hour, not hours (Phase 24, 2026-11-22).** Before the first release you said the tests take
hours and may be more than is needed. Measured, the count was not the cost: the 15,500 unit and client tests took two
and a half minutes together. The time went into 2,313 integration tests that each start an Umbraco, one after the
other, and into 590 browser tests against one site. So no test was removed, and the same checks now run side by side
([ADR-0261](decisions/0261-where-the-test-time-goes.md)):

- **The gate** (`scripts/ci-local.ps1`) took 67 minutes, and 90 when its browser tests ran a second time. It takes
  about 29 now.
- **A phase review's clean-clone verification** took 78 minutes and takes 43.
- **One fault of the product was found on the way:** with JavaScript, an answer given and left before a form's script
  had loaded got no error at its field. That is fixed.
- **The first release's workflow would have stopped at its 45-minute limit:** its test jobs ran the integration tests
  in one process. They are a matrix of jobs now. How long each takes on GitHub is yours to read on the first run
  ([OWNER-TODO.md](OWNER-TODO.md)).
- **The Umbraco 18 line on SQLite showed a fault of its own,** below under "What it does not do yet".

**A group's access is found where it is looked for (Phase 25, 2026-11-22).** On the first site you made a user group
for the person who will run it, gave it the section, and its user found every place refused; Umbraco's user group
screen had no switch for it. The permissions were where they belong, in Form Builder › Settings › Access, and worked
once given, but nothing led there ([ADR-0262](decisions/0262-a-groups-access-on-its-user-group-screen.md)):

- **Every refusal names the place whole,** "Form Builder › Settings › Access", in English and German. "Settings ›
  Access" alone was said to a user whose section shows no Settings place.
- **Umbraco's user group screen has a Form Builder view** beside Details, for a user who may manage permissions: what
  the group may do, that it has no permission yet or lacks the section, and one link to Settings › Access. It shows
  and links and saves nothing, as the screen's Save is Umbraco's.
- **Privacy › People without its permission** says which permission it needs and shows no search.
- **The backoffice bundle has 1.9 KB left** of its 512 KB: the next change to the backoffice has to make room first.

**The tests pass where the workflow runs them (Phase 26, 2026-11-23).** Until the release run of 17.0.0-beta.1 every
test had run on one Windows machine with SQLite. That run and the one of 17.0.0-alpha.1 had failed, unseen; you
said to fix it. A dry run of the release workflow now passes every job and packs, on Linux, on SQL Server and with
two servers. What the first runs there found, each fixed at its cause:

- **Three faults of the product.**
  - A Linux server without its package of older time zone names refused names browsers list, such as
    `Asia/Calcutta`, and ran a site stored under one in UTC. It now finds them through its own time zone database.
  - On SQL Server, a server that took an action of an entry read every action under a lock, so actions taken side
    by side met deadlocks: 1,844 in one job of the tests, each tried again, one of them three times in a row. The
    check now reads the entry's own actions only (8 deadlocks in a whole run since, each tried again), and a unit
    whose transaction SQL Server ended is always run again
    ([ADR-0040](decisions/0040-outbox-repositories.md)).
  - On SQL Server, a page cursor made by hand for the free-text answers of Insights caused a server error.
- **The rest were the tests' or the workflow's:** fixtures and expectations that held one system's culture
  data ([ADR-0263](decisions/0263-the-systems-culture-data-in-tests.md)), a "slow" statement that SQL Server
  answered at once, test servers that shared no key ring, time budgets set for another database on another
  machine ([ADR-0264](decisions/0264-the-timed-tests-budgets-per-database.md)), layout tests that depended on one
  font, and six smaller ones ([WORKLOG.md](WORKLOG.md)).
- **Three tools stay:** `node scripts/linux-ci.mjs` runs the workflow's Linux steps in a container on this
  machine, for nothing; the workflow **Targeted run** runs one part of CI on GitHub (one SQL Server share is about
  20 minutes of runner time against 5 h 20 min for everything); and each SQL Server job prints the deadlocks its
  server recorded, which a passing run otherwise hides.
- **What it cost:** the account's 3,000 included Actions minutes for November, about half of them here. Four
  full runs and seven targeted ones; nothing was charged ([OWNER-TODO.md](OWNER-TODO.md)).
- **Known and left:** on SQL Server the Answers view of 100,000 entries takes 3 to 5 s on a hosted runner, and a
  few deadlocks remain possible; both need a SQL Server to measure on.
- **Public and published (2026-11-25).** You made the repository public, enabled private vulnerability reporting
  and Discussions, and had the rest set up in your browser: the ruleset `protect main` (no deletion and no force
  push on `main` and `v18/main`), the Sponsor button, nuget.org's trusted-publishing policy under the
  organization that holds the `Jiron.*` prefix, the environment `nuget` for tags `v*`, and the two variables that
  turn publishing on. The release run of `v17.0.0-beta.2` passed all its 26 jobs and drafted its release, the
  first run to get that far, but had started before publishing was on. `v17.0.0-beta.3` is the same code: its run
  passed all 27 jobs and published the four packages, and a new project installs them from nuget.org with the
  README's command. Its release is published on GitHub as a prerelease with seven files; the draft of
  17.0.0-beta.2 was deleted on your word.

## Contents

- [What was built](#what-was-built)
- [How it is tested](#how-it-is-tested)
- [The budgets, measured](#the-budgets-measured)
- [What it does not do yet](#what-it-does-not-do-yet)
- [What is left for you](#what-is-left-for-you)

## What was built

Everything ROADMAP §1.1 and §1.3 describe. Each line names where it is described.

| Part | What a site gets | Read more |
| --- | --- | --- |
| The section | Five places before the tree of forms: Inbox, Library, Settings, Privacy and Activity, each one screen with tabs, showing a user only what their permissions open | [editor-guide.md](editor-guide.md), [information-architecture.md](ux/information-architecture.md) |
| The form library | Folders, search, nine starter templates, duplicate, move, a Recycle Bin with restore, delete with the pages that still use a form named, import and export as JSON | [editor-guide.md](editor-guide.md) |
| The designer | Pages, groups of one to four columns, a field picker with search behind every "Add field", drag and drop with a full keyboard alternative, undo and redo, an inspector in sections, texts per language, a live preview at three widths, the form check | [editor-guide.md](editor-guide.md) |
| Field types | The 33 types of §4.3, a third-party captcha when an editor chooses one, and a site's own types | [field-types.md](field-types.md) |
| Option lists | From the field, a reusable list (also from a CSV or text file), Umbraco content or a data type, a JSON address on an allowed host, the countries, or code; cascading lists and places per option | [field-types.md](field-types.md) |
| Rules and validation | Show, hide, skip, require, choose the outcome and gate an action; conditions with "all" and "any", nested, on answers, language, member and date; the server decides and the browser agrees (one set of shared vectors) | [rules-and-validation.md](rules-and-validation.md) |
| Drafts, publishing and history | A draft until published, every published version kept, compared and restored; entries keep the version they were sent with | [editor-guide.md](editor-guide.md) |
| Form settings | In six groups, each with a line of what is set: labels, outcomes, storage, approval, verification, availability, members, the marker, the theme, the reference | [editor-guide.md](editor-guide.md) |
| Multi-page forms | Steps or a bar, checks per page, Back without losing answers, save and continue later through an emailed link, prefill | [editor-guide.md](editor-guide.md), [rendering.md](rendering.md#save-and-continue-later) |
| Actions | Email to the team and to the submitter, webhooks (signed, retried, safe from server-side requests to private addresses), Slack and Microsoft Teams, Umbraco content, the entry's state; triggers, rules, order, retries, a delivery log and a test run | [actions.md](actions.md), [webhooks.md](webhooks.md), [email.md](email.md) |
| Entries | Per form and across forms, Spam, new markers, filters, search, columns, bulk actions; one entry's panel with its timeline, notes, approval, editing with an audit and forwarding; CSV, Excel, JSON, print and a ZIP of the files | [entries-and-exports.md](entries-and-exports.md) |
| Insights | Views, starts, completions, drop-off per page, time to complete and the fields that stop visitors, without cookies; answer summaries | [insights.md](insights.md) |
| The library | Reusable option lists and groups of fields, themes edited visually with contrast checks, email layouts | [themes.md](themes.md), [editor-guide.md](editor-guide.md) |
| Privacy | Retention with a dry run, sensitive fields, fields kept until the actions ran, IP handling, a person's entries found, exported and erased, consent records, an audit log | [privacy.md](privacy.md) |
| Permissions | Per user group, on folders and forms | [permissions.md](permissions.md) |
| For visitors | WCAG 2.2 AA, works without JavaScript and better with it, inline checks, an error summary, uploads with progress, sending without a reload, a copy of the answers, seven languages and right to left, spam protection without third parties | [accessibility.md](accessibility.md), [spam-protection.md](spam-protection.md) |
| For developers | A tag helper, a view component, a form picker and a block, views to override, CSS custom properties; the form as JSON, a submission API, the Delivery API, TypeScript types and a JSON schema, a web component; extension points with samples | [rendering.md](rendering.md), [headless.md](headless.md), [extending.md](extending.md) |
| Deployment | Forms mirrored to files, upgrades tested for every release, load-balanced hosting, SQLite and SQL Server, files in a private folder or blob storage | [deployment.md](deployment.md), [upgrading.md](upgrading.md) |
| Sites and languages | Several sites in one installation, each with its own sender, base URL, time zone and defaults; texts per culture and dictionary items | [multi-site.md](multi-site.md), [multi-language.md](multi-language.md) |
| Members | Forms for members, prefill, one entry per member | [members.md](members.md) |
| The AI add-on | `Jiron.Umbraco.FormBuilder.AI`: draft a form, translate texts, suggest help texts; editors only, no visitor data | [ai.md](ai.md) |
| Umbraco 18 | The same product on `v18/main`, whose docs say how a site moves from 17 to 18; that upgrade, with data, is tested | [ADR-0250](decisions/0250-the-umbraco-18-line.md) |

The demo site (`demo/`), the documentation in `docs/`, screenshots, the logo, CI, packaging and the Marketplace
listings are in the repository too.

## How it is tested

The clean-clone verification of ROADMAP 20.1 ran again at the end of Phase 26 (26.1), on 2026-11-25 on origin's
`main` at `58ffbe2c`, in a fresh clone with a NuGet cache of its own (`node scripts/verify-clean.mjs`). Every step
passed, in 3,170 s. The run before it, on `355c7f31`, had failed: one client test of the AI add-on named a limit of
30 s, half of what the run gives every test, and ran out of it while other work loaded the machine; it has the
run's minute now. On 2026-11-23 the verification had passed six times, last on `75b6c921` in 2,837 s. The coverage
was measured on `de208db2`: Core 97.6% of its lines; since then one local variable was renamed in Core and no
statement added or removed.

| What ran | Count | At the end of Phase 25 |
| --- | --- | --- |
| Unit tests (.NET), with the shared vectors in the server's engine | 9,199; 17 skipped, the webhook signature's PHP sample, as this machine has no PHP | 9,157; 17 skipped |
| Integration tests (.NET), on real Umbraco hosts with SQLite | 2,318 | 2,316 |
| The AI add-on's tests (.NET) | 60 | 56 |
| The client's tests (Vitest), with the shared vectors in the browser's engine | 6,363 in 240 files | 6,360 in 239 files |
| The repository's scripts (Node) | 526 | 491 |
| The browser tests' own support (Node) | 86 | 85 |
| The browser tests (Playwright in Edge): the backoffice, the website and the budgets | 597 | 597 |
| Lint, formatting, the build with no warning, licences and notices, the size budgets | all clean | all clean |
| A pack of `17.0.0-dev.202611231800` | 4 packages | 4 packages |
| The install test on Umbraco 17.7.1, and on the floor, 17.6.0 | 18 checks each, and 4 on the floor's cache | the same |
| The upgrade from `17.0.0-alpha.1`, with data | 53 checks | 53 checks |
| The secret scan of the whole history | 1,340 commits, clean | 1,305 commits, clean |
| The term guard of the whole history | skipped: your private term list is not on this machine (item 5 below) | skipped |

No test went: every count is the one before or higher, the added ones being the tests of what Phase 26 fixed and of
its tools.

**On GitHub, for the first time whole** (the dry run of the release workflow on `de208db2`, 2026-11-23, read in your
browser): 25 jobs passed and two were skipped, as a dry run drafts and publishes nothing. It took 31 minutes and
5 h 21 min of runner time.

| Job on GitHub (Ubuntu 24.04) | Time |
| --- | --- |
| Build, checks and tests (the scripts, the client, the Release build, the unit and AI tests) | 19 min 50 s |
| Integration tests on SQLite, six shares and the timed tests | 9 min 14 s to 13 min 32 s; timed 6 min 58 s |
| Integration tests on SQL Server 2022, ten shares and the timed tests | 14 min 2 s to 19 min 36 s; timed 11 min 20 s |
| Browser tests (smoke and website, in Chromium) | 26 min 51 s |
| Load balancing (two servers on one SQL Server) | 5 min 37 s |
| The shares add up (2,318 tests on each database, each once) | 17 s |
| Pack the packages | 3 min 44 s |

The longest integration job is under the 22 minutes the plan allows, so the number of shares stays. Its SQL Server
recorded 8 deadlocks over all eleven jobs. The timed tests measured, on SQLite and on SQL Server there: the answer
summaries 835 ms and 3.22 s, the first page of entries 43 ms and 93 ms, the CSV export 2.44 s and 3.33 s.

**How the gate runs now.** Its checks and builds run in lanes side by side; the integration tests run in eight
processes at once, with the unit and AI tests beside them and the tests that measure time alone after them; the
browser tests run against two sites of their own, three browsers each, with the `budgets` project alone at the end.
Each of the two long stages counts what ran and fails unless it is exactly the tests it holds, each once.

| Stage | Before (2026-11-19) | Now (the last run) |
| --- | --- | --- |
| Checks, builds, lint, the client's tests, packing | 320 s, one after the other | 237 s, in lanes |
| Unit and integration tests | 2,256 s | 599 s |
| Browser tests | 1,380 s, and once more when the site's database stalled | 917 s |
| The whole gate | 67 minutes; 90 with the second browser run | 1,753 s: 29 min 13 s |

- **Twice in a row.** The whole gate passed twice running on the final work commit, `f2c19a25`, in the repository's
  folder, no test failing and no site run a second time: in 1,746 s and 1,753 s (29 min 6 s and 29 min 13 s). Before
  the last fixes it had passed twice running on `8809766a`, in 1,729 s and 1,671 s.
- **How much it varies.** Eight passing runs of the gate on `main` took between 1,671 s and 1,815 s. Seven were under
  30 minutes; the slowest, 30 min 15 s, ran inside the last clean-clone verification, which also fetches every package
  anew. The same tests took up to 15% longer from one run to the next with nothing changed, so the half hour holds
  with about a minute to spare and no more.
- **What more runs found.** Four runs in fresh clones each failed on one test, each a fault of the test that a rare
  moment shows: a check that a random token does not hold a three-letter name (once in about two thousand runs), a
  test that looked for a dialog still closed a moment after a request, an accessibility check of a toast that had
  not faded in yet, and a test that pauses for the designer's own waits and met the test runner's five-second limit
  beside the build. Each is fixed at its cause, and the client's tests now have a minute each, a hang's limit. More
  whole runs in a day show such tests sooner than before; none is rerun to make it pass.
- **What Phase 25's runs found.** Its clean-clone verification failed six times before it passed, each time on one
  test that was there before and never on the phase's own: two accessibility checks of a website page that measured
  a colour halfway through its fade, a test that pressed Escape before a menu had the focus, one that typed before a
  text had the focus back, one that took an earlier toast for its own save and left the page, and a unit test that
  read the sources while another removed its scratch folder among them. Each is fixed at its cause, the two that are
  a kind for every test of that kind: every website page settles before axe measures it, and the tests that read
  the sources enter no scratch folder. Between them the whole browser suite passed twice in the repository's
  folder. A fresh clone shows such tests more often than the repository's folder does; why is not known.
- **A broken product is still flagged.** One fault was put into each of four layers at once, and each was caught
  where it was caught before: a comparison in the rules' shared logic by 18 vector tests in the unit tests, the order
  of a repository's query by two integration tests, a size in the default theme by a website browser test, and a
  hidden part of a backoffice dialog by a backoffice browser test. Each fault was taken out again. They ran through
  the gate's two runners (`scripts/test-integration.mjs`, `scripts/e2e-gate.mjs`), not through one whole gate run.
- **Not seen yet:** the workflow on GitHub. Its jobs are fitted to its limits by estimate from this machine.

- **Coverage:** Core 97.6% of its lines (35,800 of 36,681) and Web 97.7% (11,278 of 11,538), from the .NET tests run
  in shares on 2026-11-21 (ROADMAP §6 asks 85% of Core); the figure for Core is the one of the run in one process
  before. The product's code has not changed since; the tests of the test hosts' start-up have.
- **The walk:** every screen of the backoffice in twelve looks (two themes, two languages, three widths) and five
  journeys, at the end of the simpler backoffice on `4be8f0ac`: 1,844 shots, none with an error, a fold inside a fold
  or a key in place of its words, and nothing of the section's own from axe ([review-22.md](ux/review-22.md)). No
  file of the backoffice's screens has changed since.
- **Checked by changing the code:** each step put behaviours back wrong one at a time and ran the tests that should
  catch each, until all were caught ([WORKLOG.md](WORKLOG.md) names them per step).
- **Accessibility:** axe on every backoffice screen and every theme, keyboard paths, and the narrowest widths in the
  browser tests ([accessibility.md](accessibility.md)).
- **The Umbraco 18 line:** with every commit of `main` merged in, its whole gate passed on 2026-11-22 on `81261a7e`,
  in a clone of its own, in 1,828 s: the unit tests (9,186; the same 17 skipped), the integration tests (2,316), the
  AI add-on's (56), the client's (6,347 in 238 files) and the whole browser suite (590), on its first run. Three runs
  before it failed: its test sites hung as they started (fixed, see "What it does not do yet"), a publish on a test
  site never came back, and a test read a setting before its save had landed (fixed) while the other site stalled and
  passed on its one rerun. The 17 to 18 upgrade with data last ran at the Phase 19 review, where it passed 53 checks
  ([ADR-0250](decisions/0250-the-umbraco-18-line.md)).

## The budgets, measured

The budgets of ROADMAP §4.12, on the development machine; [performance.md](performance.md) has every measurement and
how to repeat it. Sizes are gzip.

| Budget | Measured | Limit |
| --- | --- | --- |
| The website runtime's core, what every page with a form loads first | 2.99 KB | 16 KB |
| The runtime with every feature and the largest language's words | 64.11 KB | 100 KB ([ADR-0251](decisions/0251-the-runtimes-budget-grows-to-100-kb.md)) |
| The default theme's stylesheet | 8.05 KB | 10 KB |
| Rendering a 50-field form from its cached definition | 4.8 ms median | 20 ms |
| A submission, 10 at the same moment, on SQLite | 56.1 ms at p95 | 150 ms |
| The first page of 100,000 entries | 10.7 ms median, 22.6 ms highest | 300 ms |
| A CSV export of 10,000 entries | 1.7 s median | 5 s |
| The first mail after a submission | 8.7 ms median | 10 s |
| A page with a form in Lighthouse (mobile) | performance 100, accessibility 100 | the same page without a form |
| The slowest designer interaction on a form of 300 fields | 48 ms (88 ms in a fresh browser) | 100 ms (200 ms in a fresh browser) |
| The tree opening a folder of 1,000 forms | 476 ms median, 510 ms highest | 1 s |
| What every backoffice session loads at its start | 45.00 KB | 64 KB |
| The backoffice's largest file (the designer) | 39.42 KB | 64 KB |
| The backoffice bundle | 509.84 KB | 512 KB |

## What it does not do yet

- **Payments, a conversational one-question-at-a-time mode, quizzes and polls, assignees and boards for entries,
  importing entries and scheduled exports,** among others, are ROADMAP §7's "Later": not in this release.
- **SQL Server, the load-balancing job and the webhook signature's PHP sample have never run.** Everything else runs
  here on SQLite; those run only in CI, which waits for you.
- **No real third-party service or screen reader was used:** captchas, SMTP, an AI provider and blob storage were
  tested against local stubs and fakes, and names, roles and keyboard paths are checked automatically.
- **SQLite has a single writer:** above about 20 submissions arriving at the same moment, a submission takes more than
  150 ms at p95. Busy sites should use SQL Server ([performance.md](performance.md#where-sqlite-stops-keeping-up)).
- **Umbraco 17 on SQLite can stall when several editors save at the same moment,** with or without the package:
  each of Umbraco's two jobs over the servers' cache instructions reads before it writes, and a job and a request's
  own instruction can wait for each other in SQLite's shared cache; every database call of the site then waits with
  them, for minutes. SQL Server has no such lock, and Umbraco 18 no longer uses the shared cache. The browser tests'
  hosts of one server leave both jobs out
  ([ADR-0253](decisions/0253-one-servers-test-host-reads-cache-instructions-once-a-day.md)); reporting it to Umbraco
  is on your list ([OWNER-TODO.md](OWNER-TODO.md)).
- **Umbraco 18 on SQLite can leave a write waiting for good when another write lands at the same moment.** In the
  18 line's gates of 2026-11-22 it showed three ways. Test sites hung as they started, four of twelve in one run:
  Umbraco's background jobs ran for the first time while the site's own test users were still being stored. A memory
  dump of one shows Umbraco's own user creation trying its insert again and again inside one transaction, with
  nothing else running. The test sites now start those jobs after their own seeding, by order, and none has hung
  since (six starts under full load, three whole runs). Then, once each: a page's publish on a test site never came
  back, and a write was refused as "database is locked", for which the browser tests run that site's tests once
  more. The last two were there before Phase 24 (the 18 line's gate of 2026-11-19 needed its second browser run for
  the same). What is not known: whether the publish that never came back is the same fault, and how often a real
  site on SQLite would meet it. SQL Server has no such lock. Umbraco 17 has not shown it. Reporting it is on your
  list ([OWNER-TODO.md](OWNER-TODO.md)).
- **Save and continue later needs JavaScript,** a page of the site for its link to come back to, and a form that stores
  entries; files, consents and sensitive answers are never saved, so the visitor gives them again.
- **Without JavaScript, on a form for members with "one entry per member":** a post with a file that the browser
  sends a second time (a reload of the result) is answered "not sent" although the first was stored, as the file is
  stored again under a new name ([ADR-0252](decisions/0252-the-last-pass.md)).
- **A form whose first publish from its file was refused** stays unpublished until the file changes again; the log
  names the file and the reason.
- **On Umbraco 18.3,** a form shown beside all its views in a window narrower than about 1,100 px (in German) leaves its
  name too little room, as Umbraco's own header row does for every workspace with that many views
  ([ADR-0250](decisions/0250-the-umbraco-18-line.md)).
- **Umbraco's own backoffice** has colours below WCAG AA in its dark and high-contrast themes, two components whose
  accessible name or state is wrong, and pages at least 920 px wide, so a phone scrolls sideways. Jiron Form Builder's
  own parts pass.

## What is left for you

Only you can do these; [OWNER-TODO.md](OWNER-TODO.md) has each in full, with its steps. A sensible order:

1. **Done on 2026-11-25:** the repository is public, private vulnerability reporting and Discussions are on, `main`
   and `v18/main` are protected from deletion and force pushes, the `Jiron.*` prefix is reserved, the
   trusted-publishing policy and the two variables are set, and `17.0.0-beta.3` is on nuget.org with its release on
   GitHub. Every later version tag publishes; set `JFB_NUGET_PUBLISH` to anything but `true` to stop that.
2. **Done on 2026-11-26: contributors are welcome, without an agreement**
   ([ADR-0265](decisions/0265-contributions-without-an-agreement.md)). The guide, the commit check and CI are
   open to them; what is left is reviewing what comes in. The first pull request shows whether every job runs
   from a fork.
3. **Turn on CI:** set the repository variable `JFB_CI` to `true` and raise the Dependabot limits, so every push to
   `main` runs the tests; in a public repository the runs cost nothing.
4. **Call the first stable version.** The Umbraco Marketplace lists no package that has prereleases only; both
   listings pass its validator otherwise (2026-11-26), so the product appears there with `17.0.0`
   ([releasing.md](releasing.md)).
5. **Still worth doing, none of them in the way:** a lawyer's look at the licence's plain-language summaries; a run
   of `node scripts/term-guard.mjs --history` with your own term list in `.private/terms.txt`; optionally the full
   code of conduct in the repository, and a question to Umbraco HQ about "Umbraco" in the package id. Throughout:
   never call Jiron Form Builder "open source"; it is free and source-available.
6. **Real-world checks** of the stubbed services and screen readers above, and of how GitHub and nuget.org render the
   docs.
7. **Legal judgements** for a lawyer on the privacy defaults: retention, proof of double opt-in, shortened IP addresses,
   the confirmation mail, the insights beacon, one consent for two things, and sorting into Spam.
8. **Before the first stable release,** decide whether paid add-ons will exist: it shapes the add-on packages,
   and without a contributor agreement a paid add-on builds on the packages without taking others' code from
   this repository. Before monetising anything, paid support and donations included, check the
   Cyber Resilience Act.
9. **Decisions for later:** an importer from other form systems, and publishing the docs on a site of their own.
10. **Report to Umbraco HQ** the backoffice problems above; OWNER-TODO lists each with its measurements.
