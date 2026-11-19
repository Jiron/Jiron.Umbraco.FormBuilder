# Status

Where Jiron Form Builder stands at the end of its first build, on 2026-11-19, after the pass that made its backoffice
simpler to find a way through and the fix that lets a field be called anything: what was built, how it is tested, what
the budgets measured, what it does not do yet, and what only you, the owner, can do next.

This report is the last step of [ROADMAP.md](../ROADMAP.md). `main` is the Umbraco 17 line, between releases at
`17.0.0-alpha.1.dev`; its first prerelease, `17.0.0-alpha.1`, is tagged. `v18/main` is the Umbraco 18 line at
`18.0.0-alpha.0`. Nothing is published, and the repository is private.

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

The clean-clone verification of ROADMAP 20.1 ran again at the end of Phase 23 (23.1), on 2026-11-19 on origin's
`main` at `7ac169c7`, in a fresh clone with a NuGet cache of its own (`node scripts/verify-clean.mjs`). Every step
passed, in 4,681 s, the browser tests on their first run:

| What ran | Count |
| --- | --- |
| Unit tests (.NET), with the shared vectors in the server's engine | 9,123; 17 skipped, the webhook signature's PHP sample, as this machine has no PHP |
| Integration tests (.NET), on real Umbraco hosts with SQLite | 2,313 |
| The AI add-on's tests (.NET) | 56 |
| The client's tests (Vitest), with the shared vectors in the browser's engine | 6,333 in 237 files |
| The repository's scripts (Node) | 401 |
| The browser tests' own support (Node) | 59 |
| The browser tests (Playwright in Edge): the backoffice, the website and the budgets | 590 |
| Lint, formatting, the build with no warning, licences and notices, the size budgets | all clean |
| A pack of `17.0.0-dev.202611190207` | 4 packages |
| The install test on Umbraco 17.7.1, and on the floor, 17.6.0 | 18 checks each, and 4 on the floor's cache |
| The upgrade from `17.0.0-alpha.1`, with data | 53 checks |
| The secret scan of the whole history | 1,267 commits, clean |
| The term guard of the whole history | skipped: your private term list is not on this machine (item 1 below) |

Before that run, the first build of Phase 23's fix failed two of the website's browser tests, and they were right.
The website's first file had come to wait for a second one before it ran, so an answer a visitor gave while the
form's script was still on its way no longer took its error away. That file loads nothing before it runs again, and
the build fails if it ever does ([ADR-0260](decisions/0260-a-field-may-be-called-anything.md)).

- **Coverage:** Core 97.6% of its lines (35,800 of 36,681) and Web 97.7% (11,274 of 11,538), from the .NET tests on
  `7ac169c7` (ROADMAP §6 asks 85% of Core).
- **The walk:** every screen of the backoffice in twelve looks (two themes, two languages, three widths) and five
  journeys, at the end of the simpler backoffice on `4be8f0ac`: 1,844 shots, none with an error, a fold inside a fold
  or a key in place of its words, and nothing of the section's own from axe ([review-22.md](ux/review-22.md)). No
  file of the backoffice's screens has changed since.
- **Checked by changing the code:** each step put behaviours back wrong one at a time and ran the tests that should
  catch each, until all were caught ([WORKLOG.md](WORKLOG.md) names them per step).
- **Accessibility:** axe on every backoffice screen and every theme, keyboard paths, and the narrowest widths in the
  browser tests ([accessibility.md](accessibility.md)).
- **The Umbraco 18 line:** with every commit of `main` merged in, its whole gate passed on 2026-11-19 on `f9eda3e5`,
  in a clone of its own: the unit tests (9,152; the same 17 skipped), the integration tests (2,313), the AI add-on's
  (56), the client's (6,342 in 238 files) and the whole browser suite (590). The browser suite passed on its second
  run. In the first, a write found the test site's SQLite database locked: two tests ran out of time, one of them
  while a demo member signed in, and ten did not run. The gate runs the suite once more on fresh sites for that one
  cause, never twice ([ADR-0250](decisions/0250-the-umbraco-18-line.md)). The 17 to 18 upgrade with data last ran
  at the Phase 19 review, where it passed 53 checks.

## The budgets, measured

The budgets of ROADMAP §4.12, on the development machine; [performance.md](performance.md) has every measurement and
how to repeat it. Sizes are gzip.

| Budget | Measured | Limit |
| --- | --- | --- |
| The website runtime's core, what every page with a form loads first | 2.94 KB | 16 KB |
| The runtime with every feature and the largest language's words | 64.00 KB | 100 KB ([ADR-0251](decisions/0251-the-runtimes-budget-grows-to-100-kb.md)) |
| The default theme's stylesheet | 8.06 KB | 10 KB |
| Rendering a 50-field form from its cached definition | 4.8 ms median | 20 ms |
| A submission, 10 at the same moment, on SQLite | 56.1 ms at p95 | 150 ms |
| The first page of 100,000 entries | 10.7 ms median, 22.6 ms highest | 300 ms |
| A CSV export of 10,000 entries | 1.7 s median | 5 s |
| The first mail after a submission | 8.7 ms median | 10 s |
| A page with a form in Lighthouse (mobile) | performance 100, accessibility 100 | the same page without a form |
| The slowest designer interaction on a form of 300 fields | 48 ms (88 ms in a fresh browser) | 100 ms (200 ms in a fresh browser) |
| The tree opening a folder of 1,000 forms | 476 ms median, 510 ms highest | 1 s |
| What every backoffice session loads at its start | 44.90 KB | 64 KB |
| The backoffice's largest file (the designer) | 39.42 KB | 64 KB |
| The backoffice bundle | 508.23 KB | 512 KB |

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

1. **Before the repository goes public:**
   - have a lawyer review the licence and its plain-language summaries, and draft an optional additional permission;
   - set up a contributor licence agreement, then let the commit identity check accept contributors' names;
   - run `node scripts/term-guard.mjs --history` with your term list in `.private/terms.txt`;
   - optionally: put the full code of conduct in the repository; ask Umbraco HQ about "Umbraco" in the package id.
   - Throughout: never call Jiron Form Builder "open source"; it is free and source-available.
2. **Make the repository public,** then in its settings enable private vulnerability reporting and Discussions, and
   protect `main`.
3. **Turn on CI:** set the repository variable `JFB_CI` to `true`, raise the Dependabot limits, and check the first run:
   the first on Linux, and the first of the parts above that never ran.
4. **Check the draft release** of `v17.0.0-alpha.1` under Actions › Release and Releases: seven files and the changelog's
   notes.
5. **Publishing:** reserve the NuGet prefix `Jiron.`, create the trusted-publishing policy and set `JFB_NUGET_USER` and
   `JFB_NUGET_PUBLISH`, then publish and validate both Marketplace listings ([releasing.md](releasing.md)).
6. **Real-world checks** of the stubbed services and screen readers above, and of how GitHub and nuget.org render the
   docs.
7. **Legal judgements** for a lawyer on the privacy defaults: retention, proof of double opt-in, shortened IP addresses,
   the confirmation mail, the insights beacon, one consent for two things, and sorting into Spam.
8. **Before the first stable release,** decide whether paid add-ons will exist: it shapes the contributor licence
   agreement and the add-on packages. Before monetising anything, paid support and donations included, check the
   Cyber Resilience Act.
9. **Decisions for later:** an importer from other form systems, and publishing the docs on a site of their own.
10. **Report to Umbraco HQ** the backoffice problems above; OWNER-TODO lists each with its measurements.
