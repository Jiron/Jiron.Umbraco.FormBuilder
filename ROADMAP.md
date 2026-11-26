# Jiron Form Builder roadmap: Jiron.Umbraco.FormBuilder

**Calm, capable forms for Umbraco.** Jiron Form Builder is a form builder for Umbraco 17. Editors design forms in a **Form Builder** section of the backoffice: pages, groups, fields, rules and actions, with drafts, history and a live preview. Visitors fill them in on the website, server-rendered and accessible, or through a headless API. The people who handle the results work with the entries: they review, approve, export and answer privacy requests. It is free to use, including commercially, and maintained in the open.

Written 2026-10-19 from the owner's brief, with research into Umbraco 17 packaging, the Marketplace, licensing, privacy, accessibility and email law, plus an inventory of the owner's earlier work (Ligata.Forms and Tandem). Revised 2026-10-22 with the lessons of Tandem's finished build: its process, its test harness and about sixty gotchas found on this machine (§3.3 and `docs/research/tandem-lessons.md`). The steps were consolidated from 137 to 107 on the same day. The rules for working on it are in [CLAUDE.md](CLAUDE.md), and they are binding.

## How to use this roadmap

- Work through Phase 0 to Phase 23 **in order, one step at a time**. A step can only start once the steps before it are done. The one exception: when a step is blocked, continue with the next step that doesn't depend on it, and come back later.
- **There is no deadline.** Take the time each step needs. Read the relevant sections of this roadmap again before you start a step, check the result in a real browser before you call any UI done, and prefer the robust solution over the quick one.
- Step markers (only steps use them, always at the start of a line):
  - `[ ]` open
  - `[x]` done
  - `[~]` blocked, with the reason written under the step and the item copied to `docs/OWNER-TODO.md`
- **Finishing a step:**
  1. Commit the step's work.
  2. Change its marker to `[x]` and append ` (YYYY-MM-DD, <short hash of the step's last work commit>)` to its title line.
  3. Add a line to `docs/WORKLOG.md`, commit both as `docs(roadmap): complete X.Y`, and push.

  `node scripts/roadmap-status.mjs` prints the progress line.
- **The full gate runs at milestones** (the owner, 2026-11-06; ADR-0194, amending ADR-0117). A step is finished on its focused checks, run here on its last work commit: `node scripts/fix.mjs`, the build with warnings as errors, `dotnet format --verify-no-changes`, the unit tests, the integration tests of what it touches, the client's lint, typecheck and tests, the e2e typecheck, and its "Done when" checks; a step that changes the build, the packages, the test harness or the host's start-up runs the stages of `ci-local.ps1` it changes too. The full gate runs at each phase review (the phase-review gate below) and wherever a "Done when" names `ci-local.ps1`, in a throwaway clone (`node scripts/gate-clone.mjs run <commit>`), with nothing on the browser projects' ports while it holds them. A step is ticked and pushed once its checks passed for its last work commit, and the next step's first commit comes after that tick.
- **The bootstrap exception:** step 0.1 makes no commit (the owner already made the license root commit). Its tick and WORKLOG line are written in 0.2.
- **Done when** lists the checks that prove a step is finished. Run them in the current session and show their output. Passing checks are the only evidence that counts.
- **Every phase ends with a phase review.** It reviews the phase's work, fixes what it finds, and writes a handover note to `docs/WORKLOG.md`, so that a context compaction at any moment loses nothing. Where a phase needs a review of its own (security, privacy, UX, the schema freeze), that is a step of its own. Otherwise the review is the last part of the phase's last step, whose title says "and phase review".
- You may improve this roadmap as you learn: fix facts that turn out wrong, and move work between steps. **Never split a step, and never add small ones** (the owner, 2026-11-06, ADR-0197): a step that turns out bigger than written stays one step, worked in as many commits as it needs, and something clearly missing joins the step it belongs to. Record every such change in `docs/WORKLOG.md` with the reason.
  - **Never weaken or delete** a requirement from §1 or §2, a step, or a "Done when" check.
  - If a check is impossible as written, replace it with an equal or stronger check, explained in an ADR.
  - If a requirement turns out impossible, explain why in an ADR and list it in `docs/OWNER-TODO.md`.
- Facts marked *(verify)* came from research but were not confirmed on this machine. Confirm them in Phase 1 before relying on them.
- **Clustered steps** (the owner, 2026-11-06, ADR-0197): the open steps were put together, one or two a phase. Each keeps the number of its first part and holds its parts' items and "Done when" checks word for word, under their old numbers and titles, and is ticked once all of them pass. A step number named elsewhere before ADR-0197 (11.2, 18.3) points into the step that now holds it. Steps done before then keep their letters (`9.3a`, `9.3b`).
- **`[~]` is only for things outside your control:** an owner-only item (CLAUDE.md rule 8) or an outage of an outside service. An ordering problem (a check that needs something a later step builds) is fixed by moving work, with the reason in WORKLOG, never by marking a step blocked.
- **The phase-review gate.** Every phase review's "Done when" includes these checks, whether the review is a step of its own or the end of a step "and phase review", and even where it only says "pushed":
  - `scripts/ci-local.ps1` passes (from 3.5 on);
  - the full Playwright suite passes (from 3.4 on);
  - `node scripts/secret-scan.mjs --tracked` and `node scripts/term-guard.mjs --tracked` pass;
  - Core coverage is ≥ 85% (from Phase 4 on);
  - **the wiring sweep:** every setting, endpoint, notification, extension point and menu item that the phase added is used by product code and reachable, and each has a test that shows its effect. List them in the handover note. Tandem's reviews found four that were stored or named but never wired: a setting nothing read, an option that sent nothing, a settings section with no screen, and an interface that was never built;
  - every `[~]` step has been retried;
  - the owner's private mapping in `.private/` (if there is one) is re-checked;
  - the handover note is written.
- **Settings come with their feature.** A settings screen or tab is built in the step that builds the service it configures, or later, never earlier. Tandem had to split its settings step five ways (7.5 to 7.5e) because its tabs came before their services.
- **One source for every value.** Tests and scripts never hard-code what lives elsewhere:
  - they read the Umbraco major and the package version from `Directory.Build.props`;
  - they read the migration plan's last state from the code;
  - they read ports and URLs from the demo's `appsettings.json` and `tests/e2e/support/paths.ts`.

  Tandem's install test broke silently when a migration step was added, and its Umbraco 18 branch failed on every hard-coded "17".
- **Flaky means broken.** Browser suites run with `retries: 0`. Exactly two failures may be rerun once, each noted in WORKLOG with the marker or exit code:
  - the known Umbraco start-up hang on SQLite (§3.3, the `database-hung.txt` marker);
  - a crash of the runtime itself (CLR `0x80131506`, node `0xC0000005`).

  Any other intermittent failure is a bug and gets fixed at its cause. The machine is shared, and load makes timing bugs show, which is useful.
- **Formatters before the gate.** Run `node scripts/fix.mjs` (`dotnet format`, ESLint `--fix`, Prettier) before every gate run. The build does not catch `IDE1006`, and Tandem's gate often failed once on formatting alone, which cost a 15 to 30 minute run each time.
- **When the owner writes during the run,** the message beats the plan:
  - record it (as an ADR when it changes a decision, otherwise in WORKLOG);
  - add or change roadmap steps for it, never weakening one;
  - carry on.

  Tandem's owner added "Umbraco 17 first", a design pass and "say it once" mid-run, and each became steps and checks.
- **Design work uses three skills: `design-taste-frontend`, `impeccable` and `emil-design-eng`** (the owner, 2026-10-22; ADR-0019, ADR-0020):
  - every step that designs or builds UI loads all three before it starts;
  - every full design pass and UX review runs all three again, and its review document has a section per skill (the design read and pre-flight result; the `impeccable` mode, commands and findings; the `emil-design-eng` Before/After/Why table) and says what changed;
  - where a skill asks the user to choose, decide it yourself and record the choice and its alternatives (the owner answers no questions, CLAUDE.md rule 8);
  - `impeccable` runs only with the engine bundled in the installed skill: no download, no hooks, no live mode (ADR-0020);
  - where a skill and this roadmap disagree, D12, D13, D18, §4.13, WCAG 2.2 AA, the §4.12 budgets, the Phase 2 tokens and CLAUDE.md rules 2 and 4 win, and the review document says so.
- **ADR numbers 0001 to 0018** are reserved for the ADRs this roadmap names. Any other ADR starts at 0019. Tandem's ADRs are always cited as "Tandem ADR-NNNN".
- **When a feature appears in two steps,** the earlier step builds the part it names, and the later step completes and audits it.
- **Every step that adds UI or endpoints** adds:
  - its §4.7 endpoints, with integration tests including the permission matrix (4.7);
  - the regenerated API client;
  - English and German localisation keys for every new string.

## Contents

1. [Product](#1-product)
2. [Decisions already made](#2-decisions-already-made)
3. [Reference material](#3-reference-material)
4. [Target design](#4-target-design)
5. [Phases and steps](#5-phases-and-steps)
6. [Definition of done for the first release](#6-definition-of-done-for-the-first-release)
7. [Later (not in this roadmap)](#7-later-not-in-this-roadmap)
- [Appendix A: README.md for the first commit](#appendix-a-readmemd-for-the-first-commit)
- [Appendix B: ADR template](#appendix-b-adr-template)
- [Appendix C: Worklog format](#appendix-c-worklog-format)

---

## 1. Product

### 1.1 What Jiron Form Builder is

A NuGet package for Umbraco 17 sites. Install it, open the **Form Builder** section, and build a form in minutes. Put it on a page with a picker, a block, a tag helper or the headless API. The site gets:

- **A form library** in the backoffice:
  - folders, search, and a gallery of starter templates (contact, callback, newsletter, event registration, job application, feedback, quote request, support request);
  - duplicate, move, trash (with restore) and delete, with a warning that names the pages still using a form;
  - import and export of a form as JSON.
- **A designer that stays calm.** Forms are built from **pages** (steps), **groups** (sections with one to four columns) and **fields**:
  - a searchable field palette, plus click-to-insert points and drag and drop with a full keyboard alternative;
  - inline label editing and undo/redo;
  - an inspector for the selected element, whose settings are sorted from essential to advanced;
  - per-language texts and a live preview at phone, tablet and desktop widths;
  - a **form check** that flags problems before visitors find them: a required field that a rule can hide, an email action without an email field, a missing label, a duplicate alias, a rule that points at a deleted field.
- **Field types** (§4.3): text, long text, email, number, phone, web address, date, time, date and time, dropdown, radio buttons, checkboxes, grid, ranking, single checkbox, yes/no, consent, file upload, rating, scale (NPS style), slider, signature, name, address, country, hidden, calculation, repeatable group, heading, text block, divider and image. Plus third-party captcha fields when chosen.
- **Option lists.** Choice fields take their options from:
  - the field itself;
  - a reusable list;
  - Umbraco content or an Umbraco data type;
  - a JSON address on an allowed host, or a CSV upload;
  - the built-in country list;
  - code.

  Lists can cascade: the second dropdown filters on the first. An option can have a place limit (seats, time slots), and visitors see how many places are left.
- **Rules** (conditional logic):
  - show or hide fields, groups and pages, and skip pages;
  - make a field required;
  - pick the success message or redirect;
  - decide whether an action runs.

  Rules combine conditions with "all" or "any", with nested groups and operators for text, numbers, dates, choices and files. A condition can compare two answers, or look at the page's language, the member and the date. An overview lists every rule in the form and warns about conflicts.
- **Validation:**
  - required, length, range, pattern (safe, linear-time regular expressions), format, file type and size, date windows ("at least 2 days from today");
  - "matches another field", and "unique per form" (for example one registration per email address);
  - custom messages per language.

  The server always decides; the browser gives the same answers instantly.
- **Drafts, publishing and history.** Edits stay a draft until someone publishes them. Every published version is kept, and can be compared side by side and restored. Entries always remember the version they were submitted with.
- **Form settings:**
  - submit and navigation labels;
  - success message or redirect, both conditional;
  - store entries or not;
  - manual approval;
  - opening and closing dates and an entry limit, with a closed message;
  - members-only forms and one entry per member;
  - the required or optional marker style;
  - autocomplete hints, CSS classes and the theme.
- **Multi-page forms:**
  - a progress indicator (steps, bar or none), per-page validation, back without losing answers;
  - **save and resume later** through an emailed link;
  - prefill from merge tags (member data, the page, the query string when allowed).
- **Actions** run after a submission:
  - **Kinds:**
    - email the team, or confirm to the submitter (responsive HTML, or plain text when the action chooses it, per language, attachments optional; one body per mail, because Umbraco's mail sender takes one, ADR-0007);
    - call a webhook (signed, retried, SSRF-safe), or post to Slack or Microsoft Teams;
    - create or update Umbraco content;
    - change the entry's state.
  - **Email verification** (double opt-in) is a form setting rather than an action: it sends its own mail and holds the actions back until the visitor confirms (ADR-0002).
  - **Triggers:** submitted, approved, rejected, verified.
  - **Behaviour:** each action has an optional rule, an order, a switch, retries with a visible delivery log, and a **test run** against a sample entry.
- **Entries:**
  - **Where:** a list per form and an **all forms** inbox, with a **Spam** folder, "new" markers, filters, search, a column chooser and bulk actions.
  - **One entry:** a side panel shows its values, files, a timeline of everything that happened to it, and internal notes. It is approved or rejected there, edited with an audit trail, and forwarded to a colleague.
  - **Exports:** CSV, Excel, JSON, a printable view, and a ZIP of the uploaded files.
- **Insights**, without cookies or personal data: views, starts, completions, conversion, drop-off per page, time to complete and the fields that fail validation most, per form and over time. **Answer summaries** show what people answered: counts per option, averages, and the 0-to-10 recommendation score.
- **A library:**
  - reusable option lists, and saved groups of fields (an address block, a standard consent);
  - **themes**, edited visually (colours, type, spacing, corners) with a live preview and contrast checks, no code needed;
  - email layouts.
- **Privacy built in:**
  - retention rules with a dry run;
  - sensitive fields, masked and kept out of emails, exports and logs unless allowed;
  - fields whose values are kept only until the actions have run;
  - IP handling: none by default, anonymised or full;
  - find, export and erase one person's entries across all forms;
  - consent records with the exact wording shown;
  - an audit log.
- **Permissions** per user group: design, view entries, edit entries, delete, export, manage settings. Folders and forms can be limited to certain groups.
- **For visitors:**
  - **The basics:** fast, accessible (WCAG 2.2 AA), mobile-first forms that work without JavaScript and get better with it:
    - inline validation, an error summary that links to the fields, focus management;
    - uploads with progress;
    - submission without a page reload, and a printable copy of their answers when the form offers one;
    - texts in their language, laid out right to left where the language is written that way.
  - **Spam protection that respects them:** honeypot, timing and a signed challenge by default, with an optional proof-of-work check and no third parties. reCAPTCHA or hCaptcha only when an editor chooses them, and only per §4.10.
- **For developers:**
  - **Razor:**
    - a tag helper and a view component;
    - a form picker property editor with a value converter, and a ready-made block element type on request;
    - theme views that can be overridden, and CSS custom properties.
  - **Headless:**
    - the form as JSON, a submission API, and the form picker in the Content Delivery API;
    - published TypeScript types and a JSON schema;
    - a framework-free web component that renders any form.
  - **Extensibility:** custom field types, actions, option sources, export formats, captcha providers, spam checks, merge-tag sources and file scanners. Notifications for every lifecycle event, public services, and compiled samples.
  - **Deployment:**
    - optional mirroring of forms to JSON files for source control;
    - schema-versioned definitions, idempotent migrations and an upgrade test for every release;
    - load-balanced hosting with no extra infrastructure (forms with file fields keep their files where every server reaches them, such as the blob storage a load-balanced site already uses for media, ADR-0006);
    - SQLite and SQL Server;
    - file storage in a private folder by default, or in blob storage through Umbraco's official storage provider (a sample store, because Umbraco's file systems write synchronously, ADR-0006).
- **An optional AI add-on**, `Jiron.Umbraco.FormBuilder.AI`, on Umbraco.AI: it drafts a form from a description, translates a form's texts and suggests help texts. Editors only, and visitor data never reaches an AI.

### 1.2 Who uses it

| Persona | Needs |
| --- | --- |
| Visitor | A short, clear form that works on a phone, says exactly what is wrong, never loses their answers, and doesn't track them. |
| Editor (marketing, communications) | Builds and changes forms without a developer, sees the result as visitors will, can't break a live form by accident, and is warned about mistakes. |
| Entry handler (sales, HR, support, events) | Finds new entries at once, works through them quickly (approve, reject, note, export), and can trust that every email went out or see why not. |
| Privacy officer | Retention that runs by itself, one place to find, export or erase a person's data, and proof of consent. |
| Administrator or developer | Installs in minutes, configures everything without code, deploys forms through source control, extends it through documented interfaces, and upgrades without fear. |

### 1.3 In scope for the first release

Everything in §1.1, plus:

- multi-language sites (texts per culture, dictionary keys, seven built-in visitor languages);
- multiple sites in one Umbraco installation (per-site sender address, base URL and defaults);
- Umbraco members (members-only forms, prefill, one entry per member);
- load-balanced hosting, SQLite and SQL Server;
- the optional AI add-on;
- a demo site with realistic seeded forms and content;
- complete documentation, screenshots, logo, CI, packaging and Marketplace metadata;
- an Umbraco 18 line (Phase 19).

### 1.4 Jiron Form Builder is not, and never becomes

- **Not a payment processor.** It never touches card or bank data. Payments are a "Later" extension topic (§7).
- **Not a CRM or a marketing-automation tool.** It connects to them through actions, webhooks and the extension points.
- **Not a tracker.** No cookies, no fingerprinting, no third-party scripts on a page unless an editor picks a third-party captcha. Insights are anonymous counters.
- **Not a hosted service.** It runs inside the site that installs it.
- **Not dependent on community packages**, and **not derived from any other product** (CLAUDE.md rule 2).

---

## 2. Decisions already made

These come from the owner or from planning. Change one only through an ADR that explains why.

| # | Decision | Why |
| --- | --- | --- |
| D1 | **Name:** NuGet id and namespace root `Jiron.Umbraco.FormBuilder`; display name **Jiron Form Builder**; backoffice section label **Form Builder** (the owner, 2026-11-03, ADR-0171; it was **Forms**, and its forms tree keeps that name); repo `github.com/Jiron/Jiron.Umbraco.FormBuilder` (private for now). Short identifiers use `jfb` (tables, CSS, URLs, events) and `JironFormBuilder` (settings section, folders, JS global). The owner reserves the NuGet prefix `Jiron.` (OWNER-TODO). | Owner's choice after name research (2026-10-20). A descriptive product name under the owner's own name leaves nothing to clash with, and the reserved prefix marks the packages as his. "Umbraco" appears only as the platform qualifier inside the package id, never in the display name. The product is always a "Form Builder"; no package id ends in `.Forms`. The owner named the section "Form Builder" (ADR-0171); editors find their forms in its tree, "Forms". |
| D2 | **First commit = license.** `LICENSE.md` is the PolyForm Perimeter License 1.0.1 with Jiron Form Builder's header and `Required Notice:` line. The owner placed it and made the root commit `781401e` on 2026-10-20, replacing GitHub's README-only commit `06f126f`; step 0.1 checks its SHA-256. No force-push remains approved. | The owner wants everyone to be able to use Jiron Form Builder free of charge, including companies on their own sites and agencies on client sites, and to change it for their own projects, but nobody to sell it, to publish their own version of it (free or paid, open source or not), or to offer any other competing product or service built from it. Contributions go to this repository instead. Perimeter says exactly that in a short, lawyer-drafted text (§3.6). It is **not** OSI "open source", so Jiron Form Builder calls itself "free and source-available", never "open source". A lawyer review is an OWNER-TODO item. |
| D3 | **Target Umbraco 17 LTS first:** CMS `[17.6.0, 18.0.0)` (raised from 17.5.0 in 5.5a, ADR-0074: 17.6.0 is the first 17.x whose runtime cache clears reliably), developed against the latest 17.x (17.7.1 on 2026-10-19), .NET 10. An Umbraco 18 line comes at the end (Phase 19). **17 is the most important version** (the owner, 2026-10-22): the 18 line never costs `main` a feature, a fix, a delay or a compromise, and nothing is designed for 18 at 17's expense. | 17 is the LTS that most sites run, supported until 2028-11-27. 17.5 brought the async distributed background jobs Jiron Form Builder relies on. Confirmed in 1.2b: the whole spike builds against 17.5.0, and only the `tree` workspace-view kind (17.6.0) and `AlignToClock` (17.6.2) are newer; Jiron Form Builder uses neither. Before 17.6.0 a value Umbraco's runtime cache was told to clear could stay kept (5.5a), which every cache of Jiron Form Builder relies on. |
| D4 | **Original work.** Every name, screen, syntax, data model and line of code is Jiron Form Builder's own (CLAUDE.md rule 2). Vocabulary: form, page, group, field, option list, rule, action, entry, theme, merge tag, template. | Legal safety and a coherent product. The owner's private reference material stays in `.private/` and never enters the repository. |
| D5 | **Form definitions are versioned JSON documents; entries are relational.** A form has one draft and many published versions, each a schema-versioned JSON document upgraded by code. Entries are rows: entry, typed values per field, files, a timeline and notes. Each entry points at the version it was submitted with. | Drafts, history, diff, restore, export and deployment as files fall out naturally. Old entries never change meaning when a form changes. Entries stay filterable and sortable in SQL. Proven in Ligata.Forms (draft and published documents), but entries there were JSON blobs that could not be queried, which is fixed here. |
| D6 | **Submissions are transactional and idempotent.** One transaction stores the entry, its values and one queued **action run** per action that applies (an outbox). A client-generated submission id plus a digest makes retries safe. Actions run in the background with leases, retries and a visible log. | No entry is lost when email or a webhook is down, and no double entries come from double clicks or flaky networks. Proven in Ligata.Forms. |
| D7 | **Persistence:** NPoco DTOs with Umbraco's `PackageMigrationPlan` and `AsyncMigrationBase` steps that are idempotent and use frozen schema snapshots. It works on SQLite and SQL Server. All Jiron Form Builder database access goes through one async gate, and writes start with a write. | This is Umbraco core's own path. `MigrationBase` is obsolete in 17 and gone in 18. The gate is Tandem's proven fix for SQLite's shared-cache locking (§3.3). |
| D8 | **Background work** runs as `IDistributedBackgroundJob`s: one server in the cluster runs each, coordinated by the database. Jobs cover the action and email outbox, retention (entries, mail, files and old draft versions, and the daily statistics past their time), exports and the summary emails; Insights' counts need no job, as each server sums them and writes them every 10 seconds (ADR-0211). The file scans, the abandoned-upload cleanup and the file mirror's pass have timers of their own on the server that schedules (ADR-0144, ADR-0148, ADR-0220). After a commit, the server also wakes its own processor at once, so emails go out within seconds; the job itself is only the safety net. | Load-balanced safe, with no extra infrastructure and no per-node state that matters. Umbraco's runner starts only one due job per 5-second tick, and its own jobs come first, so a package's job can wait a long time for its turn. Speed therefore never depends on the job's period (Tandem ADR-0076). |
| D9 | **Rendering:** server-side Razor first, enhanced by a small framework-free TypeScript runtime: no jQuery, ≤ 16 KB gzip, no third-party requests. Without JavaScript, forms still submit and validate on the server. A **headless** path (JSON definition, submission API, web component renderer) uses the same markup contract and the same runtime. | SEO, speed, accessibility and robustness. Headless sites are a large part of Umbraco 14+ projects. |
| D10 | **One logic, two runtimes.** Rules, validation and merge tags are implemented in C#, which is authoritative, and in TypeScript for instant feedback. Both must pass **one shared set of JSON test vectors** (`tests/vectors/`). | The browser and the server can never disagree about a hidden field or a validation message. This was a lesson from Ligata.Forms, where a condition value was handled differently than intended. |
| D11 | **Public endpoints** live at `/jfb/api/v1` (configurable), never under `/umbraco`. Razor forms without JavaScript post back to their page through Umbraco's surface routing. | Many sites block `/umbraco` publicly, and no-JS posts must re-render the page with errors. |
| D12 | **The backoffice** is a top-level section **Form Builder** (ADR-0171; alias `Jiron.FormBuilder.Section`), built with Lit, TypeScript and UUI, using Umbraco's own patterns: trees, workspaces with views, the save bar, modals and sidebars, entity actions and toasts. Reorderable lists use Jiron Form Builder's own accessible drag controller instead of Umbraco's sorter, which has no keyboard moves, names or touch support (ADR-0016). It talks to the server through a generated OpenAPI client. | It must feel native to Umbraco. No foreign design system. |
| D13 | **Privacy by default:** <ul><li>no cookies, and IP addresses are not stored;</li><li>retention defaults are on;</li><li>sensitive fields are masked;</li><li>consent fields store the exact wording shown;</li><li>logs never contain field values;</li><li>nothing loads from third-party hosts unless an editor chooses a third-party captcha.</li></ul> | GDPR, TDDDG and good practice (§3.7). Site owners get compliance help without configuring anything. |
| D14 | **Spam protection needs no third party by default:** a honeypot, a minimum fill time, a signed challenge and content rules feed a spam score. Rate limits refuse with `429` and `Retry-After` instead of scoring, because a flood must cost nothing to turn away; the visitor keeps every answer and is told when to send again (ADR-0012). A proof-of-work challenge is optional. reCAPTCHA v2/v3 and hCaptcha are optional providers that load only per §4.10. Suspicious entries go to **Spam**, where nothing is lost and no action runs, instead of being dropped. | Stops bots without privacy costs, and never silently loses a real lead. |
| D15 | **Files:** <ul><li>stored outside the public web root, through Jiron Form Builder's asynchronous `IFormBuilderFileStore`, which also works with Umbraco's official storage providers (ADR-0006: Umbraco's `IFileSystem` writes synchronously, which a streamed upload cannot feed);</li><li>names are random, and downloads go only through authorised endpoints;</li><li>extension allowlist, content sniffing, size and count limits;</li><li>a scanner hook (`IFormBuilderFileScanner`), and deletion with the entry.</li></ul> | Uploaded files are personal data and an attack vector. |
| D16 | **Versioning and branches:** the package major follows the Umbraco major. The first line is `17.0.0-alpha.N`, then `-beta.N` (and `-rc.N` when a release candidate is needed, ADR-0010), then `17.0.0`, for CMS 17. `main` is the 17 line and holds every ROADMAP and WORKLOG change; CMS 18 code lives on `v18/main` (Phase 19). Changes flow one way: `main` is merged into `v18/main`, never the reverse. `Directory.Build.props` holds `VersionPrefix` 17.0.0 and `VersionSuffix` alpha.0 (between releases, the last release's suffix with `.dev`, ADR-0249); release builds pass the tag's version; local test packs use `-dev.<yyyyMMddHHmm>`. | Compatibility is obvious at a glance, and stale NuGet caches can never serve old bits. |
| D17 | **No publishing, no visibility changes.** The release workflow is built and tested, but the NuGet push stays disabled (`vars.JFB_NUGET_PUBLISH != 'true'`). CI runs on pushes only when `vars.JFB_CI == 'true'`. The repo stays private. | Only the owner can do these. |
| D18 | **UX is a first-class requirement** in the backoffice and on the website (§4.13): <ul><li>progressive disclosure, so no screen is cluttered;</li><li>one primary action per view, with secondary actions in menus;</li><li>**say it once:** each fact appears once per screen, never as a highlighted tab, a sidebar heading and a page title at the same time;</li><li>nothing squashed on phones: text keeps its width, and controls move to their own row;</li><li>every state designed, sensible defaults and plain language;</li><li>a guided first run, live previews and keyboard paths.</li></ul> | The owner: "It's important that we don't have a bunch of buttons and stuff clustered in the same place." During Tandem the owner added: no "5 gazillion buttons", nothing "repeating itself multiple times", and no button squeezed into the middle of a text on a phone (Tandem ADR-0079). |
| D19 | **Tests:** NUnit 3 for unit and integration tests (`Umbraco.Cms.Tests.Integration` 17.x requires NUnit 3), with NUnit's own constraints (no FluentAssertions) and NSubstitute. Vitest covers the client packages, Playwright the browser (Edge locally, Chromium in CI, phones emulated in Chromium because WebKit is not available here), and axe-core accessibility. The harness follows Tandem's (§3.3). | Official Umbraco test tooling with permissive licenses, the same stack Tandem proved on this machine. |
| D20 | **Multi-language:** <ul><li>every editor-written text (labels, help, placeholders, options, messages, emails) can be translated per culture, with fallback to the default text;</li><li>`#dictionaryKey` values resolve through Umbraco's dictionary;</li><li>default visitor texts ship in English, German, French, Italian, Dutch, Spanish and Danish;</li><li>the backoffice ships in English and German.</li></ul> | Umbraco's audience is European and multilingual. |
| D21 | **Upgradability is designed in:** <ul><li>Semantic Versioning;</li><li>idempotent migrations;</li><li>schema versions and upgraders for form definitions and settings;</li><li>a fixture database and an upgrade test for every released schema;</li><li>public API versions (`v1`);</li><li>deprecations that last a full major;</li><li>`docs/upgrading.md`.</li></ul> | The owner: "needs to be easily upgradable for future releases". |
| D22 | **No real third-party keys during the build.** Captcha providers, webhooks and the AI add-on are tested against local stubs and fakes only. Real-key checks are OWNER-TODO items. | Nothing to leak, nothing to pay for, and tests stay deterministic. |
| D23 | **AI is optional and separate.** `Jiron.Umbraco.FormBuilder.AI` uses Umbraco.AI for editor-side help only: draft a form, translate texts, suggest help texts. The core packages never reference Umbraco.AI, and no entry data is ever sent to an AI. | Useful for editors, with no new privacy risk for visitors. |
| D24 | **Merge tags** use Jiron Form Builder's own syntax: `{{ field.email }}`, `{{ member.name }}`, `{{ page.url }}`, `{{ entry.number }}`, `{{ query.ref \| default: "web" }}`. Values are HTML-escaped in HTML contexts, and tags are never evaluated as code. Editors insert them from a picker; nobody has to memorise the syntax. | One small, safe, documented template language for defaults, emails, messages, redirects and webhooks. |
| D25 | **Forms as files (optional):** with `JironFormBuilder:Definitions:Mirror` on, each published form is also written to `umbraco/JironFormBuilder/forms/<alias>.json`. On startup, changed files are imported as new versions, with conflict detection. Off by default. | Teams that deploy through source control get reviewable form changes without a community dependency. |

---

## 3. Reference material

### 3.1 Ligata.Forms: what to port

Ligata.Forms is at `C:\Code\Ligata.Forms`, version 0.3.2, commit `8f4b0c5`. It is the owner's earlier, deliberately small forms package for Umbraco 17.6 (about 2,000 lines). It is read-only: copy code into Jiron Form Builder and adapt it (CLAUDE.md rule 5). Read `README.md` and `docs/ARCHITECTURE.md`, `FEATURES.md`, `EDITOR.md`, `OPERATIONS.md`, `CONSENT.md`, `TESTING.md` and `PLAN.md` there first.

**Which version to read** (checked 2026-10-22): the folder's checked-out `main` is still 0.2.1 (`2bd47b0`), and 0.3.2 exists only as `origin/main`. Several files named below (for example `Rendering/FormAssets.cs` and all of `wwwroot/`) are missing from the working tree. Read 0.3.2 with `git -C C:\Code\Ligata.Forms show 8f4b0c5:<path>`, or export it once with `git -C C:\Code\Ligata.Forms archive --format=zip -o %TEMP%\jfb-ligata-forms-0.3.2.zip 8f4b0c5` and unzip that into `%TEMP%\jfb-ligata-forms-0.3.2` (`-o` writes the file itself; PowerShell 5.1 would corrupt a piped archive). Never pull, check out or reset in that folder. Abbreviations below: `src` = `src/Ligata.Forms`, `JS` = `src/wwwroot/assets/ligata-forms/ligata-forms.js`, `BO` = `src/wwwroot/App_Plugins/LigataForms`.

**Clean-room note:** its docs name another product in a few places. Never carry those mentions, links or class names (for example a delivery class whose name reads like another product's) into Jiron Form Builder.

| Area | Ligata.Forms source | In Jiron Form Builder |
| --- | --- | --- |
| Draft and published documents. The editor's concurrency `Version` is separate from the visitor-facing `Revision`, which changes only when the public projection changes. | `src/Data/FormStore.cs` (`Save`), `src/Models/FormDefinition.cs` (`Public()`, `SamePublicContent()`) | **Port the idea** into D5's draft, versions and public revision. The public projection never contains actions, recipients, secrets or internal notes. |
| Immutable entry snapshot (definition plus values) | `EntrySnapshot` in `FormStore.cs` | **Replace** with the entry → form version reference plus the field catalogue (D5, §4.4). Keep the guarantee: later edits never change what an old entry means. |
| Transactional outbox and worker: lease, back-off `2^n` minutes, an "uncertain" state after an expired lease, no retries burnt while SMTP is not configured | `FormStore.Accept/ClaimJob/CompleteJob`, `src/Services/WorkflowWorker.cs` | **Port the logic** into action runs and the email outbox (D6, D8), as distributed jobs instead of a `BackgroundService`. |
| Idempotent submissions: a client GUID plus a SHA-256 digest; the same digest returns the original receipt, a different one is a 409; a receipt endpoint for uncertain responses | `FormStore.Accept`, `PublicFormsController` | **Port** (§4.4). |
| The same condition semantics on the server and in the browser; "a rule may only look at earlier fields" prevents cycles | `FormValidation.Matches`, `JS` `matches` | **Grow** into the rules engine with shared test vectors (D10). Keep cycle prevention, as a graph check that also covers pages and groups. |
| Server validation: unknown fields rejected, hidden fields stripped, NUL and CR/LF guards, `MailAddress` for email, choices only from the allowed list | `src/Models/FormValidation.cs` | **Port and extend** per field type (§4.3). |
| Definition validation: limits, the alias pattern, reserved aliases, site-relative redirects only | `FormValidation.Definition` | **Port** into the hard limits plus the form check (9.5b). |
| Anonymous endpoint security: Data Protection-signed challenge, honeypot, origin allowlist, no credentialed CORS, `NoStore` responses, no personal data in logs | `src/Services/RequestGuard.cs`, `src/Controllers/PublicFormsController.cs` | **Port.** Replace the in-memory counters with the ASP.NET Core rate limiter (see §3.2 for the bypass). |
| reCAPTCHA v3 verification (success, score, action, hostname allowlist, token age, clock skew) | `src/Services/RecaptchaVerifier.cs` | **Port** as one `IFormBuilderCaptchaProvider` (7.2). |
| Consent-aware, lazy captcha loading; Cookiebot categories and events | `JS`, `docs/CONSENT.md` | **Port** into §4.10. |
| "The form changed while you were filling it in": fetch the new markup, keep the answers that still fit, reset changed consent texts, refuse when an answered field was removed | `JS` (render refresh) | **Port**, simplified for live sites (6.4j). |
| CSV export with BOM and the formula-injection guard (`=`, `+`, `-`, `@`, tab, CR) | `FormsManagementController.Export` | **Port** into every export (11.4). |
| Several forms on one page: deterministic, per-request instance ids | `src/Rendering/LigataFormViewComponent.cs` | **Port** (6.2b). |
| Static web assets in an RCL with `StaticWebAssetBasePath=/`; asset version taken from the assembly | the csproj, `src/Rendering/FormAssets.cs`, `tests/versions.test.mjs` | **Port.** The install test checks the assets after `dotnet publish` too (§3.2). |
| Editor behaviour: <ul><li>pure editor-model functions tested in Node;</li><li>pointer-capture drag, because native HTML drag and drop failed in testing;</li><li>Alt+Up/Down reordering;</li><li>reorders that would break a rule are refused;</li><li>undo after removing a field;</li><li>email presets ("notify your team", "confirm to the visitor") with a live preview.</li></ul> | `BO/editor-model.js`, `dashboard.js`, `editor-*.js` | **Rewrite** in TypeScript and Lit with UUI, and keep every behaviour. |
| Offline `mailto:` fallback and static-export resilience | `JS` | **Drop.** Static export is not a Jiron Form Builder goal. |
| Ligata-specific `?thema=` mapping | `JS` | **Drop.** Generic, opt-in prefill replaces it (6.5). |

### 3.2 Lessons from Ligata.Forms (keep them)

- **Define "empty" explicitly.** The "not checked" condition never matched, because empty values were filtered out before matching. Jiron Form Builder's rules have explicit `isEmpty`/`isNotEmpty` operators and unchecked-checkbox semantics, both covered by test vectors.
- **Rate limits must not fail open.** Ligata's `MemoryCache` with a size limit stops counting once it is full. Use the ASP.NET Core rate limiter with bounded partitions, and test what happens under a flood of distinct IPs.
- **No process-wide locks.** A `static object` lock breaks under load balancing. Use the database gate and row-level claims (D7, D8).
- **Searchable data must be stored as data.** Searching with `LIKE` over JSON text also matched labels and missed non-ASCII text stored as `\uXXXX`. Entries store typed values plus a normalised search column (§4.5).
- **APIs return DTOs, never rows.** The entries list returned raw rows with whole snapshots.
- **The UI follows permissions.** A dashboard that every Content user saw answered with errors for most of them. Jiron Form Builder hides what a user may not use and explains disabled actions.
- **NPoco needs explicit `[Column("Name")]` names**, or the generated table SQL is invalid.
- **Test static assets under `dotnet run` *and* after `dotnet publish`**; a 0.3.x release broke only in one of them.
- **Localise every message**, server validation included. Ligata's server messages were English only, even on German sites.
- **Give sites layout controls** (column spans, widths, choice styles), so they never need CSS that targets a field by alias, as ligata.ch had to.
- **Good form UX ideas** from the ligata.ch redesign mock (`C:\Code\ligata\redesign`): an error summary with links plus inline errors, a character counter, well-styled native checkboxes and radios. Adopt them in the default theme (6.3b).
- **Field and action types must be registries, not `switch` statements.**

### 3.3 Tandem: what to reuse, and its lessons

Tandem is at `C:\Code\Umbraco.Community.AI.Tandem`, the owner's chat package for Umbraco 17. It is read-only; port freely (CLAUDE.md rule 5).
- **Its state:** it was finished on 2026-10-22, with all 110 steps and an Umbraco 18 line, built and proven **on this machine**.
- **Its names:** its packages and namespaces were renamed to `Jiron.Umbraco.ChatSupport` mid-run (Tandem ADR-0035), so its source paths read `src/Jiron.Umbraco.ChatSupport.*`. The folder kept its old name.

Before Phase 1, read its `CLAUDE.md`, `docs/STATUS.md`, the handover notes in `docs/WORKLOG.md`, and these ADRs:

- **Foundations:** 0001 (solution and packages), 0005 (persistence and migrations), 0008 (settings), 0010 (versioning and branching), 0011 (testing strategy, including the start-up hang), 0014 (transitive advisories), 0019 (audit log and daily statistics), 0020 (public API surface).
- **Abuse and limits:** 0016 (identity and limits), 0021 (captcha).
- **Email:** 0042 (templates, preview, test email), 0052 (the queue), 0053 (email kinds), 0076 (new mail goes out at once).
- **The backoffice:** 0039 (one settings API, a frame for every tab), 0051 (polls and the backoffice session), 0064 (the accessibility audit), 0075 (the design refinement pass), 0079 (say it once).
- **Operations and release:** 0063 (SQLite under load), 0065 (transient database errors), 0067 (upgrade tests), 0068 (the public API approval test), 0077 (the release workflow), 0078 (the Umbraco 18 line).

| Area | Tandem source | In Jiron Form Builder |
| --- | --- | --- |
| Progress line, secret scan, hooks, local CI mirror, demo script, coverage merge, npm licence check, third-party notices, workflow check, audit-suppression check | `scripts/*.mjs`, `scripts/ci-local.ps1`, `scripts/demo.ps1`, `scripts/tests/*` | **Port** and rename: the fake marker `JFB_FAKE`, Jiron Form Builder's paths and ports, and no key file. |
| Build files | `Directory.Build.props`, `Directory.Packages.props`, `NuGetAudit.props`, `.editorconfig`, `global.json`, `NuGet.config`, `src/Directory.Build.*`, `tests/Directory.Build.props` | **Port.** |
| Database gate, migration plan, frozen snapshots, migration state, `SqlNames` | `src/…Core/Persistence/*`, Tandem ADR-0005 | **Port the patterns.** |
| Settings framework: typed sections, schema versions, host locks, cache refresher | `src/…Core/Settings/*`, Tandem ADR-0008 | **Port the patterns.** |
| Captcha verifiers, rate limits, outbound email queue, audit log, daily stats | `Captcha/*`, `Limits/*`, `Email/*`, `Audit/*`, `Stats/*` | **Port and adapt.** |
| Public API conventions: ProblemDetails codes, CORS, body and time limits, an OpenAPI snapshot test | `src/…Web`, Tandem ADR-0020 | **Port the patterns.** |
| Backoffice client: workspace, generated client, manifests test, Vite config | `src/…Web/Client/packages/backoffice` | **Port the patterns.** |
| Test harness: <ul><li>integration setup;</li><li>fake time and test users;</li><li>Playwright global setup and fixtures (`loginAs`, `apiAs`, `waitForMail`, the sign-in lock);</li><li>the E2E host, the start-up wait and the hang marker.</li></ul> | `tests/*`, described in the lessons file | **Port** that shape. |
| Email: the queue with an immediate hosted sender, Umbraco's sending gate, and HTML that survives Outlook and Gmail | `Email/*`, Tandem ADR-0042, 0052, 0053, 0076 | **Port the patterns** into the outbox (D8, 8.2). |
| Packing: one pack script for the gate, CI and the release; an allowlist of every file in every package; the pack-time asset check | `scripts/pack.mjs`, `scripts/check-packages.mjs`, the `TANDEM001` target, Tandem ADR-0077 | **Port** (3.2, 18.1, 18.2). |
| Install and upgrade tests: <ul><li>a fresh site from a private template hive;</li><li>a fresh package cache and a run-local feed, with a `NuGet.config` that clears inherited sources;</li><li>`install` and `upgrade` modes;</li><li>a baseline worktree with a seed;</li><li>the database read with `node:sqlite`.</li></ul> | `scripts/install-test.ps1`, `scripts/install-test/*`, Tandem ADR-0067 | **Port** (16.5, 18.3, 19.2). |
| Release: `release.yml` calls CI through `workflow_call` after a `prepare` job that checks the version and its changelog section; publishing is gated | `.github/workflows/release.yml`, `scripts/release.mjs` | **Port** (18.2). |
| Screenshots, the UX walk and the icons, made by Playwright on the demo with scripted data | `scripts/screenshots.mjs`, the `ux` and `screenshots` projects, `scripts/icons.mjs` | **Port** (the UX reviews, 17.1, 17.3). |
| The safe confirm helper, because Umbraco's confirm modal renders strings as HTML, plus the source test that bans the raw modal | `confirmText()` in the backoffice package (Tandem 10.1) | **Port** (9.1). |
| Serving client assets precompressed (Brotli and gzip) with a `?v=` fingerprint and caching headers | the asset middleware (Tandem 6.8) | **Port** (3.2, 6.2a). |
| The fake Umbraco.AI provider with scripted answers | `tests/Jiron.Umbraco.ChatSupport.Testing/FakeAI/` | **Port** (15.3). |
| CI (gated, with `main` and `v18/main` as push branches), Dependabot, issue and PR templates | `.github/*` | **Port.** |

**Porting cautions** (from the 1.1 inventory, [docs/research/tandem-reuse.md](docs/research/tandem-reuse.md)):
- **Never copy Tandem's fixed identifiers:** the cache refresher's `UniqueId`, job and HttpClient names, the email type, the secret prefix, Data Protection purposes, BroadcastChannel and Web Lock names, and the migration plan name. Both packages may run on one site. Jiron Form Builder chooses its own once, and never changes the stored ones (the plan name, Data Protection purposes).
- **Never port Tandem's email HTML check as a sanitiser:** it is a regular expression, and §4.10 needs an established library.
- **Fix what Tandem left behind when porting it:** its notices script ignores `NUGET_PACKAGES`, its install test stops one process instead of the tree and lacks the throwaway-build settings of CLAUDE.md rule 6, and its e2e start-up wait looks for a log line that disappears once the Content Delivery API is on (3.4, 3.5, 16.5).

**Tandem's lessons** are in [docs/research/tandem-lessons.md](docs/research/tandem-lessons.md):
- **its gotchas on this machine,** about Umbraco, SQLite, email, the backoffice, Playwright, packing and Windows;
- **the shape of its test harness** to port: projects, the Testing project, the E2E host with its start-up wait and hang marker, the Playwright projects, and the stages of `ci-local.ps1`.

Read the file in full in 1.1, and look up the parts a step touches before starting it. References below to "§3.3" mean the table above and that file.

### 3.4 Umbraco 17 package facts (research 2026-10-19; checked in Phase 1)

Each fact below was confirmed on this machine in the Phase 1 step it names. The few that only a later step can show name that step.

- **What the sandbox showed:** [docs/research/umbraco-backoffice.md](docs/research/umbraco-backoffice.md) has the confirmed shapes, signatures and traps from 1.2a and 1.2b. Its last section lists what they mean for Jiron Form Builder; every backoffice step reads it before it starts.
- **Templates:** `dotnet new install Umbraco.Templates@17.7.1`.
  - **Always in a private template hive:** pass `--debug:custom-hive %TEMP%\jfb-templates-<version>` to every `dotnet new install` and every `dotnet new` call. The user-wide template install is shared with the Tandem session and must never change (CLAUDE.md rule 6).
  - Pin 17.7.1 for the whole run. Only 18.3 (the 17.6 floor) and Phase 19 use other versions, each in its own hive.
  - `dotnet new umbraco-extension -n <Name> -ex -pm Central` creates an RCL with `Client/` (Vite library mode, Lit, TypeScript, `@umbraco-cms/backoffice`, `@hey-api/openapi-ts`), `StaticWebAssetBasePath=/`, and output to `wwwroot/App_Plugins/<Name>`. It does not run npm during `dotnet build`; run it in scripts and CI. Its `@hey-api/openapi-ts ^0.85.2` fails `npm install` against 17.7.1: pin 0.99.x, which also satisfies 17.5 (1.2a, 1.2b).
  - Test sites: `dotnet new umbraco -n Site --development-database-type SQLite`, then the admin through the unattended-install settings (`Umbraco__CMS__Unattended__InstallUnattended` and the `UnattendedUser*` values) in the start-up environment, so the password never appears on a command line (`scripts/sandbox.mjs`, 1.2a).
- **`umbraco-package.json`:** in `wwwroot/App_Plugins/JironFormBuilder/`, with `id` = NuGet id. Since 17.6, `version` drives cache busting, so stamp it at build time. The floor 17.6.0 has it (ADR-0074), and 18.3 checks an upgrade on the floor. Register the manifests with a `bundle` extension (1.2a, 1.2b).
- **Management API:**
  - Attributes: `[ApiController]`, `[BackOfficeRoute("jfb/management/api/v{version:apiVersion}")]`, `[MapToApi("jfb")]`, `[JsonOptionsName(Constants.JsonOptionsNames.BackOffice)]`, `[Authorize(Policy = …)]`, `[ApiVersion("1.0")]` on every concrete controller.
  - Register the OpenAPI document in one composer, because CMS 18 replaces Swashbuckle with Microsoft.AspNetCore.OpenApi.
  - The generated client takes its configuration from `umbHttpClient.getConfig()` (`@umbraco-cms/backoffice/http-client`), which brings the base URL and the credentials. The backoffice sends a placeholder bearer token and the server reads the real one from an HttpOnly cookie, so no script can read the token (1.2a, 1.2b).
- **Manifest types Jiron Form Builder uses** (all confirmed in 1.2a and 1.2b; shapes and traps in the research doc):
  - `section`, `sectionSidebarApp` (menu), `menu` and `menuItem`, `tree` and `treeItem` (forms with folders);
  - `workspace` (routable), `workspaceView`, `workspaceAction`, `workspaceActionMenuItem`, `workspaceContext`, `workspaceInfoApp`;
  - `entityAction` (create, rename, move, duplicate, trash, restore, delete), `entityCreateOptionAction`, `entityBulkAction`, `collection` with `collectionView` and `collectionAction` (entries);
  - `modal`, `dashboard`, `propertyEditorSchema` and `propertyEditorUi`, `localization`, `searchProvider` with `globalSearch`, `condition`, `icons`, `ufmComponent`.
  - The `tree` kind of `workspaceView` arrives in 17.6.0, so folder views host `<umb-tree>` in their own element (1.2b).
- **Building blocks to reuse rather than rebuild** (confirmed in 1.2a and 1.2b):
  - not the sorter controller: ADR-0016 chose our own pointer drag for the designer;
  - the Tiptap rich text input (`umb-input-tiptap`, the default RTE since 16);
  - the document, media and member pickers (`umb-input-document`, `umb-input-media`, `umb-input-member`);
  - the data type picker (`umb-data-type-input`), the user group picker (`umb-user-group-input`) and the member group picker (`umb-input-member-group`);
  - the code editor (`umb-code-editor`: `.code` and `.language`, and it needs a height);
  - `UMB_NOTIFICATION_CONTEXT` toasts and modals through `umbOpenModal`.
- **Property editors:** a `propertyEditorSchema`, which is a server `DataEditor`, plus a `propertyEditorUi`. Use a `PropertyValueConverterBase` for Razor, and an `IDeliveryApiPropertyValueConverter` for the Content Delivery API, which must be enabled with `Umbraco:CMS:DeliveryApi:Enabled` (1.2a).
- **"Used on" tracking:** `IDataValueReferenceFactory` / `IDataValueReference` report references from property values, and Umbraco's walk covers blocks. Umbraco stores references only between `umbracoNode` rows and drops a form's, so Jiron Form Builder keeps its own index, refreshed on content save, and shows it with Umbraco's `entityReferences` info app (1.2b, ADR-0017).
- **No-JavaScript posts** (confirmed in 1.4a, ADR-0004): the form posts back to its own page with Umbraco's encrypted route token `ufprt`, which routes the post to a `SurfaceController` action. `CurrentUmbracoPage()` renders the page again in the same request, and state reaches the view through `HttpContext.Items`. `RedirectToCurrentUmbracoPage(QueryString)` answers `302` to the page with the query but cannot add a fragment, so Jiron Form Builder builds its own redirect. `BeginUmbracoForm(…, antiforgery: false)` writes no token, but posts to the bare current path, always adds a multipart encoding and a random `id`; `Html.SurfaceControllerHiddenInput<T>()` gives the same routing inside our own `<form>`. `[IgnoreAntiforgeryToken]` on the action overrides the controller's `[AutoValidateAntiforgeryToken]`. `CurrentPage` throws when the action is reached through the auto-route `/umbraco/surface/…`, so the action checks the `UmbracoRouteValues` feature first. A tampered `ufprt` never reaches the action: Umbraco renders the page.
- **Content creation** (the "create content" action): the async `IContentEditingService` / `IContentPublishingService` of 14+ rather than the legacy `IContentService` (confirmed in 1.2a).
- **Seeding a site** (found in 3.3, ADR-0028): `ITemplateService.CreateAsync` with a `TemplateOnDisk` writes the committed view back with a UTF-8 byte-order mark, so a seeder removes it afterwards. A page published in two cultures before its root has domains makes Umbraco warn on every request, so create the pages, assign the domains, then publish. Public access shows the sign-in page at the protected address (an internal rewrite), so a menu marks the current page from the request path. Members sign in and out through Umbraco's own `UmbLoginController` and `UmbLoginStatusController`.
- **Members:** `IMemberManager` (Umbraco.Web.Common) for the current member, `IMemberService` and `IMemberGroupService` (Umbraco.Core), all present in 17.7.1 (1.8); the member picker and a member workspace view work (1.2a, 1.2b). 14.3 builds on them.
- **Files** (decided in 1.4b, ADR-0006): Umbraco's `IFileSystem` writes synchronously, and Kestrel refuses synchronous reads of a request body, so Jiron Form Builder stores files through its own asynchronous `IFormBuilderFileStore`. Umbraco's official Azure Blob provider (`Umbraco.StorageProviders.AzureBlob`, 17.1.0 for 17) registers named containers with `AddAzureBlobFileSystem(name, configure)`, settings in `Umbraco:Storage:AzureBlob:{name}`, and `IAzureBlobFileSystemProvider.GetFileSystem(name).GetBlobClient(path)` gives the SDK's asynchronous client, which a blob store uses (the 15.2 sample). Without `ContainerRootPath` the provider prefixes every blob name with the virtual path; the plain registration serves nothing.
- **Umbraco reads the form of every form post while routing** (found in 1.4b): its `EagerMatcherPolicy` applies to every endpoint and, when more than one matches a path (its content route always does), looks for `ufprt` in `Request.Form`. That reads a multipart body in full (64 KB in memory, the rest in the ASP.NET temp folder) before any action, for controllers outside `/umbraco` too, and adding the path to `ReservedPaths` does not prevent it. A middleware before routing (an Umbraco pipeline filter) that gives the request an `IFormFeature` with no form content does. Umbraco sets Kestrel's body limit and the multipart limit from `Umbraco:CMS:Runtime:MaxRequestLength` (in KB; by default 52,428,800 bytes and unlimited).
- **Section access in code** (found in 1.4b): Umbraco's section requirement is internal and the allowed-applications claim is obsolete in 17 (`CS0618`), so a requirement handler reads `IUser.AllowedSections` through `IAuthorizationHelper.TryGetUmbracoUser`. Authorization handlers are singletons: a scoped one fails start-up scope validation, because Umbraco's singleton event authorizers consume `IAuthorizationService`.
- **Text and time zones in .NET** (found in 1.5, ADR-0005): on Windows, .NET takes casing from the host's ICU and character data from its own tables, so letters added in recent Unicode versions case differently from host to host, and its invariant casing leaves U+0130 and U+0131 as they are. `string.Normalize` throws for U+FFFE, and `System.Text.Json` throws `InvalidOperationException` when it reads an escaped lone surrogate. `TimeZoneInfo.FindSystemTimeZoneById` resolves IANA ids on Windows through ICU. With invariant globalization, normalisation does nothing and IANA ids are not found on Windows. Found in 3.4b (ADR-0030): on Windows, `FindSystemTimeZoneById` also takes Windows names (`HasIanaId` is false for them), and an IANA name in another letter case once its exact spelling has been looked up, because its zone cache ignores case (found in 4.4b, ADR-0043), while the browser's `Intl` takes offsets such as "+01:00"; `DateTimeOffset.TryParse` and `new Date` read a time without a zone as local time, and `new Date` rolls 30 February over into March; Node 24 runs TypeScript files as they are when they use erasable syntax only and import with the `.ts` extension.
- **Health checks:** classes with `[HealthCheck]` appear on the Settings › Health Check dashboard, grouped by `Group` (confirmed in 1.2b). A status's action runs at once, without a confirmation, so only safe, repeatable actions go there. Messages are server strings that Umbraco shows as written.
- **Webhooks** (confirmed in 1.2b): an event class derives from `WebhookEventBase<TNotification>` with `[WebhookEvent(name, Constants.WebhookEvents.Types.Other)]` and is registered with `builder.AddWebhookEvent<T>()`; publishing the notification queues the request. A distributed job sends it (every 10 s by default) with the header `Umb-Webhook-Event: <alias>` and our payload as the body.
- **Localization** (confirmed in 1.2b): one `localization` manifest per culture (`en`, `de`); a regional culture such as `de-de` falls back to the language file. The global search tab label and server strings (health checks, webhook event names) are shown as written.
- **Database:**
  - `PackageMigrationPlan` (it auto-runs unattended; `Umbraco:CMS:Unattended:PackageMigrationsUnattended`) with `AsyncMigrationBase` steps (1.3, ADR-0003);
  - DTO attributes `[TableName]`, `[PrimaryKey]`, `[ExplicitColumns]`, `[Column]`, `[NullSetting]`, `[Length]`, `[SpecialDbType(NVARCHARMAX)]`, `[Index]`, `[ForeignKey]`;
  - store dates in UTC;
  - use `ICoreScopeProvider` and `IScopeAccessor`, and call `Complete()`;
  - a notification published through `scope.Notifications` reaches its handlers after the outermost scope commits, and never after a rollback (1.6, ADR-0007);
  - NPoco reads `@name` inside a SQL string literal as a parameter, so values always go in as parameters, and `PRAGMA` statements need their table-valued form (`SELECT … FROM pragma_journal_mode`) through `Fetch` (1.6).
- **Jobs:**
  - `IDistributedBackgroundJob` (`Name`, `Period`, `ExecuteAsync(CancellationToken)` from 17.5), registered with `AddSingleton<IDistributedBackgroundJob, T>()`. Settings live in `Umbraco:CMS:DistributedJobs`. Confirmed on 17.5.0 and 17.7.1 in 1.2b: the parameterless `ExecuteAsync()` is still abstract, so implement both; `AlignToClock` (17.6.2) is not used.
  - The runner starts one due job per 5-second tick, in table order, with Umbraco's own jobs first, so a package job may wait (D8, Tandem ADR-0076). Measured in 1.6 next to Umbraco's ten distributed jobs: a 5-second job ran about every 20 s (5 to 50 s apart), a 1-minute job every 65 to 70 s, and the first job ran 109 s after the process started. **The runner waits for each job:** while one job slept for 70 s, no other distributed job ran on that server, so a slow job holds Umbraco's scheduled publishing back too (ADR-0007).
  - `IRecurringBackgroundJob` runs on every server.
- **Email:** `IEmailSender.SendAsync(EmailMessage, emailType, …)`. Check `CanSendRequiredEmail()` first: without SMTP settings Umbraco drops mail silently. Settings live in `Umbraco:CMS:Global:Smtp`, and `DeliveryMethod=SpecifiedPickupDirectory` suits tests. `EmailMessage` carries one body (HTML or text), so a multipart message with both is not possible through it: an HTML mail is one `text/html` part, a text mail one `text/plain` part, and attachments make `multipart/mixed` (1.6). Jiron Form Builder sends one body per mail (ADR-0007), and sites that replace `IEmailSender` (for example with a mail service) keep working. Measured in 1.6: `CanSendRequiredEmail()` is true with a host, a pickup folder or a `SendEmailNotification` handler, also when no From address is configured, and that send then throws; a pickup folder that does not exist throws `DirectoryNotFoundException`; MimeKit encodes non-ASCII subjects and drops line breaks in them, which glues the words together.
- **Integration tests:** `Umbraco.Cms.Tests.Integration` 17.x, `UmbracoIntegrationTest`, `UmbracoTestServerTestBase`, `[UmbracoTest(Database = NewSchemaPerTest)]`. Its 17.7.1 and 18.2.1 packages depend on NUnit 3.14.0 or later (1.8); D19 stays on NUnit 3, and 3.4 builds the harness on it. Found in 3.4a (ADR-0029): `UmbracoIntegrationTest.Setup` is not virtual, so a fixture sets configuration on `builder.Config` in `CustomTestSetup` before `AddComposers`; the test server keeps front-end routing in its start-up window and answers every page request with the maintenance result, whose view it lacks, so middleware tests put a stand-in for the site behind the middleware; its `IHostEnvironment` is "Production" and its `IWebHostEnvironment` "Tests", re-registered by the web host after Umbraco's set-up, so a fixture that needs Development registers `IHostEnvironment` in `CustomTestSetup`; the factory cache reuses a host across fixtures unless the database option is `NewSchemaPerTest` or `NewEmptyPerTest`; Umbraco works in `TEMP/` under the test project.
- **The browser tests' host** (found in 3.4c, ADR-0031): about a minute after it starts, Umbraco 17.7.1 rebuilds its indexes by running every registered `IIndexPopulator` in turn, and logs nothing when it is done. Its populators are registered in `AddExamineComposer`, and composers without an order between them run in any order, so a populator that must run last is registered by a composer with `[ComposeAfter(typeof(AddExamineComposer))]`. The integration host's base class registers Umbraco's populators a second time. "The Delivery API is not enabled" is logged only while the Delivery API is off. A site made without the template's "use Delivery API" option lacks `AddDeliveryApi()`, and with `Umbraco:CMS:DeliveryApi:Enabled` its requests fail with 500; with it, the API answers 401 while it is off.
- **Release schedule** (Umbraco's published plan, read 2026-10-19):
  - 17 LTS: until 2028-11-27;
  - 18 STS: 2026-06-25 to 2027-06-25;
  - 19 STS: planned 2026-12-10;
  - 21: the next LTS, planned 2027-12.
- **What 18 breaks** (Tandem's 18 line met it; Phase 19 checks Jiron Form Builder's): `MigrationBase`, `UmbracoApiController`, `ILocalizationService`, `IFileService` and some sync overloads are removed. Swagger moves to `/umbraco/openapi` (OpenAPI 3.1). From Tandem's 18 line (Tandem ADR-0078):
  - documents register through `AddBackOfficeOpenApiDocument` and are served at `/umbraco/openapi/{name}.json`; tests read them through `IOpenApiDocumentProvider`, keyed by name;
  - the new generator writes enums as numbers, integers as "integer or string" and every record constructor parameter as required, documents form files and validation problems differently, and needs a schema transformer to give the TypeScript client the same types as on 17;
  - the snapshots in `docs/api/` and the generated client therefore differ per line.
- **Static files:** Umbraco serves package files through `UseStaticFiles`, with no compression and no caching headers (measured in 1.4a), so the website runtime needs its own serving to meet §4.12 (6.2a). Packed as static web assets, precompressed copies break the build of every site that installs the package (SDK 10.0.401 records each copy's original with the packing machine's path), so the website assets are embedded in the Web assembly instead (3.2, ADR-0026).
- **Output caching:** sites may put pages behind output caching or a CDN. The markup of anonymous forms therefore contains nothing per visitor: the challenge is fetched when the visitor first interacts, and there is no antiforgery token (§4.10); members-only forms are rendered per member and never cached (ADR-0004). Umbraco's own website output cache (confirmed in 1.4a; not in 17.3.0, present in 17.5.0, 17.7.1 and 18.2.1):
  - opt-in with `Umbraco:CMS:Website:OutputCache:Enabled` (`ContentDuration` defaults to 10 s);
  - **the cache key ignores the query string**, so anything rendered from the query needs an `IWebsiteOutputCacheVaryByProvider` (a collection: a package adds rules without removing the site's);
  - `IWebsiteOutputCacheRequestFilter` is a single service, so a package must never register one;
  - only `Set-Cookie` or `no-store` block storage: the policy checks neither the method nor the status code (posts to a surface action were still never stored in 1.4a);
  - `IWebsiteOutputCacheTagProvider` (a collection) tags pages, and `IWebsiteOutputCacheManager.EvictByTagAsync` evicts them.

  ADR-0004 sets Jiron Form Builder's rules on top of it.

### 3.5 Umbraco Marketplace and NuGet facts (research 2026-10-19)

- **Listing requirements:** the installable package needs the NuGet tag `umbraco-marketplace` (only on the meta package) and must depend on `Umbraco.Cms.*`. Jiron Form Builder's meta package therefore references `Umbraco.Cms.Web.Website` `[17.6.0, 18.0.0)` directly, not only through Web, so the listing never depends on whether the Marketplace reads dependencies through our other packages (ADR-0001). The AI add-on, which is installed on its own, gets the tag too. Supported versions come from the dependency range (`[17.6.0,18.0.0)` = 17 only). Non-OSI and commercial licences are accepted; the listing is about the package, not its licence type.
- **Sync:** new packages are picked up daily at 04:00 UTC, and known ones every 2 h.
- **`umbraco-marketplace.json`:**
  - It is read from the root that `PackageProjectUrl` points to, so **it cannot be read while the repo is private**.
  - Schema: `https://marketplace.umbraco.com/umbraco-marketplace-schema.json`; validator: `https://marketplace.umbraco.com/validate`.
  - Fields:
    - `Title` (at most 256 characters), `Description` (at most 4,000);
    - `Category` and `AlternateCategory`, each one of "Analytics & Insights", "Artificial Intelligence", "Campaign & Marketing", "Commerce", "Developer Tools", "Editor Tools", "Headless", "PIM & DAM", "Search", "Themes & Starter Kits" and "Translations" (the schema's list, confirmed 2026-10-23 in 1.8). **There is no "Forms" category**: use "Editor Tools", plus "Campaign & Marketing";
    - `AuthorDetails`, `DocumentationUrl`, `IssueTrackerUrl`, `DiscussionForumUrl`, `VideoUrl`, `Screenshots`, `Tags`;
    - `PackageType` (`Package` | `Integration`; `Package`), `LicenseTypes` (`Free` | `Purchase` | `Subscription`; this is about price, so `["Free"]`);
    - `VersionDependencyMode` (`Default` | `SemVer`): `Default`, because the dependency range has an upper bound. The deprecated `LicenseType` and `AlternatePackageNames` stay unused;
    - `RelatedPackages`, `IsSubPackageOf`, `PackagesByAuthor`, `VersionSpecificPackageIds`;
    - `LookingForContributors`, `LookingForMaintainer`, `AddOnPackagesRequiredForUmbracoCloud`.
  - `additionalProperties: false`.
- **Icon:** the NuGet `PackageIcon` (PNG or JPEG, < 1 MB, 128×128 recommended).
- **NuGet licence rules:**
  - **Licence expressions must be OSI- or FSF-approved.** PolyForm Perimeter has no SPDX id and is not OSI-approved, so packages use `PackageLicenseFile` with the packed `LICENSE.md`. Set only one licence property.
  - `PackageRequireLicenseAcceptance` shows a dialog only in Visual Studio; `dotnet add package` does not prompt. Jiron Form Builder sets it to `true` as a visible signal of a non-standard licence.
- **Other NuGet settings:** `PackageReadmeFile` (.md), `PackageIcon`, `IncludeSymbols` + `snupkg`, `EnablePackageValidation`, and `ContinuousIntegrationBuild` in CI. SourceLink is built in. `PublishRepositoryUrl=false` while the repo is private. It only stops Source Link from filling `RepositoryUrl` from git; an explicitly set `RepositoryUrl` still lands in the package (Tandem 13.1). That is harmless, because nothing is published while the repository is private, but don't rely on the setting to hide anything.
- **Trusted publishing:** `NuGet/login@v1` OIDC with a 1-hour temporary key, set up on nuget.org per owner, repo and workflow file. Gate the push with a repository variable and keep it off (D17).

### 3.6 Licensing, naming and clean-room facts (research 2026-10-19; not legal advice)

- **The licence (D2):** PolyForm Perimeter 1.0.1. Use `SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1` in file headers where headers are used.
  - **What it allows:** any purpose except "providing to others any product that competes with the software". Changes and distribution are allowed, and there is a patent licence and a 32-day cure period for first violations.
  - **What "competes" covers:** a product competes when it is marketed as a substitute for the functionality or value of the software, even if free.
  - **What is fine:** a client website built with Jiron Form Builder does not compete; neither do hosting it, add-ons that extend it, or a GitHub fork made to send a pull request.
  - **What is not:** publishing a copy or a changed version for others to use as an alternative, even a free, open-source one. That is a competing product. Ideas stay free: anyone may build their own form builder from scratch.
  - **Why not PolyForm Shield:** Shield also forbids competing with any product the licensor or its affiliates provide *using* the software. Because the owner's own studio may build client sites with Jiron Form Builder, Shield could turn every other agency into a "competitor". Perimeter avoids that.
  - **Why not the others:**
    - Commons Clause can catch paid setup and support work;
    - the Elastic License 2.0 allows selling copies and can catch agency hosting;
    - FSL turns every version into MIT after two years;
    - PolyForm Noncommercial forbids company use;
    - BSL needs a custom grant.
- **Wording:** a licence that forbids competing products is **not** OSI "open source" (OSD criteria 1 and 6). Jiron Form Builder says "free and source-available", never "open source". "No licence" would mean all rights reserved, so nobody could use it.
- **Contributions:** GitHub's terms make contributions inbound = outbound, which under Perimeter would leave the owner unable to relicense contributed code. Outside contributions therefore wait for a **contributor licence agreement**, an OWNER-TODO item with a lawyer:
  - under German law it must be a broad, sublicensable licence grant (§§ 29, 31(5), 31a UrhG), not an assignment;
  - it includes an original-work attestation.
  - **Decided by the owner on 2026-11-26 (ADR-0265): no agreement.** Contributions are accepted as GitHub's terms bring them, under Perimeter; relicensing contributed code would need its authors' consent. `CONTRIBUTING.md`, the commit check and CI are open to contributors.
- **The Cyber Resilience Act** (EU 2024/2847): reporting duties apply from 2026-09-11 and most obligations from 2027-12-11. Non-monetised software is generally outside its scope; any monetisation (paid add-ons, paid support, possibly donations) needs a check first (OWNER-TODO). Jiron Form Builder's `SECURITY.md` and vulnerability handling follow good practice either way.
- **The Umbraco name:** Umbraco's brand rules forbid "Umbraco" in product names without permission and any suggestion of being part of Umbraco A/S.
  - The package id uses "Umbraco" only as a platform qualifier after the owner's prefix (`Jiron.Umbraco.FormBuilder`), a common pattern for community packages. The product name "Jiron Form Builder" never contains it.
  - Never use the Umbraco logo, never use "Umbraco" in paid ads, and always carry the non-affiliation notice (Appendix A).
- **The name** was decided on 2026-10-20 after name research. Short brand-style names either clashed with existing products or came too close to them, so the product carries a descriptive name under the owner's own name, which leaves nothing to clash with. The NuGet prefix `Jiron.` is reserved by the owner (OWNER-TODO), which also gives the packages the verified mark. The build never renames anything on its own.
- **Clean room:**
  - **What may be reimplemented:** functionality, ideas, logic, data formats and programming languages are not protected by copyright (EU Software Directive Art. 1(2), CJEU *SAS v WPL* C-406/10, § 69a(2) UrhG, 17 U.S.C. § 102(b)).
  - **What is protected:** code (including shipped scripts, styles and views), documentation text (the *SAS* follow-up judgment found infringement by copying manuals), graphics and icons, and possibly a distinctive screen design (CJEU *BSA* C-393/09).
  - **Unfair competition:** in Germany, close imitations can also be unfair competition (UWG § 4 Nr. 3) even without copyright. Jiron Form Builder therefore designs its own screens and never uses screenshots of other products as design references.
  - **Licence terms:** commercial licence terms may forbid reverse engineering, and US courts enforce such terms. Hence CLAUDE.md rule 2: never install, run, unpack or decompile another product.

### 3.7 Privacy, accessibility and email facts (research 2026-10-19; not legal advice)

- **GDPR:** the site is the controller. Features that help it:
  - minimisation (store-nothing mode, no IP by default, sensitive fields);
  - storage limitation (retention with purge, including files and emails);
  - data subject rights (find, export, erase across forms);
  - proof of consent (Art. 7: never pre-ticked, with the exact text stored);
  - security (Art. 32: permissions, audit, protected uploads).
- **Consent checkboxes must never be pre-ticked** (CJEU *Planet49*). A consent field's default is always unchecked, and the designer does not offer a "checked" default for it.
- **Third-party captchas in the EU** load scripts from third parties and transfer data abroad. Under TDDDG § 25 that needs consent unless strictly necessary. Hence the consent modes in §4.10 and no third party by default (D14).
- **Web fonts from third-party CDNs** have been held unlawful without consent in Germany (LG München I, 2022, about Google Fonts). Jiron Form Builder ships no external fonts (CLAUDE.md rule 4).
- **Double opt-in:** in Germany, newsletter sign-ups need verified consent for email advertising (UWG § 7). That is why email verification is built in (§4.4).
- **Accessibility:** the European Accessibility Act has applied since 2025-06-28 to many consumer services, including e-commerce. WCAG 2.2 AA forms help sites meet it (§4.12).
- **Shipping legal text:** ship a **checklist** plus **clearly labelled templates** (DE and EN) and a factual sheet of what the package does. Never claim that anything is "compliant".

### 3.8 The owner's private folder

`.private/` is git-ignored (CLAUDE.md rule 2). The owner may keep reference material there, the term list for `scripts/term-guard.mjs`, and the private version of the goal.

- Read it only when the goal or step 1.1 says so.
- Keep any mapping or notes about it inside `.private/`.
- Never quote it, name what is in it, or derive tracked text from it word for word.
- The roadmap's own requirements (§1, §2, §4) stand on their own and need nothing from `.private/` to be built.

---

## 4. Target design

Phase 1 turns this design into `docs/architecture.md` and ADRs, and Phase 2 turns §4.13 into concrete screens. Where research on the machine shows a better way, change it there and record why.

### 4.1 Solution layout

```
Jiron.Umbraco.FormBuilder/
├─ LICENSE.md  README.md  CLAUDE.md  ROADMAP.md  CHANGELOG.md  SECURITY.md  SUPPORT.md
├─ CONTRIBUTING.md  CODE_OF_CONDUCT.md  THIRD-PARTY-NOTICES.md
├─ umbraco-marketplace.json             # Marketplace metadata (read from the repo root once public)
├─ Jiron.Umbraco.FormBuilder.slnx
├─ Directory.Build.props  Directory.Packages.props  NuGetAudit.props  global.json  NuGet.config  .editorconfig
├─ assets/                              # logo.svg (master), icon-512/256/128/64.png, brand.md
├─ schemas/                             # jfb-form.v1.schema.json, jfb-actions/..., shipped and tested
├─ docs/                                # user and developer docs (Phase 17), decisions/, research/, ux/, privacy/, screenshots/
├─ scripts/                             # roadmap-status, secret-scan, term-guard, install-hooks, ci-local.ps1, demo.ps1, install-test.ps1, screenshots, icons, notices, licences
├─ src/
│  ├─ Jiron.Umbraco.FormBuilder.Core/   # domain: definitions, field/action/option registries, rules + validation + merge-tag engines,
│  │                                       # entries, actions, email outbox, settings, permissions, persistence (NPoco), jobs, notifications
│  ├─ Jiron.Umbraco.FormBuilder.Web/    # RCL: management API, public API, rendering (view component, tag helper, surface controller),
│  │  │                                    # themes (Razor views + CSS), property editors, value converters, health checks, static assets
│  │  └─ Client/                           # npm workspace:
│  │     ├─ packages/logic/               #   rules, validation, merge tags in TypeScript (shared by runtime, element and backoffice)
│  │     ├─ packages/runtime/             #   the website runtime that enhances Razor-rendered forms
│  │     ├─ packages/element/             #   <jfb-form> web component for headless sites
│  │     └─ packages/backoffice/          #   Lit + TypeScript backoffice extension
│  ├─ Jiron.Umbraco.FormBuilder/        # meta package (no code): references Core and Web; the one sites install; tagged umbraco-marketplace
│  └─ Jiron.Umbraco.FormBuilder.AI/     # optional add-on on Umbraco.AI (Phase 15)
├─ samples/Jiron.Umbraco.FormBuilder.Samples/   # compiled extension samples (Phase 15)
├─ tests/
│  ├─ Jiron.Umbraco.FormBuilder.Tests.Unit/
│  ├─ Jiron.Umbraco.FormBuilder.Tests.Integration/
│  ├─ Jiron.Umbraco.FormBuilder.Testing/        # stubs (webhook receiver, captcha verifiers, fake AI provider), fake time, fixtures; never packed
│  ├─ vectors/                                    # shared JSON test vectors: rules, validation, merge tags, calculations (D10)
│  └─ e2e/                                        # Playwright Test: website, backoffice, email, privacy, a11y, screenshots
├─ demo/Jiron.Umbraco.FormBuilder.Demo/         # Umbraco 17 demo site with seeded en/de content and forms
└─ .github/  workflows/ci.yml  workflows/release.yml  dependabot.yml  ISSUE_TEMPLATE/  pull_request_template.md  CODEOWNERS
```

### 4.2 Modules and dependencies

```
Jiron.Umbraco.FormBuilder (meta) ──► Umbraco.Cms.Web.Website [17.6,18) (directly, for the Marketplace)
  └─ FormBuilder.Web ──► FormBuilder.Core ──► Umbraco.Cms.Core / Infrastructure [17.6,18)
         └──────────► Umbraco.Cms.Web.Common, Web.Website, Api.Common, Api.Management, Api.Delivery [17.6,18)
Jiron.Umbraco.FormBuilder.AI (optional) ──► FormBuilder.Web, FormBuilder.Core, Umbraco.AI (ADR-0018)
```

- **Core** holds every rule of the product and knows nothing about HTTP. Services take and return domain types. Every public service has an interface. An architecture test fails the build when Core references an ASP.NET Core HTTP, MVC, routing or Razor assembly, or an Umbraco web or API package (ADR-0001).
- **Web** hosts the HTTP surfaces, rendering and static assets. All Umbraco-major-specific glue (OpenAPI registration, auth policies, manifest helpers) lives in clearly named classes, so the 18 port touches as few files as possible.
- **Client packages:**
  - `logic` has no DOM and no dependencies, and is tested against `tests/vectors/`;
  - `runtime` and `element` share `logic` and the markup contract (§4.3);
  - `backoffice` uses `logic` for the designer's live rule preview and the form check.

### 4.3 The form definition model (schema v1)

A form definition is one JSON document, validated against `schemas/jfb-form.v1.schema.json` and upgraded by code when `schemaVersion` changes. ADR-0002 fixes the exact shape: every property with its default, the form settings and each built-in type's settings. In outline:

```jsonc
{
  "schemaVersion": 1,
  "key": "guid",                                   // the name, alias and folder live on the form row (ADR-0002)
  "settings": { /* §4.3.4 */ },
  "pages": [{
    "key": "guid", "title": { "": "Your details", "de-DE": "Ihre Angaben" }, "description": null,
    "rules": [],                                    // show/hide (skip) the whole page
    "groups": [{
      "key": "guid", "title": null, "description": null, "columns": 2, "style": "plain|card",
      "rules": [],
      "fields": [{
        "key": "guid", "alias": "email", "type": "email", "span": 1,
        "label": { "": "Email" }, "hideLabel": false, "help": null, "placeholder": null,
        "required": true, "messages": { "required": null, "invalid": null },
        "defaultValue": null, "prefill": { "query": null, "member": null, "memberMode": "show|hide" },
        "sensitive": false, "includeInEmails": true, "keep": "always|untilActionsRun",
        "autocomplete": "email", "cssClass": null,
        "validation": [ { "kind": "pattern", "pattern": "...", "message": { "": "..." } } ],
        "rules": [ { "key": "guid", "effect": "show|hide|require", "enabled": true, "match": "all|any", "conditions": [] } ],
        "options": null,                            // choice types: { "source": "items|list|content|…", "items": [ … ] }
        "settings": { /* type-specific, validated by the field type */ }
      }]
    }]
  }],
  "validationRules": [ /* form-level: rule + target field + message, for cross-field checks */ ],
  "actions":   [ { "key": "guid", "type": "email", "name": "Notify sales", "enabled": true, "stopOnFailure": false,
                   "trigger": "submitted|approved|rejected|verified", "rules": [], "settings": {} } ],
  "outcomes":  [ { "key": "guid", "kind": "message|redirect", "message": { "": "<p>Thanks!</p>" }, "showAnswers": false,
                   "redirect": { "content": null, "url": null, "delaySeconds": 0 }, "rules": [] } ]
}
```

#### 4.3.1 Conventions

- **Keys and aliases:**
  - Every element has a stable GUID `key`. Entries, rules and actions refer to fields by key, never by alias or position.
  - The form's own name, alias and folder live on `jfbForm`, not in the document, so restoring a version never renames a form. The form alias follows the field alias pattern and is unique among all forms, trashed ones included (ADR-0002).
  - `alias` is unique within the form, matches `^[a-z][a-zA-Z0-9]{0,63}$`, and is not reserved: `form`, `entry`, `page`, `member`, `query`, `context`, `site`, `date` and `ufprt` (Umbraco's route token for no-JS posts). Jiron Form Builder's own inputs (`jfb-…`) and the honeypot's name always contain a `-`, which no alias can, so none of them collides with a field (ADR-0012).
  - An alias is generated from the label (camelCase). It can be edited until the first published version has entries; after that, changing it asks for confirmation and keeps the old alias as a merge-tag alias.
- **Localised text:** every editor-written text is a `LocalizedText`, a map from culture (`""` = default) to text.
  - The visible text resolves as the request culture → its neutral culture → default.
  - A value of exactly `#key` resolves through the Umbraco dictionary.
- **Limits** (host-configurable): 30 pages, 100 groups, 300 fields, 1,000 options per field, 50 actions, 50 outcomes and 200 rules per form (ADR-0034). A definition of at most 2 MB.
- **The public projection** (what the website and the headless API get) is built from an allowlist (ADR-0002). It never contains:
  - actions, outcomes (the submission's answer carries the outcome) or recipients;
  - option-source configuration. Options from a library list or another source are resolved when the form is rendered, so editing a list needs no publish;
  - prefill-from-member mappings beyond the field;
  - anything marked internal.

  The **public revision** changes only when the public projection changes (§3.1). A submission against an older revision is validated against the current version. It gets the usual answer when every error falls on a field the posted revision also had, and `form_changed` when an error falls on a field the visitor never saw, a field the visitor answered no longer exists, or a consent text the visitor agreed to has changed (ADR-0002).

#### 4.3.2 Field types (built in)

Each type declares: alias, icon, group in the palette, storage kind (`text`, `longText`, `number`, `date`, `bool`, `file`, `json`, `none`), which common properties it supports, a settings schema (rendered by the generic settings editor), server validation and parsing, the Razor partial, and its client behaviour. Types are registered in a collection, so sites and add-ons can add, replace or remove types.

| Palette group | Type (alias) | Type-specific settings |
| --- | --- | --- |
| **Text** | Text (`text`) | min/max length (default 255), prefix and suffix text, character counter |
| | Long text (`textarea`) | rows, auto-grow, min/max length (default 4000), min/max words, character counter |
| | Email (`email`) | "ask twice" confirmation input, allowed or blocked domains |
| | Phone (`phone`) | default country code, lenient international format check |
| | Web address (`url`) | allowed schemes (`https` default, `http` optional) |
| **Numbers and dates** | Number (`number`) | min, max, step, decimals, unit (prefix/suffix) |
| | Date (`date`) | min/max as absolute dates or relative ("today", "+2 days", "+1 year"), weekdays only, excluded dates (holidays), default to today; a native date input |
| | Time (`time`) | min, max, step in minutes |
| | Date and time (`datetime`) | the date and time settings combined |
| | Rating (`rating`) | 3 to 10 symbols, symbol (star, heart, circle), labels for both ends |
| | Scale (`scale`) | from 0 or 1 to 5 to 10 (NPS style), labels for both ends |
| | Slider (`slider`) | min, max, step, unit, show value |
| | Calculation (`calculation`) | a safe formula (`+ - * / ( )`, `min`, `max`, `round`, `if`, and `daysBetween`, `yearsBetween` for dates) over number, choice, rating and date fields; display format; visible or hidden. The server recomputes it and its value wins. |
| **Choices** | Dropdown (`dropdown`) | prompt text, allow multiple (native list box), option source |
| | Radio buttons (`radio`) | layout (vertical, horizontal, or cards, which show an option's image when it has one), an "Other" option with free text, option source |
| | Checkboxes (`checkboxes`) | layout as for radio, min/max selections, "Other" option, option source |
| | Grid (`grid`) | rows (statements) rated on shared columns (a scale or an option list), one or several choices per row, all rows or chosen rows required. Each row is its own fieldset, so it works without JavaScript and reads well with a screen reader; on phones the rows stack, one question each. |
| | Ranking (`ranking`) | options to put in order, all of them or the top N. "Move up" and "move down" buttons with live announcements, never drag only; without JavaScript, a position number per option, checked for unique positions on the server. |
| | Checkbox (`checkbox`) | text beside the box (rich text with links); required means it must be ticked |
| | Yes/No (`yesno`) | labels, style (two buttons or a switch) |
| | Consent (`consent`) | consent text (rich text with links to the privacy page), required by default. **The exact text shown is stored** (§4.5 `jfbConsentText`), so it may use only merge tags that are the same for every visitor (ADR-0014), and the form posts the hash of the wording it showed, so a changed wording is never stored as agreed (ADR-0009). |
| | Country (`country`) | allowed countries, countries on top, default; names come from `Intl.DisplayNames` in the browser and the server's culture data, so no country data file is needed |
| **People** | Name (`name`) | parts (title, first, middle, last), with the right autocomplete tokens |
| | Address (`address`) | parts (two street lines, postcode, city, region, country), required parts, autocomplete tokens |
| **Files** | File upload (`file`) | allowed type groups (images, PDF, Office documents, text, archives) plus custom extensions, max size per file, max files, a drag-and-drop zone, image thumbnails, "offer the camera" on phones |
| | Signature (`signature`) | pad size; **"type your name instead"** as the keyboard and screen-reader path; stored as a PNG file |
| **Structure** | Repeatable group (`repeater`) | child fields (no nested repeaters), min/max rows, "add" and "remove" labels; values stored per row |
| | Hidden (`hidden`) | value from a merge tag (for example `{{ query.utm_source }}`), opt-in query prefill. Values from server-only sources (member, context) are resolved when the entry is submitted and never rendered into the page. |
| **Content** (no value) | Heading (`heading`) | level h2 to h4 |
| | Text block (`paragraph`) | rich text (sanitised) |
| | Divider (`divider`) | none |
| | Image (`image`) | media item, required alt text, size |
| **Protection** | Captcha placement (`captcha`) | marks where the form's chosen captcha renders; without it, it renders before the submit button (§4.10) |

**Settings every input type shares** (shown only when the type supports them):

- label (and "hide label" with a visually-hidden fallback), help text, placeholder;
- required and its message;
- default value (merge tags), prefill;
- sensitive, include in emails;
- keep the value always, or **only until the actions have run** (§4.4);
- autocomplete token, column span, CSS class;
- rule, validation.

**Place limits:** dropdown, radio buttons and checkboxes can give each option a number of places (workshop seats, time slots, sizes). Visitors see "3 places left" or "full", and a full option is disabled with that text as its reason. How places are counted is in §4.4.

**Field types are greyed out with a reason** when they need configuration that is missing, for example a captcha provider without keys.

#### 4.3.3 Rules, validation, merge tags and calculations (one logic, two runtimes, D10)

- **Rules:**
  - A rule is `{ "key": guid, "effect": "show|hide|require|run|use", "enabled": true, "match": "all|any", "conditions": [ … ] }`. Each element holds a list of rules, at most one per effect (`show` and `hide` exclude each other), so a field can be shown under one rule and required under another (ADR-0002). A condition is one of:
    - `{ "field": key, "op": …, "value": … }`, where the value can also be `{ "field": key }` to compare two answers ("departure after arrival");
    - `{ "context": "culture|memberSignedIn|memberGroup|query.<name>|today", "op": …, "value": … }`, for the page's language, the member, an allowed query value and the date in the site's time zone;
    - a nested group, at most 10 levels deep, the rule's own conditions being the first (ADR-0061).
  - **`require`** applies to fields: the field is required while the rule matches ("phone is required when the contact method is phone").
  - **Switched off:** a rule with `enabled: false` is kept but ignored by both runtimes, so editors can try a form without it.
  - **Context in cached pages:** the server always decides. The page's culture is part of the markup; the member values come with the challenge response (§4.6), because cached markup holds nothing per visitor (§3.4).
  - **Operators:**
    - `equals`, `notEquals`, `contains`, `notContains`, `startsWith`, `endsWith`;
    - `isEmpty`, `isNotEmpty`, `isOneOf`, `isNoneOf`;
    - `greaterThan`, `lessThan`, `between` (numbers and dates, and relative dates like "today");
    - `isChecked`, `isNotChecked`;
    - `countAtLeast`, `countAtMost` (multi-choice and files);
    - `matches` (a safe pattern).
  - **How values compare** (ADR-0005): answers are cleaned (lone surrogates and noncharacters replaced, NFC, an explicit whitespace list trimmed) and empty ones dropped. Text compares without case, folded one character at a time in the Latin, Greek, Cyrillic and Armenian blocks and the full-width Latin letters (other scripts compare exactly, because runtimes case their new letters differently); option keys compare exactly; numbers and dates compare by value, and an invalid value never matches. `equals` and `isOneOf` look for some answer, comparisons take the first answer, bounds are inclusive, and every negation is the exact opposite.
  - **What rules apply to:** fields, groups, pages, actions, outcomes and form-level validation rules.
  - **Hidden means gone:** a hidden field is never required, never validated and never stored, and the server strips it even if posted. A rule that depends on a hidden field sees it as empty.
  - **A whole form at once** (ADR-0061): both runtimes decide visibility in dependency order (an element after what holds it and after the fields its show or hide rule looks at), then what is required, the validation rules' messages, the actions that run and the outcome (the first whose `use` rule matches or that has none). A repeatable group's child fields are decided row by row: there a rule sees its own row's answers, and elsewhere a child field's answers are those of every row in which it is shown.
  - **Publishing refuses** cycles, rules that point at missing fields, groups nested deeper than both runtimes read, and any condition that fails the check both runtimes run before they evaluate (ADR-0005): unknown operators, an operator the field's kind does not take, a malformed operand and an unsafe pattern. A draft keeps them, so an editor never loses work, and the form check lists each one with a fix; the preview shows a broken rule as an issue instead of evaluating it (ADR-0002).
- **Validation kinds** (the error kinds of ADR-0002, which messages, statistics, the insights beacon and the vectors use):
  - `required`, `invalid` (a value the type cannot read, such as a date that is no date, or whose form is wrong, such as a malformed email address, ADR-0069), `minLength`/`maxLength`, `minWords`/`maxWords`, `min`/`max`, `step`, `confirmation` (an email address typed again that is missing or another, ADR-0062), `digits` (a number with more than 15 digits in all or after the point, ADR-0069; `too_many_digits` in the spike's vectors);
  - `dateWindow`, `selections`, `fileType`/`fileSize`/`fileCount`, `full` (no places left on a chosen option), `rowCount` (a repeatable group with fewer rows than its least or more than its most, ADR-0071);
  - the checks of a field's `validation` list: `pattern`, `matchesField`, `uniquePerForm`;
  - plus form-level validation rules ("show this message on field X when the rule matches").
  - **Where they live** (ADR-0002): the limits a field type owns (lengths, ranges, steps, date windows, selections, file rules) are its settings, with custom messages per error kind; a field's `validation` list holds only `pattern`, `matchesField` and `uniquePerForm`.
  - **Patterns** run on Jiron Form Builder's own matcher in both runtimes, never on a regular-expression engine (ADR-0005): a small subset of the usual syntax, parsed and compiled to an automaton of at most 10,000 instructions that reads code points in time linear in the value's length, so no pattern can hang a browser or a server (ADR-0030). A pattern matches the whole value and is case-sensitive; publishing refuses anything outside the subset and its limits (`unsafe_pattern`), and a draft keeps it while the form check lists it (ADR-0002).
- **Merge tags:** `{{ source.path | filter: arg }}`.
  - **Sources:**
    - `field.<alias>` (the answer as its type displays it), `field.<alias>.value` (its wire form), `field.<alias>.label`, `field.<alias>.<part>` (a part of a name or an address), `field.<repeater>.<child>` (a list across the rows; ADR-0066);
    - `entry.number|reference|date|url|state|answers`, `form.name|alias`;
    - `page.name|url|<propertyAlias>` (with `page.up.<alias>` to search the ancestors);
    - `member.name|email|<propertyAlias>`;
    - `query.<name>` (only where the form allows query values), `context.<key>` (values the developer passes when rendering or submitting);
    - `site.name|url`, `date.now`, `dictionary.<key>`.
  - **Filters:** `default`, `upper`, `lower`, `truncate`, `date`, `number`, `currency`, `join`, `html`, `url`, `json`.
  - **Values:** a source gives text, or text with the wire number or date behind it (the answer as displayed and its value), or a list of them, so `number`, `currency`, `date` and `json` read the value and every other filter the text (ADR-0064).
  - **Escaping:** each place that renders a template has one context (`html`, `text`, `header`, `url`, `json`, `jsonString`, `form` or `xml`), whose encoder runs on every value a tag produces; explicit filters combine with it without a breakout (ADR-0014). A JSON body is one `json` context whose scan knows whether a tag stands inside a string literal (ADR-0064).
  - **Where a tag may stand** (ADR-0064): each context reads the template's own text, never a value, and refuses a tag where a value could become code, markup or an address's host: in HTML and XML only in text content, read as the HTML tokenizer reads it (where a browser's reading depends on the whole document, every later tag is refused); in a URL only after its path starts, and never in an address with another scheme than `http` or `https`; in JSON not right after a backslash in a string.
  - **Problems:** unknown tags render empty and raise a form-check warning; malformed or misplaced ones render as their text and are a form-check error, each with the position where it starts. A template has at most 200 tags, a tag 10 filters, and the output 100,000 UTF-16 code units. Nothing throws.
  - **Cache-safe visitor texts:** before submission, visitor texts use only what is the same for every visitor on the page, plus answer recall; `member`, `context` and `entry` belong to hidden fields, outcomes, mail and actions. Culture formats of dates and numbers come from the server as explicit patterns, so both runtimes print the same text (ADR-0014).
  - **Answer recall:** visitor texts (labels, help texts, text blocks, page titles and outcome messages) may use `field.<alias>` of a field above them or on an earlier page ("Thanks, Anna. Where should we send the quote?"). The runtime updates them as the visitor types, the server renders the same text for no-JS posts and the summary page, and a hidden field resolves empty. A text that names a later field is a form-check error.
  - Developers add sources and filters through `IFormBuilderMergeTagSource` and `IFormBuilderMergeTagFilter`.
- **Calculations:** a tiny expression language (ADR-0063): numbers in the wire form, fields by their alias, `+ - * / ( )`, one comparison per level (`= != < <= > >=`, giving 1 or 0), and the functions `min`, `max`, `round`, `if`, `and`, `or`, `not`, and `daysBetween`, `yearsBetween` over date fields and `today()` in the site's time zone, parsed into a tree. It has no evaluation of arbitrary code and no reflection. Number, choice and checkbox fields give numbers (several answers add up, none is 0), date fields give dates; an unreadable answer, an empty date or a failed division leaves the calculation without a value. A formula takes at most 1,000 characters and nests at most 20 levels; its mistakes are `invalid_formula`, `unknown_operator`, `invalid_operand`, `too_deep`, `out_of_range`, `unknown_field`, `operator_not_for_kind`, `constant_formula` and `cycle`, each with the position where it starts. The server recomputes every calculation, and its result is stored, rounded to its `format.decimals` (ADR-0005). Numbers are exact decimals in both runtimes: division keeps 20 places and `round` rounds half away from zero (ADR-0005).
- **Wire forms** (ADR-0005): invariant and strict. Numbers have an optional minus, no leading zeros, no exponent and no culture; dates are `YYYY-MM-DD`, times `HH:mm(:ss)`, date-times local wall-clock `YYYY-MM-DDTHH:mm(:ss)`; a checked checkbox is "true". "Today" is the local date of the injected instant in the site's time zone; the instant is `YYYY-MM-DDTHH:mm:ss`, an optional fraction and `Z` or an offset, and the zone an IANA name (ADR-0030).
- **Shared test vectors:** `tests/vectors/rules.json`, `validation.json`, `merge-tags.json` and `calculations.json` are run by NUnit against Core and by Vitest against `packages/logic`, in the `jfb-vectors/1` format of ADR-0005. Each is written as ASCII from a module in `tests/vectors/suites/` by `tests/vectors/build.mjs`; 3.4b started with `spike.json` (the 163 cases of 1.5) and `port.json` (ADR-0030), which 5.2 and 5.3 grow into these files. A case that passes in one runtime and fails in the other fails CI. A tables check compares fold, clean, recomposition and the merge tags' `upper` and `lower` between the runtimes for every code point.

#### 4.3.4 Form settings (grouped as they appear in the UI)

- **Submission:**
  - labels for submit, next and back;
  - **a summary page** ("check your answers") before submitting;
  - progress indicator (steps, bar, none);
  - store entries (always; never, actions only; or for N days);
  - manual approval;
  - **email verification** (double opt-in) before actions run;
  - save and resume (off by default; link valid for N days);
  - **the entry title:** a merge-tag line that names each entry in the lists (for example `{{ field.name }}, {{ field.company }}`), with sensitive fields left out;
  - **the entry reference:** a format of prefix, year and padded number (for example `REQ-2026-00042`), shown to the visitor as `{{ entry.reference }}`.
- **After sending:** ordered **outcomes**, each with an optional rule: a message (rich text with merge tags), a redirect (content page or URL with merge tags), or a message and then a redirect after N seconds. A message can also show **a copy of the answers** with a print button and a "fill in again" link (6.5).
- **Availability:**
  - open from and until;
  - maximum number of entries;
  - closed message;
  - members only (any member, or chosen member groups);
  - one entry per member;
  - "unique per form" on a chosen field.
- **Spam protection:** inherit the site default, or choose: standard, standard + proof of work, reCAPTCHA v2, reCAPTCHA v3 (threshold), hCaptcha. Plus strictness for the content rules.
- **Privacy:**
  - retention per state (submitted, approved, rejected, spam), in days;
  - IP handling (inherit, none, anonymised, full);
  - whether entries count in insights.
- **Appearance:**
  - theme;
  - required marker (mark required with `*`, mark optional with "(optional)", or none);
  - validation summary on or off;
  - choice layout defaults;
  - CSS class.
- **Developer:**
  - headless access (allow the public definition endpoint for this form);
  - allowed query values for prefill;
  - context values to store.

### 4.4 Entries, the submission pipeline and actions

- **Entry states:**
  - `Draft` (save and resume) → `Submitted`;
  - `Unverified` (waiting for email verification) → `Submitted`: an `Unverified` entry runs no action, and confirming queues its `submitted` and `verified` actions together, in the order shown;
  - `Submitted` → `Approved` or `Rejected` (manual approval; without it, entries are approved automatically and the `approved` trigger fires);
  - any state ↔ `Spam` (marking an entry "not spam" moves it to `Submitted` and runs its `submitted` actions then; on a form with email verification, an entry without `VerifiedUtc` goes to `Unverified` instead and gets the verification mail, within the visitor-address limits).

  Every change is a timeline event. A state change that a backoffice user makes can be undone for 10 seconds, so the actions and Umbraco webhook events it starts wait for that window; changes made by rules, actions, verification or automatic approval run at once (ADR-0023). Deleting is permanent: the entry, its values, files, notes, events, action runs and queued emails go together, and the audit log records that it happened. A test entry (`IsTest`, from preview) is kept out of exports, insights and actions unless the editor asks.
- **Entry numbers:** each form numbers its entries 1, 2, 3, … (`#1042`). The number appears in the backoffice and in emails (`{{ entry.number }}`), and it is unique per form.
- **The pipeline** for a submission (JavaScript, no-JS post or API):
  1. Resolve the form and its published version.
  2. **The gate:** on a members-only form, the post's origin first, so nothing is rendered for another site (ADR-0004, ADR-0012); then rate limits (a refusal, `429 rate_limited` with `Retry-After`; the runtime and the no-JS page keep every answer and say when to send again, ADR-0012); then the challenge's signature and expiry (`401 challenge_required`) and, on a members-only form, its member.
  3. **Idempotency, before anything that could refuse a retry:** a `submissionId` this form already has is answered from its entry only when the post carries that entry's challenge or comes from the server key that made it: the same digest returns the original receipt and outcome, a different one `409 duplicate_submission`, and the no-JS path shows the first entry's outcome. Any other post with that `submissionId` gets `409 duplicate_submission` with no receipt and no outcome. Nothing below runs for these, so a retry after a lost answer never meets `already_submitted`, its own `uniquePerForm` value, a full option or a closed form. The unique index on (FormId, SubmissionId) settles races.
  4. **Compatibility:** if the posted public revision is older but compatible, accept it; otherwise answer `form_changed` (the runtime refreshes the form, §3.1). The posted hash of every consent wording is compared with the current one on every submission, whatever the revision (ADR-0009).
  5. Check availability: dates, limits, members, uniqueness, and the places left on chosen options.
  6. Check protection: the challenge's single use, honeypot, time, captcha, content rules. The result is a spam score with reasons. A failed captcha is rejected. A high score is **accepted silently into `Spam`**, so bots learn nothing. An unavailable captcha provider, and a no-JS post on a form whose protection needs JavaScript, score into `Spam` too, so no lead is lost and none runs actions unchecked (ADR-0012).
  7. Read every answer through its field type before the rules, so rules compare what was read; after applying them, validate per field type. Hidden fields are stripped and never fail, and calculations are recomputed (ADR-0058).
  8. **One transaction:** the entry, values, file links, consent texts, the timeline event, one `ActionRun` per action whose trigger and rule match (none for spam, test or unverified entries), the places taken, the unique answers held, and the stats increment.
  9. After the commit, wake this server's action processor, then return the outcome.
- **Places on options:**
  - The submission takes a place on each chosen option with a limit inside its transaction, which holds the form's row, so two servers can never overbook (ADR-0090). An option's places count every entry that holds one, those that came in before the limit was set included. When none is left, the field fails validation with its own message, and the visitor can choose another option.
  - Spam and test entries take no places. Rejecting, marking as spam, deleting and erasing an entry give its places back. Moving an entry back takes them again, and when an option is full by then, the backoffice asks before overbooking it.
- **Unique answers:** a field's "unique per form" check holds its answer against the answers of the entries that count toward the entry limit, compared as the entries screen filters them (letter case and surrounding spaces make no difference, a number or a date compares by its value). It is checked while the answers are read and again inside the transaction, which holds the form's row, so two servers can never store the same answer twice (ADR-0091). Spam, rejected, draft and test entries hold none, and deleting, erasing or anonymising an entry gives its answers back. A page's validation leaves the check out (ADR-0012).
- **Values kept only until the actions have run:** a field set to "only until the actions have run" is stored with the entry, used by its action runs, and deleted once every one of them is final (succeeded, failed for good or skipped) and no trigger can still fire for the entry (below). An entry event records it, and the entry shows the field as "not kept". A run applies when one exists, or when the form has an action whose trigger can still fire for the entry (any of the form's triggers while the entry is `Unverified`, or in `Spam` until it is purged; `approved` or `rejected` while it waits for approval); the value is kept until no such trigger can fire any more and every run is final, or until the entry expires unverified. When no run applies, the value is never stored.
- **Actions:**
  - **Built-in types:**
    - email (to fixed addresses, a field value, the member, or the creator; CC/BCC; reply-to a field; attachments of uploaded files within size caps, never a file quarantined or still waiting for its scan (ADR-0144); HTML with a layout, or plain text, with the text version always generated (one body per mail, ADR-0007); per-culture subject and body; or a Razor view chosen by a developer);
    - webhook (JSON, form-encoded or XML; field mapping or "everything"; standard metadata; HMAC-SHA256 signature header; secrets as `$JironFormBuilder:Secrets:<Name>` references; SSRF protection);
    - Slack and Microsoft Teams messages (webhook presets with formatted text);
    - create or update content (document type, parent from a picker or a dynamic rule, mapping fields and fixed values to properties, publish or save only);
    - set state (approve, reject or mark as spam, under a rule).

    Email verification is a form setting (§4.3.4), not an action type: its mail is a system mail (ADR-0007), and no action runs until the visitor confirms; then its `submitted` and `verified` actions are queued together, in the order shown.
  - **Running them:**
    - Actions run in the background in the order shown, each in its own `ActionRun`, with retries (5 attempts, after 1, 3, 9 and 27 minutes) and an `Uncertain` state when a lease expires mid-send or an attempt is cut off (ADR-0007).
    - **The order:** the actions that one event queues for an entry (its submission or confirmation, an approval, a rejection) run in the order shown: a run starts once every earlier run queued with it has finished its first attempt (or its lease ran out; a retry from the deliveries log starts the attempts over) and none of them is running at that moment, and once an earlier run that stops the rest is final (ADR-0007, ADR-0040). A retrying run does not hold the later ones back, and the actions of different events do not wait for each other.
    - **The late finisher:** an attempt whose lease ran out may still record its result, which then replaces `Uncertain`. Nothing else retries an `Uncertain` run by itself.
    - A failed action never blocks the others unless it is marked "stop the rest if this fails"; then it stops the rest of the actions its event queued, and retrying it from the deliveries log brings them back.
    - Every run shows on the entry's timeline and in the **deliveries** log, which can retry it.
    - **Retrying an `Uncertain` run asks first,** because its first attempt may already have been delivered: the screen says so, and the management API refuses the retry without an explicit confirmation.
    - **Sending can be paused:** with the host switch `JironFormBuilder:Actions:Paused` on, no action run and no email is sent, and none is lost. Entries are still stored, runs and emails stay queued, and they go out once the switch is off. It is meant for a copy of a live database restored onto a test server, and for incidents. The deliveries log, the dashboard and a health check say that sending is paused.
  - **Umbraco webhooks:** Jiron Form Builder also registers entry events (submitted, approved, rejected, deleted, action failed) with Umbraco's own webhook system, so sites can subscribe in Settings › Webhooks. Confirmed in 1.2b: our event appeared in Umbraco's event picker, and a webhook on it delivered our payload (§3.4, "Webhooks").

### 4.5 Database schema v1 (table prefix `jfb`; all times UTC; GUID keys for anything exposed)

| Table | Columns (main) | Indexes and notes |
| --- | --- | --- |
| `jfbFolder` | Id PK, Key (unique), ParentId FK null, Name, SortOrder, IsRestricted, TrashedUtc null, CreatedUtc, UpdatedUtc, CreatedByUserKey, UpdatedByUserKey | (ParentId). Trash keeps the tree; restore returns to the parent or the root. `IsRestricted` makes only the grants on the nearest restricted folder or form, and on everything between it and the form, count there (ADR-0013). |
| `jfbForm` | Id PK, Key (unique), FolderId FK null, Alias (unique), Name, DraftVersionId FK null, PublishedVersionId FK null, PublicRevision, ConcurrencyVersion, LastEntryNumber, ThemeAlias null, IsRestricted, TrashedUtc null, CreatedUtc, UpdatedUtc, CreatedByUserKey, UpdatedByUserKey | (FolderId), (TrashedUtc). `ConcurrencyVersion` makes stale saves fail with `409 concurrency_conflict`. `IsRestricted` as on `jfbFolder`. `ThemeAlias` copies the published version's `appearance.theme`, for the library's "used by" list and its delete guard (ADR-0015). |
| `jfbFormVersion` | Id PK, FormId FK (cascade), Number, Kind (Draft, Published), SchemaVersion, Definition (NVARCHARMAX), DefinitionHash, PublicHash, PublicRevision null, Comment, IsPinned, CreatedUtc, CreatedByUserKey, PublishedUtc null, PublishedByUserKey null | Unique (FormId, Number). Every save adds a row. Published versions are never pruned. Draft saves are pruned by the cleanup job (keep the last 50 and the last 90 days, never pinned ones or ones an entry points at). `PublicRevision` is set on published rows (ADR-0002). |
| `jfbFormField` | Id PK, FormId FK (cascade), FieldKey, Alias, Label (default culture), FieldType, StorageKind, IsSensitive, SortOrder, FirstVersionNumber null (null = only in a draft so far: shown with test entries only, set by the first publish that has the field), LastVersionNumber null (null = in the live version, when `FirstVersionNumber` is set), ParentFieldKey null (repeater children and grid rows) | Unique (FormId, FieldKey). The **field catalogue**: every field ever published, for entry columns, exports and search. `jfbEntryValue` refers to a field by this `Id`, so a field gets its row before its first value is stored: on publish, or on the first preview submission for a field that exists only in the draft (ADR-0003). |
| `jfbEntry` | Id PK (bigint), Key (unique), FormId FK, FormVersionId FK, Number, State, IsTest, SubmissionId, SubmissionHash, Culture, ContentKey null, PageUrl (path only), MemberKey null, IpAddress null, SpamScore, SpamReasons, SearchText (NVARCHARMAX, normalised, no sensitive values), Context (JSON), DurationSeconds null, SubmittedUtc, UpdatedUtc, StateChangedUtc, StateChangedByUserKey null, FirstSeenUtc null, VerifiedUtc null, ResumeTokenHash null, ResumeExpiresUtc null, CurrentPageKey null, ChallengeId null, HoldUntilUtc null, HoldReason null, PurgeAfterUtc null | Unique (FormId, Number) and (FormId, SubmissionId). `SubmissionId` is always set; the server generates one for no-JS, server-key and programmatic entries. **Filtered** unique indexes `WHERE … IS NOT NULL` on (FormId, ChallengeId) and on ResumeTokenHash, created with custom SQL in the migration (SQLite and SQL Server both support them; a test inserts two NULL rows). Also (FormId, SubmittedUtc), (FormId, State, SubmittedUtc), (MemberKey), (PurgeAfterUtc) (ADR-0003), and (FormVersionId), so pruning a draft save checks its entries without a scan (ADR-0033). |
| `jfbEntryValue` | Id PK (bigint), EntryId FK (cascade), FieldId FK (`jfbFormField`, no cascade: SQL Server refuses a second cascade path, and entries are deleted before their form), RowIndex, ValueIndex, Text (NVARCHARMAX) null, TextKey (nvarchar 256, cleaned and folded in C# as rules compare (ADR-0005), for sort and filter; SQLite's `LOWER` folds ASCII only) null, Number (a double: `REAL` on SQLite, `FLOAT` on SQL Server, exact for the at most 15 significant digits a number field accepts; `Text` keeps the submitted value; Umbraco maps `decimal` to `TEXT` on SQLite) null, Date null, Bool null, FileId FK null | (EntryId, FieldId), (FieldId, TextKey, EntryId), (FieldId, Number, EntryId), (FieldId, Date, EntryId): covering indexes for the query shapes of ADR-0003. An integer `FieldId` instead of the field's GUID key halves the values on disk (1.3). Multi-value and repeater rows use ValueIndex and RowIndex. A filtered index on (FileId) lets the upload cleanup check a file without a scan (ADR-0033). |
| `jfbEntryFile` | Id PK, Key (unique), EntryId FK (cascade) null, FormId, FieldKey, OriginalName, StoredName, ContentType, SizeBytes, FileHash, ScanState, CreatedUtc | (EntryId, CreatedUtc): an entry's files, and the orphans oldest first. Files without an entry after 24 h are orphans and get cleaned up. |
| `jfbConsentText` | TextHash PK (SHA-256 of culture and text), Culture, Text (NVARCHARMAX), FirstSeenUtc | A consent value stores `true` plus this hash: proof of the exact wording, stored once. |
| `jfbEntryEvent` | Id PK (bigint), EntryId FK (cascade), OccurredUtc, Kind, UserKey null, ChangeKey null, Details (JSON) | (EntryId, OccurredUtc), (ChangeKey). `ChangeKey` ties the events of one state change by a user, also a bulk one, so Undo finds them (ADR-0023). Edits record old and new values only for non-sensitive fields, and they are deleted with the entry. |
| `jfbEntryNote` | Id PK, EntryId FK (cascade), UserKey, Body, CreatedUtc, UpdatedUtc | Internal notes, never sent anywhere. |
| `jfbActionRun` | Id PK (bigint), EntryId FK (cascade), FormVersionId, ActionKey, ActionType, Trigger, SortOrder, StopOnFailure, State (Pending, Running, Succeeded, Failed, Skipped, Uncertain), Attempts, NextAttemptUtc, ChangeKey null, LockedUntilUtc, LockedBy, LastErrorCode, LastErrorMessage (sanitised, ≤ 1000), CreatedUtc, StartedUtc, FinishedUtc | (State, NextAttemptUtc), (EntryId, SortOrder), (ChangeKey). `SortOrder` is the action's position, for the order of §4.4. A run queued by a user's state change waits for the undo window: its first `NextAttemptUtc` is the end of the window (ADR-0023). A claim writes a token of its own to `LockedBy`, and every state change names the run, its token and its state (ADR-0007). |
| `jfbEmail` | Id PK (bigint), Kind (Action, Resume, Verification, Test, Report, Digest, Forward), EntryId FK null (set null), ActionRunId null, To, Cc, Bcc, ReplyTo, From, Subject, HtmlBody, TextBody, Attachments (JSON of file keys), Culture, RecipientKey null, State (Queued, Sending, Sent, Failed, Uncertain, Limited), Attempts, NextAttemptUtc, LockedUntilUtc, LockedBy, LastErrorCode, LastErrorMessage, CreatedUtc, SentUtc, PurgeAfterUtc | (State, NextAttemptUtc), (PurgeAfterUtc), (RecipientKey, CreatedUtc). Sent, failed, uncertain and limited mail is purged after N days (default 30); `PurgeAfterUtc` is set when a mail reaches one of those states. RecipientKey is the visitor-typed address, trimmed, case-folded in C# (SQLite's `LOWER` folds ASCII only) and without a `+tag` in its local part, so the per-address limits count every spelling of one mailbox. `Limited` is final: a mail those limits hold back is never claimed, sent or retried (ADR-0012). |
| `jfbOptionList` | Id PK, Key (unique), Alias (unique), Name, Kind (Manual, Content, DataType, Url, File, Countries, Code), Config (JSON), Items (JSON), CacheSeconds, ConcurrencyVersion, CreatedUtc, UpdatedUtc | |
| `jfbTheme` | Id PK, Key (unique), Alias (unique, fixed once created), Name, BaseTheme, Tokens (JSON), CustomCss, ConcurrencyVersion, CreatedUtc, UpdatedUtc | The default theme is a General setting per site (ADR-0015), not a flag here. |
| `jfbEmailLayout` | Id PK, Key (unique), Alias (unique), Name, Layout (JSON: logo, colours, header and footer per culture), ConcurrencyVersion, CreatedUtc, UpdatedUtc | The default layout is an Email setting per site (§4.8), not a flag here. |
| `jfbFormTemplate` | Id PK, Key (unique), Kind (Form, Group), Name, Description, Category, Definition (NVARCHARMAX), CreatedUtc, UpdatedUtc, CreatedByUserKey, UpdatedByUserKey | Templates saved by users: whole forms, and saved groups of fields that the designer inserts as copies with new keys. The built-in ones ship as embedded JSON. |
| `jfbOptionCapacity` | FormId FK (cascade), FieldKey, ValueHash (SHA-256 of the option value), OptionValue, Taken, UpdatedUtc | PK (FormId, FieldKey, ValueHash): a hash, because SQL Server's collation folds free text that SQLite keeps apart (ADR-0033). The places taken on options with a limit; the limit itself lives in the definition. `Taken` is the number of the form's values at the field that equal the option's value and whose entry holds places (ADR-0090): the row is made with that count, with `INSERT … WHERE NOT EXISTS`, the first time a submission's transaction counts the option, and every write that changes such a value, or whether its entry holds places, changes `Taken` in the same transaction, after it has locked the form's row. |
| `jfbUserSetting` | UserKey, Name, Json (NVARCHARMAX), UpdatedUtc | PK (UserKey, Name). A backoffice user's own choices, the same on every device: column sets, saved filters, export choices, the digest. |
| `jfbPermission` | Id PK, UserGroupKey, TargetType (Global, Folder, Form), TargetKey (`Guid.Empty` for Global), Permissions (flags) | Unique (UserGroupKey, TargetType, TargetKey). |
| `jfbSetting` | Id PK, Scope (`global` or a site root key), Section, SchemaVersion, Version, Json (NVARCHARMAX), UpdatedUtc, UpdatedByUserKey | Unique (Scope, Section). |
| `jfbReference` | FormId, ContentKey, PropertyAlias, Culture, UpdatedUtc | PK (FormId, ContentKey, PropertyAlias, Culture), and (ContentKey): every save, publish, move or delete of a content item replaces its rows. Needed: Umbraco's relations link only `umbracoNode` rows (1.2b, ADR-0017). |
| `jfbAuditEntry` | Id PK (bigint), OccurredUtc, UserKey, Action, EntityType, EntityKey, Details (JSON) | Never field values. (OccurredUtc), (EntityKey). |
| `jfbDailyStat` | Day, FormId, Metric, Dimension, Value | PK (FormId, Day, Metric, Dimension): one form's days are a seek; (Day, FormId) for retention. Metrics: views, starts, pageReached:{pageKey}, submissions, spam, completionSeconds, origin:{contentKey}, hour:{0-23}, invalid:{fieldKey}:{errorKind}. Unique members are counted from entries at query time, so stats never hold personal data. New metrics need no migration. |
| `jfbKeyValue` | Key PK, Value, UpdatedUtc | One-time flags (section granted, setup done, file-mirror hashes). |

- **Names in the database** (ADR-0033): columns are camelCase (`formId`, `submittedUtc`), as Umbraco's are, and SQL's reserved words become `toAddresses`, `ccAddresses`, `bccAddresses`, `fromAddress` (with `replyToAddress` beside them) and `triggerKind`. Enums stored as text are the camel case of their names (`pending`, `published`), written and read by one converter that refuses any other spelling; hash columns are named for what they hash (`submissionHash`, `fileHash`). [docs/database.md](docs/database.md) has every column, index and foreign key.
- **Keys never hold NULL.** No nullable column is part of a primary key or of a plain unique index. SQL Server treats NULLs there as equal and SQLite as distinct, so the two databases would disagree (Tandem ADR-0005).
  - Use a sentinel instead: `Guid.Empty` for "global", and `""` for the invariant culture or an empty dimension.
  - Or use a filtered index, as on `jfbEntry`.
  - A schema test enforces this.
- **Times on SQL Server** are `DATETIME`, rounded to about 3 ms. Lists order by (time, Id), and nothing compares stored times for equality.
- **Times on SQLite** are text in one format, which sorts correctly only while every value is written the same way. They are written only as UTC `DateTime` parameters (never as strings, `DateTimeOffset` values or SQL date functions), and read back as UTC (ADR-0003).
- **Entry queries** use only the shapes of ADR-0003: field filters as `IN` subqueries, keyset paging, a sort by a value that walks the value index, one-pass search, and a time budget per statement (`query_timeout`). They need no planner statistics on SQLite.
- **Counters and once-only rows** (entry numbers, daily stats, one-time flags) use one atomic statement, never a read followed by a write:
  - `UPDATE … SET LastEntryNumber = LastEntryNumber + 1`, read back in the same transaction;
  - or `INSERT … SELECT … WHERE NOT EXISTS`;
  - on SQL Server, `UPDLOCK, HOLDLOCK` wherever a read must come first (Tandem 3.9).
- **The migration plan** `Jiron.Umbraco.FormBuilder` has step 1 `CreateSchemaV1`, then one class per later change. Every step is idempotent (`TableExists`, `ColumnExists`, `IndexExists`) and has a frozen schema-snapshot DTO.
- **Every release gets an upgrade test** (16.5), and `docs/database.md` holds an ER diagram (Mermaid) that a test keeps in sync with the DTOs.

### 4.6 Public API v1 (`/jfb/api/v1`, anonymous, CORS allowlist, rate limited, ProblemDetails errors, OpenAPI document `jfb-public`)

| Route | Purpose |
| --- | --- |
| `GET forms/{keyOrAlias}?culture=&contentKey=` | The public projection for headless rendering, when the form allows it; a form for members answers `403 member_required` unless a member it admits or a server key asks (ADR-0150). `ETag` from the response itself, so it follows the public revision, the culture and dictionary changes (ADR-0002). |
| `GET forms/{key}/element?instance=&culture=&contentKey=&theme=&context-<name>=` | The published form's first render as the headless element writes it (ADR-0128): the view model the render endpoint renders, with the context values the form lists, as JSON with a `contract` number and the definition endpoint's answer as the model data's text, without what only a post without JavaScript uses. `no-store`. |
| `GET forms/{key}/render?instance=&page=&contentKey=&culture=&theme=` | The published form as HTML in the page's theme and culture, for a runtime that got `form_changed` on a page that may come from a cache (ADR-0004). `page` must be a local path; `theme`, the placement's theme if the page named one, is a theme's alias (ADR-0105). `no-store`. |
| `POST forms/{key}/challenge` | A signed challenge (form, time, and optionally a proof-of-work puzzle). Fetched on first interaction, so cached pages stay cacheable. The response also carries what cached markup cannot: the places left per option, and the member values that rules use (§4.3.3). |
| `POST forms/{key}/uploads` | Upload one file before submitting (progress per file): `multipart/form-data` with the `challenge` part first, then the `file` part, streamed and checked on its first bytes (ADR-0006). Returns a signed upload token. Limits per form, file and IP, and on the bytes that may wait for an entry, in all (`503 uploads_full`) and per address (`429 rate_limited`, ADR-0148); `413 payload_too_large` and `415 unsupported_media_type` close the connection. |
| `POST forms/{key}/validate` | Validate one page's values on the server: `{revision, pageKey, culture, contentKey, values, consents, query, context, challenge}`, the answers given so far read and ruled as the submission's; `204` when the page's answers pass, `422` with the errors of that page's fields by alias (multi-page); nothing is stored (ADR-0089). |
| `POST forms/{key}/entries` | Submit: `{submissionId, revision, culture, contentKey, values, consents, query, context, challenge, captchaToken}` (a file field's value holds the tokens its uploads answered with, and `consents` holds, per consent alias, the `jfbConsentText` hash of the wording shown, ADR-0009, ADR-0088) → `{receipt:{submissionId, reference, submittedUtc}, outcome:{kind, html, url, delaySeconds, showAnswers}}`, each part of the outcome only when it applies. |
| `POST forms/{key}/receipt` | Check an uncertain submission by `{submissionId, challenge}`: the receipt and the outcome when an entry with that submission id was made with that challenge (its signature counts for up to 24 hours past its expiry) or server key, otherwise `404 not_found` for every miss (ADR-0012); a form the site does not serve and a form for members answer with the form's own codes (ADR-0089). |
| `POST forms/{key}/drafts` | Save and resume: `{revision, culture, contentKey, pageKey, values, email, challenge}` → the resume link is emailed; the response never contains it. Files and consent answers are not kept with a draft (an upload without an entry is removed after 24 hours, ADR-0009, and a consent counts only for the wording shown when it is given), so the resumed form asks for the files again and shows every consent unticked. |
| `POST drafts/resume` | `{token}` → values and page. The token comes from the URL fragment (`#jfb-resume=…`), so it never reaches server logs. |
| `GET verify?t=` and `POST verify` | Email verification. The GET only shows a confirm button, because mail scanners follow links; the POST confirms. |
| `GET forms/{key}/options/{fieldKey}?parent=&culture=` | Cascading option lists. |
| `POST forms/{key}/events` | Insights beacon (`view`, `start`, `page`, and `invalid` with only the field key and the kind of error) with no identifiers, counted per day only. |

**Rules:**

- Tokens always travel in the body or the URL fragment, never in query strings that get logged (except the verify link, which is single-use and short-lived).
- Same-origin requests are always accepted; others only from `JironFormBuilder:PublicApi:AllowedOrigins`.
- Client IPs come only from `HttpContext.Connection.RemoteIpAddress`. Sites behind proxies configure ASP.NET Core `ForwardedHeaders` (documented).
- **Server-to-server headless submissions** may send `Authorization: Jfb-Key <key>`. Host configuration holds a name and the SHA-256 of each key, never the key, compared in fixed time. They skip the browser challenge, the captcha and the honeypot, never validation, the content rules or the domain limits, and are limited per key (ADR-0012).
- **Stable error codes:** `form_not_found`, `form_closed`, `form_changed`, `validation_failed` (`422`, errors by alias), `invalid_request` (`400`: a body that is not valid JSON or not the request's shape), `challenge_required`, `captcha_failed`, `rate_limited`, `payload_too_large`, `unsupported_media_type`, `member_required`, `duplicate_submission`, `entry_limit_reached`, `already_submitted`, `origin_denied`, `server_key_invalid` (`401`, ADR-0126), `preview_ticket_invalid` (`401`, a preview frame's ticket that does not hold, ADR-0203), `timeout`, `not_found`, `server_error`.
- **Every endpoint declares** its rate-limit policy, body limit and time limit, and a test reads the OpenAPI document and fails when one is missing (ADR-0012).

### 4.7 Management API (`/umbraco/jfb/management/api/v1`, OpenAPI document `jfb`, generated TS client)

- **Forms and folders:** tree, search, create (blank, template, import), get, save draft (with concurrency), publish, unpublish, duplicate (with or without actions), copy actions to another form, move, trash, restore, delete permanently, empty trash, references ("used on").
- **Versions:** list, get, diff (two versions, field by field), restore, pin, comment.
- **Designer support:**
  - the registries: field types, action types, option-source kinds, captcha providers, merge-tag catalogue and themes;
  - form check results;
  - a preview render of the draft;
  - test an action against a sample entry.
- **Entries:**
  - list (filters, search, sort, paging, column set), get, mark seen, change state (single and bulk), edit, delete (single and bulk);
  - notes, timeline, file download, forward;
  - action runs and retry;
  - counts for badges;
  - the all-forms inbox and spam.
- **Exports:** start (format, filter, fields), status, download (time-limited and authorised), and immediate streaming for small sets.
- **Library:** option lists (CRUD, preview, CSV/TXT upload), themes (CRUD, preview CSS), email layouts (CRUD, preview, test send), templates.
- **Settings** per section and scope; **permissions**; **audit log**; **deliveries** (emails and webhook runs, retry); **insights** and **answer summaries**; the current user's own **user settings** (`jfbUserSetting`).
- **Privacy:** find a person by email or member, export everything about them (ZIP of JSON and files), erase or anonymise, retention dry run.
- **Content:** the form picker's folders and forms, search, the forms and the folders of given keys and the site's themes, authorised by content access (6.7e, 6.7g, ADR-0013).
- **Setup and health:** checklist status and health results (any user of the section); "create a form block type for me", which creates an element type and a data type and so needs `manageSettings` and Umbraco's Settings section (6.7b).

### 4.8 Settings model

**Host configuration** (`appsettings.json` → `JironFormBuilder`). It is for developers and for locks, and it wins over the backoffice:

```json
"JironFormBuilder": {
  "PublicApi": { "BasePath": "/jfb/api", "AllowedOrigins": [], "ServerKeys": [] },
  "Rendering": { "IncludeAssetsAutomatically": true },
  "Files": { "Root": "~/umbraco/Data/JironFormBuilder/Uploads", "MaxFileBytes": 52428800, "MaxWaitingBytes": 10737418240, "MaxWaitingBytesPerAddress": 1073741824, "BlockedExtensions": ["exe", "dll", "js", "mjs", "bat", "cmd", "ps1", "sh", "msi", "msp", "scr", "pif", "cpl", "jar", "vbs", "wsf", "hta", "lnk", "reg", "html", "htm", "xhtml", "shtml", "svg", "php", "phtml", "asp", "aspx", "jsp", "cgi"] },
  "Privacy": { "AllowFullIp": false, "AllowRetentionOff": false, "AllowImplicitCaptchaConsent": false },
  "Captcha": { "RecaptchaV2": { "SiteKey": null, "SecretKey": null }, "RecaptchaV3": { "...": "..." }, "HCaptcha": { "...": "..." } },
  "Webhooks": { "AllowedHosts": [], "AllowHttp": false },
  "Actions": { "Paused": false },
  "Secrets": { "CrmToken": null },
  "Access": { "Grants": [ { "group": "editor", "permissions": [ "viewEntries" ] } ] },
  "Definitions": { "Mirror": false, "Folder": "~/umbraco/JironFormBuilder/forms" },
  "Limits": { "...": "hard caps the backoffice cannot exceed, the rate-limit partitions and the mail caps (ADR-0012)" },
  "Designer": { "SettingLocks": [ { "kind": "action", "type": "email", "setting": "from", "value": "noreply@example.com", "hidden": true } ] },
  "LockedSections": []
}
```

`Files:MaxWaitingBytes` caps the bytes of uploads no entry took yet, and `Files:MaxWaitingBytesPerAddress` one address's share: past either an upload is refused before it is stored (ADR-0148). `Files:MaxFileBytes` caps the limit an editor can give any file field. It is not a request limit: the upload endpoint and the no-JS post set Kestrel's limit per request from the form's fields, whatever the site's `Umbraco:CMS:Runtime:MaxRequestLength` (ADR-0006). `Files:Root` is `~/` and a path relative to the content root, or an absolute path; a root inside the web root or around it, as written or through its links, stops the site from starting (ADR-0041). `Limits:Definitions` holds the definition limits of §4.3.1: each may be lowered, or raised to ten times its default, and a form stored above a lowered limit keeps working while saving it is refused (ADR-0042). The host configuration is read once, when the site starts: a value out of range, or a key Jiron Form Builder does not have, stops it with a message that names the key.

**Backoffice settings** (typed sections in `jfbSetting`, ADR-0008; `global` scope, plus per-site overrides where marked). A site is a root content node, and the site of a request is the root of the content the form is shown on or submitted from; without content, the root whose Umbraco domain matches the request's host (14.4); otherwise the global values apply.

- **General** (each field can be set per site, except where it says otherwise):
  - default theme, the required-marker style, default labels;
  - new-form defaults (consent field, notify-the-creator action), for every site at once, since a new form belongs to no site (ADR-0044);
  - how long draft saves stay in a form's History, for every site at once (the version cleanup of 10.3; by default the last 50 and those of the last 90 days, §4.5);
  - per site: the base URL (an origin: the scheme, the host and a port, nothing after them; by default the origin of the site's first `https` domain in Umbraco, the root's own before those deeper down, otherwise Umbraco's application URL, never the request's Host header; ADR-0067) and the **time zone** (an IANA id, used for "today", opening dates and relative dates; saving refuses an id the server cannot resolve, ADR-0005).
- **Email** (each field can be set per site):
  - sender name and address;
  - default layout;
  - team recipients for system notices (failed deliveries);
  - team addresses that entries may be forwarded to (besides backoffice users);
  - test email.
- **Spam protection:**
  - default level;
  - content rules (blocked words, blocked email domains, maximum links);
  - blocked address ranges (CIDR; matched against the request, never stored);
  - provider keys status (keys themselves only in host configuration).
- **Files:** allowed type groups, maximum sizes, scanner status.
- **Privacy:**
  - default retention per state, Spam included (the one home of the retention defaults, ADR-0021);
  - IP handling;
  - sensitive-field defaults;
  - third-party captcha consent mode (explicit, Cookiebot category, CMP through the JS API, and `implicit` only when the host allows it);
  - the insights switch.
- **Access:** which user groups get which Jiron Form Builder permissions, globally and per folder or form (when the host doesn't lock them). Stored in `jfbPermission` (ADR-0013), with the same locks, conflicts and audit as the sections. A folder or form can be **restricted**, so that, below it, only the grants on the nearest restricted folder or form and on everything between it and the form count (ADR-0013); `JironFormBuilder:Access:Grants` sets the global grants from host configuration.
- **Integrations:** webhook signing secret reference, Slack and Teams defaults, Umbraco webhook events on or off.

Every setting has:

- a default, validation (server and client), help text, a docs entry and a test;
- a "locked by configuration" display naming the key when the host locks it: locked, restricted (some values refused) or clamped (ADR-0008). A lock added later applies where the value is used, without rewriting stored rows.

A settings change raises a notification and a cache refresher: the saving server applies it at once, the others on their next sync, usually within seconds (ADR-0008).

### 4.9 Extension points (documented, with samples)

- **C# types** (each registered through `builder.JironFormBuilder()…`):
  - `IFormBuilderFieldType` (or the `FormBuilderFieldTypeBase<TSettings>` helper): a custom field with a settings schema, parsing, validation (of its values, of its uploads' size and first bytes, and of its rows, ADR-0071), storage kind, Razor partial and client behaviour name;
  - `IFormBuilderActionType` (`FormBuilderActionTypeBase<TSettings>`): a custom action with settings, a test run and retry semantics;
  - `IFormBuilderOptionSource`: options from code, with optional parent filtering for cascades;
  - `IFormBuilderExportFormat`: a custom export;
  - `IFormBuilderCaptchaProvider`: a captcha;
  - `IFormBuilderSpamCheck`: an extra spam signal with a score and a reason;
  - `IFormBuilderMergeTagSource` and `IFormBuilderMergeTagFilter`;
  - `IFormBuilderFileScanner`: malware scanning, for example ClamAV, in a sample;
  - `IFormBuilderTheme`: a theme, with its stylesheet, class map, token defaults and view folder (ADR-0015);
  - `IFormBuilderFileStore`: other storage, for example blob storage through Umbraco's official provider (sample in 15.2). Asynchronous: `SaveAsync(id, stream)` stores all of it or nothing, `OpenReadAsync(id)` returns a stream or null, `DeleteAsync(id)`; ids are random and never URLs (ADR-0006); a save never replaces a file, and a reader never sees one that is still being written (ADR-0041).
- **Settings schemas:** C# attributes on a settings class (`[FormBuilderSetting(Label, Description, Editor, Order, Group, SupportsMergeTags)]`) give every custom type a backoffice editor without any JavaScript. A custom Lit element can replace the editor for a setting through a `jfbSettingEditor` manifest.
- **Notifications:**
  - forms: `FormSaving`/`Saved`, `FormPublishing`/`Published`, `FormTrashing`/`Deleted`;
  - entries: `EntrySubmitting` (cancelable, can add field errors), `EntrySubmitted`, `EntryStateChanged`, `EntryDeleting`;
  - actions: `ActionRunning`/`ActionRunCompleted`/`ActionRunFailed`;
  - `ExportCompleted`.
- **Services:**
  - `IFormBuilderFormService` with its parts `IFormBuilderFormVersionService` and `IFormBuilderFormExchangeService` (ADR-0053), `IFormBuilderEntryService` (query, create programmatically, change state);
  - `IFormBuilderEntryReader` (read approved entries on the website, for example a guestbook);
  - `IFormBuilderRenderer`;
  - `IFormBuilderMergeTagService`;
  - `IFormBuilderOptionService` (a field's or a library list's options, resolved in a culture and for a parent's answers, ADR-0073, ADR-0077).
- **Rendering:**
  - the `<jfb-render form="alias|key" theme="…" context-…="…">` tag helper, `@await Component.InvokeAsync("FormBuilderForm", …)` and `<jfb-assets>`. The custom element `<jfb-form>` belongs to the headless renderer;
  - the CSP nonce: `<jfb-assets nonce="…">`, or an `IFormBuilderCspNonceProvider` that a site (or its CSP package) registers to return the request's nonce, which `<jfb-assets>` also uses when it is included automatically (ADR-0004);
  - theme views in Jiron Form Builder's own layout (ADR-0015): one set of views that every theme shares, themed by a class map, a stylesheet and tokens. `Views/JironFormBuilder/<theme>/` holds `Form.cshtml`, `Page.cshtml`, `Group.cshtml`, `Field.cshtml`, `fields/<type>.cshtml`, `Navigation.cshtml`, `Progress.cshtml`, `ErrorSummary.cshtml`, `Summary.cshtml`, `Outcome.cshtml` and `Notice.cshtml`, each starting with its contract header (`@* jfb-view: Field 1 *@`);
  - per-form overrides in `Views/JironFormBuilder/<theme>/_forms/<alias>/`, with the same file names. A view resolves from the form's override, then the theme, its base theme, and `default`;
  - the markup contract of ADR-0015 (the `data-jfb-*` hooks, ids, names and states), shared by every theme, the runtime and `<jfb-form>`.
- **Website JS API:**
  - `window.JironFormBuilder.init(root)` for forms injected later;
  - `get(formEl)` → `{ validate(), next(), back(), submit(), values() }`;
  - `consent.grant('captcha')`/`withdraw()`.
- **DOM events:** `jfb:ready`, `jfb:page`, `jfb:invalid`, `jfb:submitting`, `jfb:submitted`, `jfb:error`; `<jfb-form>` adds `jfb:rendered` once it shows its form, and `jfb:error` when it cannot (ADR-0128, added in 6.6e). They are for the site's own analytics; Jiron Form Builder tracks nothing itself.
- **Headless:** the TypeScript types and JSON schema of the public projection, and `<jfb-form>` with `api`, `form`, `culture`, `content`, `theme` and `context-<name>` attributes (ADR-0128).

### 4.10 Security and privacy by design

- **Escaping and rendering:**
  - Escape all visitor input everywhere: Razor, emails, the backoffice and exports.
  - Rich text from editors is sanitised server-side, before rendering and before emails, by an established allowlist sanitiser (HtmlSanitizer, MIT; licence and dependencies checked in 3.5), never a home-made one.
  - Theme custom CSS goes through the sanitiser's CSS parser, which removes `@import`, `url()`, `image-set()` and `@font-face` sources that point to other hosts, `expression()` and `behavior`.
  - No `innerHTML` with untrusted data, and no `unsafeHTML` in Lit, except for sanitised editor HTML through one audited helper.
  - Exports guard against formula injection.
- **Anonymous endpoints:**
  - signed challenges (Data Protection, form-bound, time-bound), honeypot, minimum fill time;
  - the rate limiter per IP, per form and site-wide, with bounded partitions that fail closed: sliding windows on `TimeProvider` (ADR-0137), applied by Jiron Form Builder's own middleware and filter, so a site's own `UseRateLimiter()` and CORS are untouched; a key that names no form spends only its address's limits and a lookup's (ADR-0149); past `MaxTrackedClients` new addresses share one overflow partition (ADR-0012);
  - request size and time limits, and cancellation everywhere;
  - `Cache-Control: no-store` on every response except the definition, which sends `no-cache` with its `ETag` (ADR-0012);
  - origin checks.
  - **Antiforgery:** anonymous forms carry no antiforgery token, so they work on cached pages; their endpoints are not authenticated, and SameSite plus origin checks cover them. **Members-only forms** act for a signed-in member, so every post must come from that member's own page: their challenge is bound to the member, and no cookie is set for it (ADR-0004, ADR-0012).
- **Third-party captchas** (reCAPTCHA, hCaptcha):
  - Their scripts load only on forms that use them, and only after consent.
  - **Consent modes:**
    - `explicit` (default): a short notice and a "Load the check" button, or the consent field, before anything loads;
    - `cookiebot` (a switch between `necessary`, the default, as Cookiebot files a captcha's cookie, and `marketing`; the owner, 2026-11-06, ADR-0199);
    - `cmp`: `JironFormBuilder.consent.grant('captcha')` from any consent manager;
    - `implicit`: only if the host allows it (`Privacy:AllowImplicitCaptchaConsent`, a restriction like `AllowFullIp`).
  - reCAPTCHA can use `recaptcha.net`. Verification checks hostname, action, score and token age (ported from Ligata.Forms).
- **Files:**
  - an extension allowlist per field plus a global blocklist, checked against the bytes' magic numbers;
  - random stored names, never served from a public path, sizes enforced while streaming;
  - **the public API never takes a form:** a middleware before routing gives every request under its base path an `IFormFeature` with no form content, because Umbraco's routing would otherwise read (and buffer) the whole body of any form post before the action (§3.4, ADR-0006);
  - upload refusals are strict: the server never reads past a field's limit, refuses a declared length over it before reading, and checks the challenge before the file's first byte; `413` and `415` close the connection;
  - **no-JS posts with files** name their form in the action (`?jfb-post=<key>.<instance>`), so a middleware before routing sets the form's limits, reads the post itself and turns one that is too large into an error at the field; Kestrel may drain up to twice the form's limit after the answer, so the browser shows the page instead of a reset connection;
  - downloads only through the management API, with permission checks, `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`;
  - images are never rendered inline from untrusted SVG;
  - the scanner hook can quarantine a file, which marks the entry.
- **Webhooks and URL option lists:**
  - `https` only (`http` only when the host allows it);
  - hosts only from `JironFormBuilder:Webhooks:AllowedHosts` (host configuration, so editors cannot add hosts), each on its scheme's standard port unless its entry names another (`host:port`, added in 5.5g, ADR-0082);
  - private, loopback and link-local addresses blocked after DNS resolution (SSRF), with no redirects to other hosts;
  - timeouts and response caps;
  - secrets only as `$JironFormBuilder:Secrets:<Name>` references, never stored in the database or shown in the UI.
  - **Testing without weakening production:** an `IFormBuilderHttpTransport` lets only the `Testing` assembly reach the stub on `127.0.0.1:5560`. A test proves that production refuses loopback.
- **Backoffice:**
  - **no HTML from data in Umbraco's modals:** Umbraco's confirm modal renders its strings as HTML, and form names, entry values and file names are untrusted. Every confirm goes through one helper with a Lit template, and a source test fails on any direct use of the modal (Tandem 10.1);
  - **polls never keep a session alive:** badge counts and other polls pause while the user is idle and never renew an expired session (Tandem ADR-0051);
  - **the preview frame cannot reach the backoffice:** it is sandboxed without `allow-same-origin`, so a form's theme or scripts never get the backoffice session. Its requests therefore come from the opaque origin `null`, which the public API accepts only with a short-lived signed preview ticket bound to the form (a finding of Tandem's security review);
  - every management endpoint is authorised by Jiron Form Builder permissions, with tests that call each endpoint as a user who can see the resource but lacks the permission (`403 forbidden`) and as one who holds nothing on it (`404`, ADR-0013); endpoints that any user of the section may call declare the section requirement and are tested with a user without the section; the form picker is the one listed exception, authorised by content access;
  - sensitive values are masked unless the user has "view sensitive data", and exports leave them out unless chosen and permitted;
  - the audit log records views of entries with sensitive data, exports, deletions, state changes, settings and permission changes.
- **Logging:** logs never contain field values, email addresses, tokens, file names or IP addresses (ADR-0009). Tests capture the logs and check.
  - MVC logs action arguments at Trace through their `ToString`, and records print every property there, so request models that carry values or tokens seal a `ToString` that prints only their type.
  - Job runs log exception types and codes, never messages, because database and mail server messages can quote personal data.
- **Email:**
  - line breaks in header values are replaced by a space (MimeKit alone would drop them and glue the words, 1.6), and the sender always comes from configuration, never from a visitor;
  - links in emails come only from the configured base URL, never from the request's Host header;
  - attachments are capped.
  - **Mail to an address a visitor typed** (confirmations, verification, resume links) is the main abuse surface of any form: a bot can make the site send mail, under the site's own name, to anyone. Tandem's reviews found this four times. So:
    - each recipient address gets at most a few such mails per day across all forms (default 3), and each site at most a set number per hour (default 200), both host-configurable;
    - visitor text in such a mail is plain text: links typed by the visitor are never clickable, and HTML is escaped;
    - spam and test entries never trigger them (§4.4);
    - a mail over a limit is not sent, and the delivery log shows it as "limited", never silently dropped;
    - a health check warns when the limits are reached often.
  - Mail to the team (fixed addresses from the form) is not capped this way, but it carries no visitor-controlled header except a validated reply-to.
- **Privacy defaults:**
  - no IP stored;
  - retention on (submitted 365 days, approved 365, rejected 90, spam 30; editable);
  - sent emails purged after 30 days;
  - no cookies, and no browser storage unless the visitor uses "save and resume";
  - insights are counters only;
  - the full table of defaults, test entries, drafts, files, the audit log and statistics included, is ADR-0009.
- **CSP-friendly website runtime:** no inline scripts or event handlers, no style attributes, a nonce option for the script tag, and no `eval`. On cached pages `script-src 'self'` is enough. A nonce works only on uncached pages: a cached page keeps an old nonce and the browser blocks the runtime (1.4a). Lazily imported chunks inherit the module script's nonce, with or without `'strict-dynamic'` (ADR-0004).

### 4.11 Ports, folders and local files

| Port | Use |
| --- | --- |
| 5500 | Demo site (`scripts/demo.ps1 start`) |
| 5510 | Research sandbox (Phase 1) |
| 5520 | E2E test host (fresh database per run) |
| 5521 | Static page server for headless and cross-origin tests |
| 5522 to 5529 | Extra E2E hosts for suites that need their own (privacy on 5522, load on 5523, resilience with restarts on 5524, the strict site behind the output cache and a strict CSP on 5525, 6.4k; the hosts that must refuse to start on 5526 and 5527, 4.4a, which never listen; refused uploads over HTTPS and HTTP/2 on 5528 and 5529, 7.3d), §3.3 |
| 5530 | Install-test site (packages from the local feed) |
| 5531 to 5539 | More E2E hosts that run beside the others (places and members on 5531, protection on the page on 5532, the two hosts of the load-balancing suite on 5533 and 5534, on one SQL Server database in CI, and the designer locks on 5535) |
| 5540 | Umbraco 18 sandbox (Phase 19) |
| 5541 | Umbraco 18 demo (Phase 19) |
| 5550 | Vite dev server for the client packages (`--port 5550 --strictPort`) |
| 5551 | Playwright HTML report server |
| 5560 | Local HTTP stub for webhooks, URL option lists and captcha verification (test hosts only; see §4.10) |
| 5562 to 5596 | The browser tests' second and third sites, when `scripts/e2e-gate.mjs` runs several side by side: each its own copy of every port the first site's hosts, stubs and static page use, in the same order: 5562 to 5578 and 5580 to 5596 (`tests/e2e/support/sites.ts`) |

Servers that Jiron Form Builder starts use only these ports. Ports the OS assigns automatically are fine. Never touch 1210, 1211 or 5400 to 5499, and never connect to `MSSQL$UMBRACO` (CLAUDE.md rule 6).

`.local/` is git-ignored. It holds:

- `credentials.json`: generated users for every test role (admin, designer, entry handler, viewer, privacy officer, no access, and an outsider without the Forms section) and two members, per site;
- `sandbox/`, `install-test/`, `e2e/`;
- databases;
- `mail-pickup/`;
- Playwright traces and raw screenshots;
- `nuget-feed/`.

### 4.12 Quality budgets

- **Website runtime:** ≤ 16 KB gzip for the core, with features loaded on demand (uploads, signature, repeater, calculations, captchas): ≤ 100 KB gzip with everything (the owner, 2026-11-14; ADR-0251, raising the 60 KB of ADR-0146). The default theme CSS is ≤ 10 KB gzip. Nothing loads from third parties (§4.10). The files are served compressed and cacheable (6.2), because Umbraco's static files are neither, and a page with a form scores the same in Lighthouse as the page without it (16.2).
- **Server speed (demo machine, SQLite):** each budget is measured at a stated concurrency. SQLite has a single writer that Umbraco itself also uses, so parallel writes queue there (Tandem ADR-0063). The load test (16.2) therefore also reports where SQLite stops keeping up, and the docs steer busy sites to SQL Server.
  - rendering a 50-field form takes ≤ 20 ms from the cached definition (1.4a: a median of 0.12 to 0.15 ms, ADR-0004);
  - a submission (store plus outbox) takes ≤ 150 ms at p95, with up to 10 submissions arriving at the same moment on SQLite (ADR-0003);
  - the first page of entries for a form with 100,000 entries loads in ≤ 300 ms, and a 10,000-entry CSV export streams in ≤ 5 s;
  - the first email leaves ≤ 10 s after a submission.
- **Backoffice:** a 300-field form keeps designer interactions under 100 ms, and the form tree with 1,000 forms opens in under 1 s. Its files (ADR-0183; measured at 9.6b: 54.47, 55.14 and 246.76 KB): what every backoffice session loads at its start, in any section, ≤ 64 KB gzip (the entry, its static imports, and the words of English and of the largest other language, as Umbraco loads both); any one file ≤ 64 KB gzip; the whole bundle ≤ 512 KB gzip.
- **Accessibility:** WCAG 2.2 AA for every theme and every backoffice screen: axe clean, keyboard only, screen-reader names, visible focus, target sizes, reduced motion. Every theme preset passes contrast, and custom theme colours get a live contrast warning.
- **Coverage:** ≥ 85% line coverage on Core, every public and management endpoint covered by integration tests, and every shared vector passing in both runtimes.
- **Builds:** zero warnings (warnings as errors), analyzers on, `dotnet format --verify-no-changes` clean, ESLint and Prettier clean.

### 4.13 UX principles (backoffice and website)

- **Calm, uncluttered screens** (the owner's explicit requirement), with measurable rules:
  - **One primary action per view**, placed where Umbraco puts it (the workspace footer and save bar).
  - **At most three visible secondary actions** in any toolbar or header. Everything else lives in a "…" menu.
  - **Contextual actions appear in context:** on the selected element, on hover *and* keyboard focus, never as permanent rows of icons.
  - **Progressive disclosure.** The inspector shows "Essentials" open, and "Validation", "Rules", "Appearance" and "Advanced" collapsed, remembered per user. A settings group shows at most seven settings before "More options".
  - **Defaults that are right for most people,** so most editors never open "Advanced".
- **Say it once** (the owner's rule from Tandem, Tandem ADR-0079). Each fact appears once per screen.
  - **Where you are:** the section, a form's name or a view is never shown as a highlighted tab, a sidebar heading and a page title all at once. The place Umbraco already shows it is enough.
  - **Counts and states:** a count shows in the badge *or* the heading, never both. A state shows as a chip *or* a sentence, never both.
  - **Lists:** a list grouped under a heading doesn't repeat that heading on every row.
  - **Detail panels:** a detail panel doesn't repeat what the list row beside it already shows.
  - **Marks:** a label or badge that restates its neighbour ("AI" beside "AI assistant") goes.

  The checklist of every UX review (2.2, 9.6, 10.4, 11.6, 12.6, 16.6) has a "said twice?" line per screen.
- **Nothing squashed on narrow screens.**
  - **Text keeps its width:** controls wrap to a row of their own, and a close button sits in a corner, never between a text and another button.
  - **A test measures it:** layout tests assert the geometry (for example "the message keeps more than half the bar's width at 375 px"), not only that the elements exist.
- **One visual system.** Phase 2 fixes the design tokens, and every later style uses them: a hand-picked radius, colour or shadow is a review finding.
  - **Tokens:** one corner scale, one shade, the spacing grid and the type scale.
  - **Tandem's conventions,** which the owner approved ("I really love how it's looking"; Tandem ADR-0075):
    - two radii in the backoffice;
    - one page frame;
    - no eyebrow labels above headings;
    - dots only for real states;
    - empty values read "none" or "no data" rather than a dash.

  Tandem's screenshots (`docs/screenshots/` there) are the polish bar; the owner's packages should feel like one family, while each stays native to Umbraco.
- **It feels native to Umbraco.** Use the backoffice's UUI components, spacing, typography, icons and patterns: trees, workspaces with views, the save bar, sidebars, modals, confirm dialogs and toasts. No foreign design system.
- **Every screen has every state:**
  - empty states that say what to do next ("No entries yet. Share the form, or preview it and send a test entry");
  - loading skeletons;
  - errors with a way out;
  - success feedback.
- **A guided first run.** The Forms dashboard starts as a checklist with deep links, and each item turns green by itself when done:
  1. Check email sending.
  2. Create a form from a template.
  3. Preview and send a test entry.
  4. Put the form on a page (with "create a form block type for me").
  5. Choose who sees entries.
- **The designer is the hero.**
  - **Layout:** the canvas shows the form as visitors will see it. The palette and the inspector are side panels that can be collapsed, and the canvas width switches between phone, tablet and desktop.
  - **Editing:** insert points between fields, drag and drop *and* "Move to…" for keyboard users, inline label editing, undo/redo (Ctrl+Z / Ctrl+Y) for every designer change, and duplicate with Ctrl+D.
  - **Feedback:** the form check is a single badge that opens a list of issues with "fix it" links. Rules show as small chips on the elements they affect.
- **Plain language and one vocabulary** (CLAUDE.md conventions). Labels say what happens ("Delete approved entries after 365 days"), not internal names. Help text sits under each setting, and a "Learn more" link goes to the docs page.
- **Safe by design:**
  - Reversible actions use **undo** instead of confirm dialogs.
  - Destructive ones confirm and name what will be lost ("Delete 23 entries and 4 uploaded files").
  - Publishing shows what changed since the live version.
  - Host-locked settings say which configuration key locks them.
- **Fast for entry handlers:**
  - keyboard shortcuts in entries (`j`/`k` move, `Enter` opens, `a` approves, `r` rejects, `s` marks spam, `/` searches, `Esc` closes), listed in a `?` overlay;
  - the detail panel opens beside the list, without losing the place;
  - optimistic updates, and no full reloads.
- **Live preview** wherever looks or texts change, and a **test** button wherever something talks to the outside: email, webhook, action.
- **The visitor's side is calm too:**
  - one column on phones, large targets (≥ 44 px), labels above fields, help before the input;
  - errors that say how to fix them;
  - never losing answers (back, errors, refresh with save and resume);
  - progress shown on long forms;
  - no captcha puzzles by default.
- **Simple to find a way through** (the owner, 2026-11-16, ADR-0254; built in Phase 22): one way to do a thing, in the place where the thing is; pick first, details after; in the designer the form is the widest thing on the screen, and no column stands empty; what is fixed in number is never pushed out of sight by what grows; one level of folding on a screen; Umbraco's pattern before an own one. Where Phase 22 changes something above (the palette as a side panel, the save bar on every view of a form), its step's ADR says so.
- **Checked, not assumed.** Each UI phase ends with a review of every screen as every role, in light and dark, in English and German, with screenshots:
  - **Widths:** website screens at 375, 768 and 1440 px; backoffice screens at 1024, 1440 and 1920 px. Umbraco's own backoffice needs about 920 px (Tandem 7.9), so at narrower widths the review only checks that Jiron Form Builder's screens break no worse than Umbraco's.
  - **Making the pictures:** a UX-walk script (ported from Tandem's `ux` project) takes all of them in one run, so a review never skips a variant for time.
  - **Critique:** three design skills run in every review (the owner, 2026-10-22; ADR-0019, ADR-0020): `design-taste-frontend` for the design read and its pre-flight check, `impeccable` for critique, audit and polish, and `emil-design-eng` for motion and interaction feedback.

### 4.14 Upgrade and compatibility policy

- **Semantic Versioning** within each Umbraco major line. A patch never needs a migration, and a minor may add migrations but never breaks public C#, HTTP or JS APIs. Prereleases are `-alpha.N`, `-beta.N` and `-rc.N` (ADR-0010).
- **The public surface** is listed in `docs/extending.md`: public C# types, `/jfb/api/v1`, the definition schema and the public projection, the JS API, DOM events and `data-jfb-*` attributes, theme view names, the markup contract and CSS custom properties, and host configuration keys (ADR-0010).
  - Breaking changes there need a new major or a new API version, served beside the old one for at least the rest of the major and the whole next one.
  - The management API belongs to the backoffice, which ships in the same package version. It changes by addition where it can, but it is not part of the guarantee, and the upgrade notes list any change that could break a site's own script.
  - Deprecations carry `[Obsolete]` with the replacement and keep working for the rest of their major and the whole next one, so each lasts at least a full major (D21, ADR-0010).
- **Data:** migrations are idempotent and forward-only. Definition and settings JSON carry `schemaVersion`, and code upgraders are tested with stored fixtures of every released version.
- **Theme views are versioned.** Each one starts with a comment naming its contract version. The upgrade notes list every view whose contract changed, so sites with overrides know what to check.
- **`docs/upgrading.md`** has one section per release, written in the release step.

---

## 5. Phases and steps

### Phase 0: Repository bootstrap (license first)

- [x] **0.1 Preflight and the license root commit** (2026-10-22, 781401e)
  - **Tools:** check `dotnet --list-sdks` (10.0.401), `node --version` (24.x), `git --version`, and Edge at `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`.
  - **The commit identity, before anything else:** the machine's global git identity is the owner's personal one.
    - Set the repo-local identity: `git config user.name "Jiron"` and `git config user.email "59139789+Jiron@users.noreply.github.com"` (GitHub's own address for the owner's account).
    - Check that `git config --local user.email` prints it.
    - The pre-commit hook of 0.2 refuses any other identity.
  - **The license root commit** was made by the owner on 2026-10-20: `781401e802e4e3e9ed33bbaeb40a8a8f55bc1c3a` ("chore: initial commit under the PolyForm Perimeter License 1.0.1"), with owner commits on top. The license is verified, never written: the SHA-256 of its `LICENSE.md` must be `1fab54a90ec00024a95f27efd3145eb1437747454d17291bb027836e4fa21466`. Check it with read-only commands:
    - `git rev-list --max-parents=0 origin/main` prints exactly that commit;
    - `git show --name-only --format= 781401e` lists exactly `LICENSE.md`, `README.md`, `.gitignore`, `.gitattributes`, `CLAUDE.md`, `ROADMAP.md`, `GOAL.md` and `docs/OWNER-TODO.md`;
    - the SHA-256 of `git cat-file blob 781401e:LICENSE.md` and of `git cat-file blob HEAD:LICENSE.md` both equal the value above, so nothing has touched the license since;
    - `git ls-files` lists nothing under `.private/`, and `git log --all --name-only` names no `.private/` path;
    - **the term check:** a Node one-liner reads every pattern in `.private/terms.txt` and checks the files and the message of `781401e`. It prints only counts, and reports 0 patterns when there is no list file;
    - the local `main` equals `origin/main` (fast-forward only, never discard local work).
  - **If anything differs, never force-push.** The owner's one-time permission to replace GitHub's README-only commit `06f126f` is used up, because that commit is already gone.
    - Describe the difference.
    - Mark 0.1 `[~]` with OWNER-TODO "the root commit is not the expected license commit", or "a private term is in the root commit". Only the owner can decide about history.
    - Continue with 0.2 on top of the remote's `main`.
  - **The folder** should contain:
    - `.git/`, `.gitignore`, `.gitattributes`;
    - `LICENSE.md`, `README.md` (Appendix A), `CLAUDE.md`, `ROADMAP.md`, `GOAL.md`;
    - `docs/OWNER-TODO.md` and `docs/research/tandem-lessons.md`;
    - possibly the owner's git-ignored `.private/` folder, and a `.claude/` folder created by Claude Code (also ignored).

    List anything else in 0.2's WORKLOG line, and commit it only when a step needs it.
  - **The private folder:** `git check-ignore -v .private/GOAL.md` must name the `.gitignore` rule, whether or not the folder exists.
  - **The reference folders** exist and can be read: `C:\Code\Ligata.Forms` (with `8f4b0c5` present, §3.1), `C:\Code\Umbraco.Community.AI.Tandem` and `C:\Code\ligata`. If one has moved, find it, fix the paths in CLAUDE.md and §3, and say so in WORKLOG.
  - Make **no commit** in this step (the bootstrap exception).
  - Done when: the outputs above are shown, the local identity is set, and `git ls-remote origin refs/heads/main` equals `git rev-parse HEAD`.
- [x] **0.2 Repository hygiene, working files and phase review** (2026-10-22, a9f633b)
  - **Build files** (port from Tandem, §3.3):
    - `global.json` (SDK 10.0.401, rollForward latestPatch);
    - `.editorconfig`;
    - `Directory.Build.props`:
      - `net10.0`, nullable, implicit usings, warnings as errors, `latest-recommended` analysis, code style in build, deterministic, XML docs;
      - package identity: `Authors` and `Company` "Jiron" (as the owner chose for Tandem), and `Copyright` "Copyright (c) 2026 Manassés Zähnler", as in `LICENSE.md`;
      - `PublishRepositoryUrl=false` while private;
      - version 17.0.0-alpha.0;
    - `Directory.Packages.props` (central package management);
    - `NuGetAudit.props`;
    - `NuGet.config` (nuget.org plus the local feed `.local/nuget-feed`, with package source mapping so that `Jiron.Umbraco.FormBuilder*` comes only from the local feed).
  - **Scripts:**
    - `scripts/package.json` (dev-only, versions pinned): `@playwright/test`, `axe-core` and `@axe-core/playwright`, used from Phase 1 on by the spikes, wireframes and checks (Edge through `channel: 'msedge'`), plus whatever the checks below need.
    - `scripts/roadmap-status.mjs` (port):
      - **Output:** one line the `/goal` evaluator can read: `ROADMAP done=N/T open=N blocked=N blocked_missing_in_owner_todo=N next=<id> <title> head=<sha> origin_main=<sha> clean=yes|no`.
      - **Step ids** may carry a letter when a step is split (`9.3a`, `9.3b`), so the parser accepts `\d+\.\d+[a-z]?`.
      - **No silent misses:** it fails on any line starting with `- [ ]`, `- [x]` or `- [~]` that it cannot parse, so a split step can never vanish from the count.
    - `scripts/secret-scan.mjs` (port, without the key-file logic):
      - modes: staged files (default), `--tracked` and `--history`;
      - shape-based patterns;
      - forbidden paths (`.private/`, `.local/`, `*-key.txt`, `*.Local.json`, `.env*`);
      - the fake marker `JFB_FAKE`;
      - a short fingerprint allowlist.
    - `scripts/term-guard.mjs` (new):
      - **The list:** reads `.private/terms.txt` (one case-insensitive JavaScript regular expression per line, `#` for comments). `--terms <file>` names another list (used by the self-test).
      - **Modes:**
        - staged files (default);
        - `--message <file>` (the commit-msg hook);
        - `--tracked`;
        - `--history` (every added line on every ref, plus every commit message, tag message and tag name).
      - **Output:** hits name the file and line (or the commit) and the **pattern number**, never the term or the matched text.
      - **Without the list file** (other clones, CI), it prints "term list not present, skipped" and exits 0.
    - `scripts/check-text.mjs` (new): it fails on control characters (other than tab, LF and CR) in any tracked text file, on a UTF-8 BOM, and on non-ASCII characters in code and client sources outside the language tables. Docs and Markdown may hold non-ASCII text such as "Zähnler". It catches text damaged by tools (§3.3, "text gets damaged in transit"). Modes: staged files (default) and `--tracked`.
    - `scripts/fix.mjs` (new): runs every formatter that exists (`dotnet format`, ESLint `--fix`, Prettier). It grows as 3.1 and 3.2 add the tools, and runs before every gate run ("How to use").
    - `scripts/install-hooks.mjs`: `core.hooksPath .githooks`.
      - The `pre-commit` hook refuses a commit whose author or committer (`git var GIT_AUTHOR_IDENT`, `GIT_COMMITTER_IDENT`) is not `Jiron <59139789+Jiron@users.noreply.github.com>`. Then it runs the secret scan, the term guard and the text check on staged files.
      - The `commit-msg` hook runs the term guard on the message.
  - **Bootstrap bookkeeping:** tick 0.1 with the root commit's hash (0.1 has no commit of its own), and write its WORKLOG line in this step.
  - **Docs and templates:**
    - `SECURITY.md`: report privately (GitHub private vulnerability reporting once the repo is public, until then `info@ligata.ch`), what is in scope, response times as goals not promises;
    - `CONTRIBUTING.md`: how to work in the repo; outside contributions open once the contributor agreement exists (OWNER-TODO);
    - `CODE_OF_CONDUCT.md`: Contributor Covenant 2.1 with its attribution, contact `info@ligata.ch`;
    - `SUPPORT.md`: where to ask, what to include, no guaranteed support;
    - `THIRD-PARTY-NOTICES.md`;
    - `CHANGELOG.md` (Keep a Changelog, `Unreleased`);
    - `docs/WORKLOG.md` (Appendix C);
    - `docs/OWNER-TODO.md`: extended with anything new;
    - `docs/decisions/0000-template.md` (Appendix B);
    - `.github/ISSUE_TEMPLATE/` (issue forms: bug report with versions, feature request, `config.yml` pointing questions to discussions and security reports to `SECURITY.md`);
    - `pull_request_template.md`, with an original-work checkbox ("I wrote this myself and did not copy, decompile or derive it from proprietary software");
    - `CODEOWNERS` (`* @Jiron`).
  - **Phase review:**
    - Re-read Phase 0. Check that the license is the root commit and that nothing from `.private/` was ever committed (`git log --all --name-only` contains no `.private/` path).
    - Check that every commit since the root carries the noreply identity: `git log --format='%ae %ce' 781401e..HEAD` shows only `59139789+Jiron@users.noreply.github.com`.
    - Write the handover note.
  - Done when:
    - `node scripts/secret-scan.mjs --tracked` and `--history` exit 0;
    - `node scripts/term-guard.mjs --tracked` and `--history` exit 0 and report how many patterns were loaded, which is more than 0 while `.private/terms.txt` exists;
    - `node scripts/roadmap-status.mjs` prints the line, and its tests cover a split step (`1.2a`) and an unparseable checkbox line;
    - `git config core.hooksPath` prints `.githooks`;
    - `node scripts/check-text.mjs --tracked` exits 0;
    - **the hooks work,** tested in a throwaway clone under `%TEMP%\jfb-hooktest-<timestamp>` with a **synthetic** term list (one pattern, `jfb-canary-<random>`). These five are all rejected:
      - a staged file containing a key-shaped string generated at runtime;
      - a staged file containing the canary;
      - a commit message containing the canary;
      - a staged file containing a form feed;
      - a commit made with another email address (`git -c user.email=someone@example.invalid commit …`);
    - unit tests for the scripts pass (`node --test "scripts/tests/*.test.mjs"`);
    - the work and the handover note are committed and pushed.

### Phase 1: Research spikes and decisions (no product code)

All spikes run in `.local/sandbox/` (git-ignored) on port 5510. Findings go to `docs/research/*.md` and decisions to `docs/decisions/NNNN-*.md`. Copy any useful spike code into the product later, cleaned up.

- [x] **1.1 Reference inventory and feature study** (2026-10-22, c9f9be4)
  - Read Ligata.Forms 0.3.2 (§3.1, from `8f4b0c5`, not the checked-out 0.2.1) and Tandem (§3.3 and `docs/research/tandem-lessons.md`) as listed.
  - Write `docs/research/ligata-forms-port.md`. For each source file it says port, rewrite or drop, the target in Jiron Form Builder, and the lessons that apply. Write `docs/research/tandem-reuse.md` for each script, pattern and gotcha.
  - **The owner's private reference:** if the goal or `.private/README.md` asks for it, map the private material in `.private/` to this roadmap in a file **inside `.private/`**. Add roadmap steps for real gaps that fit §1, with the WORKLOG reason "added from the owner's reference material" and no other detail.
  - **Feature study (no code):**
    - Study public feature pages and documentation of form builders in general (CMS plugins and hosted form tools) for ideas beyond §1.1. Keep notes that name products **only** in `.private/research/` (git-ignored).
    - Write the tracked `docs/research/feature-catalogue.md`: every Jiron Form Builder feature and every candidate idea, **without naming any product** (CLAUDE.md rule 2), each marked "in roadmap (step)", "added (step)" or "later (§7)". Add roadmap steps for strong, in-scope ideas, with the reason in WORKLOG.
  - Done when:
    - every row of §3.1 and §3.3 is covered, both docs and the feature catalogue are committed, and the term guard passes.
- [x] **1.2a Umbraco 17 sandbox: navigation, workspaces and building blocks** (2026-10-22, 3424359)
  - `dotnet new install Umbraco.Templates@17.7.1` in the private template hive (§3.4), and every `dotnet new` there too. Sites are created by a script, so no password appears in a command line.
  - Create `.local/sandbox/Sandbox` on port 5510 with SQLite and unattended install. Generate an admin user and store it in `.local/credentials.json`.
  - **In a throwaway extension, build:**
    - a section with a sidebar menu;
    - a **tree with folders** (children, entity actions create, rename, move, delete, and a trash);
    - a **routable workspace** with four views, the save bar and workspace actions;
    - a **collection** (table view) with bulk actions;
    - a modal and a sidebar, plus Umbraco's confirm modal with a string holding markup, to confirm whether it still renders HTML in 17.7.1 (§3.3);
    - the backoffice's light and dark themes, and its narrowest usable width.
  - Start `docs/research/umbraco-backoffice.md` with the exact manifest shapes, element names and signatures, and keep screenshots in `.local/`.
  - **Building blocks:**
    - **Drag and drop:** the **sorter** across nested containers (pages → groups → fields) next to Ligata's pointer-capture drag. Measure keyboard support, nesting, scrolling and screen-reader names.
    - **Inputs:** a **Tiptap** rich text input, document and media pickers, a user group picker and a member picker inside custom elements.
    - **A property editor** (schema + UI) with a value converter and a Content Delivery API converter.
  - Done when:
    - the sandbox runs, every piece above works (Playwright screenshots), and the doc section is committed;
    - every piece works in the sandbox, its section of the doc is committed, and the drag-and-drop choice is written as ADR-0016 "Designer interaction".
- [x] **1.2b Umbraco 17 sandbox: integration points** (2026-10-22, 3dfea08)
  - A `searchProvider`, `localization` en/de, a dashboard and a health check.
  - Umbraco webhook event registration.
  - `IDataValueReferenceFactory` with a custom entity type for "used on".
  - A workspace view on the **member** workspace.
  - A UFM component for block labels.
  - **The version floor:** build the throwaway extension against `Umbraco.Cms` 17.5.0 too (its own template hive), to confirm the APIs D3 relies on exist there.
  - Fix §3.4 where it is wrong.
  - Done when:
    - every piece works in the sandbox;
    - the doc is complete;
    - every *(verify)* about the backoffice in §3.4 and §4 is resolved; the rest names the Phase 1 step that resolves it;
    - the "used on" choice is written as ADR-0017 "References".
- [x] **1.3 Spike: persistence, migrations and entries at scale** (2026-10-23, f07fdc0)
  - Create the entry tables of §4.5 with a `PackageMigrationPlan` and `AsyncMigrationBase`, both idempotent. Add a second step and confirm the upgrade behaviour. Decide `IgnoreCurrentState`.
  - **The load:** seed 100,000 entries with 12 values each on SQLite.
  - **Measure,** with and without the planned indexes:
    - the first page of a form sorted by date;
    - a filter on a text value, on a number range and on a date range;
    - full-text search on `SearchText`;
    - counts by state;
    - streaming 10,000 entries to CSV;
    - inserting 50 parallel submissions with unique entry numbers.
  - Note the SQL Server differences to test. Steps 4.1 and 4.3 own the CI tests, because test projects arrive in 3.4.
  - Write `docs/research/spike-entries.md` and ADR-0003 "Entry storage". If the relational values miss the §4.12 budgets, the ADR picks a design that meets them (for example extra computed columns), with numbers.
  - Done when:
    - a fresh install and an upgrade both pass on SQLite;
    - number and date range filters and sorts are **correct** for negative, decimal and multi-digit values and for dates across years (SQLite must not compare them as text);
    - the ADR has measured numbers that meet §4.12.
- [x] **1.4a Spike: rendering and posting** (2026-10-23, 751442b)
  - In the sandbox:
    - a view component and a tag helper rendering a three-field form;
    - a **no-JS post** through a `SurfaceController` with `BeginUmbracoForm`, re-rendering errors and doing post/redirect/get on success;
    - a JS `fetch` post to a public controller outside `/umbraco`;
    - two forms on one page;
    - a page behind ASP.NET Core output caching, with the challenge fetched lazily;
    - RCL static web assets under `dotnet run` **and** after `dotnet publish`;
    - a CSP with a script nonce.
  - Write ADR-0004 "Rendering and submission paths". It also decides where the runtime gets the new form after `form_changed` (an endpoint in §4.6 that renders the published form, or a page reload), because the page itself may come from an output cache.
  - Resolve the open no-JS note of §3.4.
  - Done when:
    - Playwright shows a no-JS submission (JavaScript disabled) and a JS submission, both with errors and success, plus published-mode assets loading;
    - with two forms on one page, each keeps its own errors and values;
    - a page served from the output cache is byte-identical for two visitors who share no cookies, and contains no per-visitor value (no antiforgery token, no challenge, no nonce);
    - the CSP pages log no violation in the browser.
- [x] **1.4b Spike: uploads and file storage** (2026-10-23, c7a9c65)
  - **Uploads:** a streamed multipart upload with size limits enforced while reading, stored through an `IFileSystem` in a folder that is not web-accessible, with a magic-number check. *(Done through an asynchronous store instead, because `IFileSystem` writes synchronously; ADR-0006.)*
  - **Downloads:** a protected download through the management API.
  - **Blob storage:** read the docs and source of Umbraco's official Azure Blob storage provider and decide how an `IFormBuilderFileStore` can use it (a sample, not a dependency). Resolve the open files note of §3.4.
  - Write ADR-0006 "File storage and uploads".
  - Done when:
    - an integration test shows that a 60 MB upload to a 50 MB limit is cut off without buffering, that a renamed `.exe` is refused, and that the stored file cannot be fetched by URL;
    - the same test shows the download working for an authorised backoffice user and refused (`401` or `403`) for an anonymous caller and a user without access.
- [x] **1.5 Spike: one logic, two runtimes** (2026-10-23, 106a893)
  - Write a minimal rules evaluator in C# and TypeScript, with 40 vectors in one JSON file run by NUnit and by Vitest.
  - **Decide:**
    - the vector format;
    - wire formats (invariant culture, ISO dates, decimal strings);
    - time zones and "today" (the per-site `TimeZone` setting of §4.8, with injected time, and vectors across a daylight-saving change);
    - number semantics (C# `decimal` against JS numbers) and the rounding rules for calculations. Stored numbers keep at most 15 significant digits (ADR-0003).
  - Write ADR-0005 "Shared logic and test vectors".
  - Done when: `dotnet test` and `npx vitest run` in the sandbox run the same vector file, and a deliberately broken TypeScript operator makes the TypeScript suite fail (shown, then reverted).
- [x] **1.6 Spike: background work and email** (2026-10-23, 86d24fb)
  - Write a distributed job with leases, and an in-process "kick" after commit through a bounded channel.
  - Simulate two servers on one database: SQLite cannot be shared reliably, so simulate the second processor in-process, and note the real two-host check, which 8.1 adds to CI.
  - Send through `IEmailSender` to a pickup folder, gated by `CanSendRequiredEmail()`.
  - **One body:** `EmailMessage` carries HTML *or* text (§3.4). The ADR decides whether Jiron Form Builder mail is HTML only, as Tandem's is (Tandem ADR-0042), or whether a text part can be added. Either way it never bypasses `IEmailSender`, because sites replace it with their own mail services.
  - **The job tick:** show that the in-process kick, not the job's period, carries the latency, with Umbraco's own jobs registered (one job per 5-second tick, §3.4).
  - Measure the time from submission to email.
  - Write ADR-0007 "Background work, outbox and email".
  - Done when: the latency numbers are in the ADR, and no action ran twice in 1,000 simulated runs with two processors.
- [x] **1.7a The foundation ADRs** (2026-10-23, 0d0cdd0)
  - ADRs:
    - 0001 solution structure and packages;
    - 0002 the form definition model, versions and the public projection;
    - 0008 the settings model (typed sections, schema versions, scopes, locks);
    - 0010 versioning, branching and the upgrade policy (§4.14);
    - 0011 testing strategy.
  - Done when: all of them are committed, and §4 of this roadmap is updated where the ADRs changed it.
- [x] **1.7b The other remaining ADRs** (2026-10-23, 5abc382)
  - ADRs:
    - 0009 privacy defaults;
    - 0012 anonymous endpoints, spam protection and challenges;
    - 0013 permissions;
    - 0014 the merge-tag language;
    - 0015 themes and the markup contract.
  - Done when: all of them are committed, and §4 of this roadmap is updated where the ADRs changed it.
- [x] **1.7c The architecture document** (2026-10-23, e14ff19)
  - Write `docs/architecture.md`, the contract for later phases:
    - modules, the dependency diagram, the definition model and its JSON schema outline, the data model with an ER diagram;
    - the public and management APIs, settings, extension points;
    - a threat model (STRIDE table), quality budgets and the glossary.
  - It draws on every ADR so far and does not repeat them: where an ADR decides something, the document says so in a line and links to it.
  - Done when:
    - it is committed with every part above;
    - a check shows that its ER diagram names every table of §4.5, and its API sections every route of §4.6;
    - all ten ADRs of 1.7a and 1.7b are committed, and §4 of this roadmap is updated where they changed it.
- [x] **1.8 Phase review** (2026-10-23, b1314ea)
  - A subagent reviews the ADRs for contradictions with §1, §2 and §4. Fix what it finds.
  - Check that no *(verify)* is left in §3 and §4.
  - Write the handover note.
  - Done when: pushed, with the phase-review gate.

### Phase 2: Experience design (no product code)

The owner's top UX requirement is calm, uncluttered screens (D18, §4.13). This phase designs every screen before any of it is built, so later phases implement a design instead of improvising one.

- [x] **2.1 Information architecture and words** (2026-10-23, aeb7bfa)
  - Write `docs/ux/information-architecture.md`:
    - the Forms section's menus, trees, workspaces and views;
    - every screen with its one primary action and its secondary actions (visible or in "…");
    - a role matrix (designer, entry handler, viewer, administrator, privacy officer, no access): who sees what.
  - Write `docs/ux/copy.md`: the glossary (CLAUDE.md vocabulary), tone (plain, friendly, direct; sentence case; verbs on buttons), message patterns for errors, empty states and confirmations, with en and de examples.
  - List the top 15 tasks per role with their click paths.
  - Done when: committed, and a subagent playing a first-time editor finds every top task in ≤ 3 clicks from the section (each path written down).
- [x] **2.2 Backoffice wireframes** (2026-10-23, 129db8d)
  - As static HTML pages in `docs/ux/wireframes/` (UUI-like styling, no build step), one per screen:
    - setup dashboard, form library, template gallery;
    - designer (canvas, palette, inspector open and closed, phone width);
    - rules builder, form check, settings, actions (list and email editor), preview, publish dialog, history and diff;
    - entries list, entry panel, all-entries inbox, spam;
    - insights;
    - option list, theme editor, email layout;
    - global settings, permissions, deliveries, audit log;
    - privacy tools.

    Each shows its empty, loading and error states where they differ.
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before the first wireframe (ADR-0019, ADR-0020), and write `PRODUCT.md`, the `impeccable` product context, in your own words from §1 and §2.
  - Render them to PNG with Playwright (`scripts/wireframes.mjs`).
  - Write `docs/ux/review-2.2.md` with a checklist per screen against the measurable rules of §4.13, including the "said twice?" line and the token rules.
  - Done when: the PNGs, `PRODUCT.md` and the review are committed, every screen passes its checklist, and the review has a section per skill with its output and findings.
- [x] **2.3 Website form design** (2026-10-23, a22f7d7)
  - **Design the default theme:**
    - field anatomy (label, help, input, error);
    - the required and optional marks, the error summary and inline errors;
    - focus styles, choice cards, uploads, signature, repeater rows;
    - progress (steps and bar), the summary page, success and closed states;
    - a phone layout.
  - **Fix the design tokens** (§4.13, "One visual system") for the website and the backoffice in `docs/ux/tokens.md`: the corner scale, the shade, the spacing grid and the type scale. For the website they are CSS custom properties that 6.3 uses as they are.
  - Build it as mock HTML and CSS in `docs/ux/website/`, with PNGs at 375 and 1440 px in light and dark site contexts.
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before the design starts (ADR-0019, ADR-0020), and write `DESIGN.md`, the `impeccable` design context, pointing to the token file instead of repeating its values.
  - Done when: axe is clean on the mocks, every text and control passes WCAG 2.2 AA contrast (checked by a script), the token file and `DESIGN.md` are committed with each skill's output, and the work is committed.
- [x] **2.4 Design critique and phase review** (2026-10-23, 6f6559e)
  - Run a design critique on 2.2 and 2.3: rerun all three design skills (ADR-0019, ADR-0020): `design-taste-frontend`, an `impeccable` critique and audit, and an `emil-design-eng` review. A subagent with the §4.13 checklist may add a fourth view. Fix every finding that is not purely taste, and re-render.
  - **Phase review:** write the handover note.
  - Done when:
    - `docs/ux/critique-2.md` lists the findings (a section per skill), the decisions and before/after images;
    - pushed, with the phase-review gate.

### Phase 3: Skeleton, tooling, demo site, CI

- [x] **3.1 Solution and projects** (2026-10-23, a245195)
  - Create the `.slnx` and the projects of §4.1 (the AI add-on and samples come in Phase 15). Base `FormBuilder.Web` on `dotnet new umbraco-extension -ex -pm Central`, then restructure. Keep the packages solution filter in `src/` (§3.3).
  - Reference packages through central management, with Umbraco as `[17.5.0, 18.0.0)` (D3).
  - Add an empty composer per project. The meta package has no code.
  - Done when: `dotnet build -c Release` has zero warnings, and `dotnet pack` produces three nupkgs (Core, Web, meta) into `.local/nuget-feed`.
- [x] **3.2 Client workspace** (2026-10-23, 521131c)
  - Create `src/Jiron.Umbraco.FormBuilder.Web/Client` as an npm workspace with `packages/logic`, `runtime`, `element` and `backoffice`: TypeScript strict, Vite library builds, ESLint, Prettier and Vitest.
  - **The backoffice package:** Lit, `@umbraco-cms/backoffice` pinned to the minimum supported 17.x, and `@hey-api/openapi-ts` client generation.
  - **The runtime and element packages:** no runtime dependencies, output to `WebsiteAssets/` and embedded in the Web assembly as `jfb/assets/<path>` (ADR-0026: packed as static web assets under `wwwroot/jfb/assets/`, the compressed copies broke the build of every site that installs the package), and code split per feature. The build also writes Brotli and gzip copies of every file, which 6.2 serves (§3.4, "Static files").
  - **Checks:**
    - `scripts/size-check.mjs` measures gzip sizes against §4.12. It also reports the backoffice bundle's size, which Tandem never measured and a form designer will make much larger; 9.6 sets its budget;
    - an MSBuild target fails `dotnet pack` when the assets are missing, stale (an input hash), for another version, or contain source maps, and when the assembly to be packed does not embed the website assets as they are (ADR-0026). Each pack builds the client for its own version (Tandem's `TANDEM001`);
    - `umbraco-package.json` gets its `version` stamped at build;
    - **the licence notice:** every emitted JS and CSS file keeps a `/*! Jiron Form Builder … Required Notice: … */` banner (the bundler's legal-comments setting), as PolyForm requires for every copy. The size check counts it, and the MSBuild target checks it is there.
  - **Ignored output:** the built assets are git-ignored (`.gitignore` lists both output folders, `wwwroot/App_Plugins/JironFormBuilder/` and `WebsiteAssets/`).
  - Done when: `npm ci && npm run lint && npm test && npm run build` passes, the size check prints the budgets, and the pack check fails on an asset without the notice (tested).
- [x] **3.3 Demo site** (2026-10-23, a1ef6a7)
  - Create `demo/Jiron.Umbraco.FormBuilder.Demo` (Umbraco 17.7.1, created in the private template hive, §3.4) on port 5500 with SQLite and unattended install. Admin credentials are generated on first start into `.local/credentials.json` by the script, never on a command line.
  - Reference the Jiron Form Builder projects.
  - **Seeder** (C#, idempotent):
    - document types, templates and a block list;
    - English and German pages: home, contact, events, careers, feedback, a members area with login, privacy, imprint;
    - a footer newsletter slot;
    - mail delivered to `.local/mail-pickup/demo`.

    Demo forms are added by the feature steps as features arrive. Each feature step updates the seeder.
  - **`scripts/demo.ps1 start|stop|reset|login`:**
    - processes are tracked by PID files;
    - the demo is never started any other way (the imaging-key lesson, §3.3);
    - `login` prints the URL and the credentials file path, never the password.
  - Done when: `scripts/demo.ps1 reset; scripts/demo.ps1 start` serves the home page in both languages, and Playwright logs into the backoffice with the generated credentials.
- [x] **3.4a Test projects and the Testing project** (2026-10-23, d701d52)
  - 3.4 was split into 3.4a, 3.4b and 3.4c on 2026-10-23: the .NET test projects, the vector runners and the browser harness are three harnesses, each with its own checks (WORKLOG).
  - **Unit:** NUnit 3 and NSubstitute.
  - Build the harness in the shape of the lessons file, part 2 (§3.3).
  - **Integration:** `Umbraco.Cms.Tests.Integration` 17.x, set up per Tandem's lessons (§3.3), with a sample test that boots Umbraco with Jiron Form Builder's composers.
  - **Testing project:**
    - a local HTTP stub on 5560 that records webhook calls and plays captcha verification answers;
    - fake time;
    - users and members factories;
    - a mail pickup reader;
    - the test-only endpoints (`/jfb-testing/…`), guarded to test hosts.
  - Done when:
    - `dotnet test` passes, with the integration sample test booting Umbraco with Jiron Form Builder's composers;
    - a test proves that a host outside Development has no `/jfb-testing/` endpoint.
- [x] **3.4b Vector runners and the tables check** (2026-10-23, 49da93b)
  - **Vectors:** a runner that turns `tests/vectors/*.json` into NUnit cases and Vitest cases (the `jfb-vectors/1` format, one test case per vector, files read in place; ADR-0005), and the tables check: a script writes fold, clean and recomposition for every code point from the TypeScript side, and an NUnit test compares them, with known gaps listed exactly.
  - **The logic they run:** the runners need the code they test, so port the spike's modules of 1.5 (text, exact decimals, calendar values, patterns, conditions and rules, calculations; ADR-0005) into Core and `packages/logic` as the start that 5.2 and 5.3 complete, and its vector file into `tests/vectors/` with the script that writes it as ASCII.
  - Done when:
    - both vector runners run the spike file of 1.5 with every case passing, and the tables check passes;
    - `dotnet test` and the client's `npm test` pass.
- [x] **3.4c The browser harness** (2026-10-23, 78d494e)
  - Build it in the shape of the lessons file, part 2 (§3.3).
  - **E2E:** Playwright Test, with Edge locally and Chromium in CI.
    - **Global setup:**
      - starts a fresh host on 5520 with its own database and mail folder, and waits for Umbraco's start-up work. The wait looks for a log marker that appears whether the Content Delivery API is on or off: Tandem's waited for "The Delivery API is not enabled", which 6.6 would make disappear;
      - creates the users admin, designer, entry handler, viewer, privacy officer, no-access and outsider (no Forms section), plus two members. Step 4.6 adds their Jiron Form Builder permissions;
      - writes the hang marker in its teardown.
    - **Fixtures:** `loginAs`, `apiAs`, `waitForMail`, `fillForm`, `submitForm`, and the sign-in lock.
    - **Projects:** the ones of the lessons file, each added to the config together with its first spec (`smoke` now).
  - Done when:
    - `dotnet test` and `npx playwright test --project=smoke` pass;
    - a test of `support/host-health.ts` shows that a hung database writes the marker, and that the start-up wait ends with the Delivery API on and with it off.
- [x] **3.5 CI, the local CI mirror and phase review** (2026-10-23, 14d3f5a)
  - **`.github/workflows/ci.yml`** (ubuntu-latest):
    - checkout, setup-dotnet 10 and node 24 with caches;
    - client lint, tests and build;
    - Release build with `dotnet format` check;
    - unit and integration tests on SQLite;
    - a **SQL Server** job (service container `mcr.microsoft.com/mssql/server:2022-latest`);
    - a Playwright smoke job;
    - a **load-balancing** job (two demo instances on 5500 and 5501 sharing one SQL Server database), filled in by step 8.1;
    - uploaded test results, and concurrency cancel.
  - **Gating:** pushes to `main` and `v18/main` run the jobs only when `vars.JFB_CI == 'true'`; `workflow_dispatch` and version tags always run. The `v18/main` trigger is there from the start, so Phase 19 doesn't have to remember it.
  - **`dependabot.yml`:** nuget, npm and github-actions, with `open-pull-requests-limit: 0` until the owner turns it on.
  - **Checks:**
    - `scripts/check-workflows.mjs` (GitHub's MIT parser);
    - `scripts/check-npm-licences.mjs`, over every lock file (`scripts/` and `Client/`, later `tests/e2e/`): rule 4's list for packages that ship, and for development packages also the public-domain dedications of ADR-0027, tested as ADR-0027 says;
    - `scripts/check-audit-suppressions.mjs` (port, ADR-0024): fails when a package whose advisories `NuGetAudit.props` suppresses is referenced directly (`PackageReference` or `PackageVersion`), and when an ImageSharp entry leaves the host-only group. A test covers both;
    - `scripts/check-nuget-licences.mjs` (new: reads each restored package's nuspec license and fails on anything outside CLAUDE.md rule 4);
    - `scripts/third-party-notices.mjs` (generates and checks `THIRD-PARTY-NOTICES.md`). It reads the nuspecs from `NUGET_PACKAGES` when that is set, as CI sets it, and from the default cache otherwise; Tandem's read only the default cache, so its CI check would have failed on a fresh runner. A test covers both.
  - **`scripts/ci-local.ps1`** runs everything except SQL Server and load balancing: the gate.
    - **Stages:** the stages of the lessons file, each judged by its exit code only (PowerShell 5.1 turns stderr into errors).
    - **Reruns:** it reruns the browser projects once only when the hang marker exists, and says so.
    - **Output:** it prints the time per stage, so a slow gate shows where the time went.
  - **Phase review:**
    - Review the build files for duplication, and check that the pack contents look right (`dotnet pack` + unzip listing).
    - Write the handover note.
  - Done when:
    - `scripts/ci-local.ps1` passes, and the workflow check passes. Add "set `JFB_CI`, raise the Dependabot limit, and check the first CI run (including the SQL Server and load-balancing jobs)" to OWNER-TODO;
    - pushed, with the phase-review gate.

### Phase 4: Core domain and persistence

- [x] **4.1 Schema v1 and migrations** (2026-10-23, 19fd0d9)
  - Create the DTOs and frozen snapshots for every table in §4.5, plus the `PackageMigrationPlan` (per ADR-0003 and the 1.3 spike). The filtered unique indexes use custom SQL for both databases.
  - The plan grants the Forms section to the administrators group once (a `jfbKeyValue` flag), the way Umbraco's own migrations do it (§3.3), and writes the default settings rows.
  - **SQL Server without a server:** a test renders the schema DDL through Umbraco's SQL Server syntax provider, with no connection, and compares it with a reviewed snapshot. That catches type, cascade-path and index problems before CI does *(verify feasibility in this step; otherwise an ADR picks the strongest offline check)*.
  - Done when:
    - integration tests prove that a fresh install on SQLite creates every table, index (including the filtered ones) and foreign key, checked with Jiron Form Builder's own schema queries;
    - a schema test shows that no nullable column is part of a primary key or a plain unique index (§4.5);
    - a second run is a no-op;
    - two entries with a NULL `ChallengeId` can be stored;
    - a test shows the ADR-0003 schema on SQLite: `typeof` gives `real` for a stored number and `text` for a date, the value and entry indexes are exactly those of §4.5, `jfbEntryValue.FieldId` has no cascade, and the plan states `IgnoreCurrentState => false`;
    - the SQL Server DDL snapshot test passes;
    - the same tests are part of the SQL Server job in `ci.yml` (show the job definition; the owner checks the CI result).
- [x] **4.2a The definition model and its JSON** (4.2 split in two, WORKLOG 2026-10-23) (2026-10-23, cb3025d)
  - **The model:** C# records for §4.3, System.Text.Json with source generation, and `LocalizedText`.
  - **Reading and writing** (ADR-0002): the strict reader with the save-time refusals and their codes (unreadable JSON, unknown properties, wrong types and values, duplicate keys, an unsupported `schemaVersion`, the size, text and element limits of §4.3.1), and the canonical form with `DefinitionHash`.
  - **The schema:** `schemas/jfb-form.v1.schema.json`, with a test that validates serialised definitions against it (a JSON-schema library is allowed as a test-only dependency if MIT or Apache-2.0).
  - **Upgraders:** the definition upgrader framework, tested with a synthetic older version.
  - Done when:
    - round trips are lossless: a property-style test over generated definitions writes, reads and writes each one to the same bytes and the same model, and a fully populated document is read and written byte for byte;
    - serialised definitions validate against the schema, and a test shows that the schema and the model name the same properties, with the same required ones, for every object;
    - every save-time refusal has a unit test that checks its code and its JSON path;
    - the upgrader test reads a synthetic older version through the real reader.
- [x] **4.2b Structural validation and the public projection** (2026-10-23, 54debdd)
  - **Structural validation**, with stable error codes:
    - limits, alias rules and reserved aliases;
    - aliases unique without regard to case, and the option values of a field unique once folded as rules compare text (ADR-0005), because SQL Server ignores case where SQLite does not, and entry filters compare folded text (ADR-0033);
    - unique keys, existing references and cycles;
    - pattern safety.
  - **The public projection** and its hash.
  - Done when: unit tests cover every structural rule (aliases that differ only by case and option values that fold to the same text included), and a property-style test over generated definitions shows that the public projection never contains action settings, recipients or option-source configuration.
- [x] **4.3a The database gate and the form repositories** (4.3 split in six, WORKLOG 2026-10-23) (2026-10-24, 067274a)
  - **The database** (D7, ADR-0007): every unit of work runs in its own Umbraco scope behind one async gate (port the pattern, §3.3). A write starts with a write; a unit the database turns away with a transient error runs again where that is safe. Paging, filtering and sorting happen in the database, and no repository reads and then writes in one transaction to change a state.
  - **One way to the database** (ADR-0007): Core opens no database connection of its own, because on SQLite a connection outside Umbraco's shared cache can hang Umbraco's own transactions (1.6).
  - **Repositories,** one per aggregate, all async: key-value, folders, forms with versions and the field catalogue, and settings.
  - **Aliases** of forms are looked up without regard to case on both databases, as they are unique (ADR-0033).
  - Done when:
    - integration tests cover CRUD for each of these repositories, and concurrency conflicts on forms and settings;
    - a lookup by alias finds the form in any letter case (ADR-0033);
    - a test fails the build when Core constructs a database connection of its own (ADR-0007).
- [x] **4.3b The library and permission repositories** (2026-10-24, 6410aaf)
  - **Repositories:** option lists, themes, email layouts, templates and permissions, through the gate of 4.3a.
  - **Aliases** (option lists, themes, email layouts) are looked up without regard to case on both databases, as they are unique (ADR-0033).
  - Done when: integration tests cover CRUD for each of these repositories and concurrency conflicts on the library rows that carry a concurrency version; a lookup by alias finds the option list, theme or layout in any letter case (ADR-0033).
- [x] **4.3c Entries, their values, files, events and notes** (2026-10-24, ad7fef5)
  - **Repositories:** entries with their values and file rows, events and notes.
  - **Entry numbers** come from the atomic update of ADR-0003, as the first write of the unit, with the unique index as the backstop. The values of a page load by `EntryId IN (…)`, at most 1,000 ids per statement.
  - **Text keys:** `jfbEntryValue.TextKey` is the value cleaned and folded as rules compare text (ADR-0005), so the entries screen filters as rules decide.
  - Done when:
    - integration tests cover CRUD for each of these repositories, and **unique entry numbers under 50 parallel inserts**;
    - a unit test shows `TextKey` folding as the rule vectors of 1.5 compare (the Turkish i's, final sigma, no folding outside the folded blocks).
- [x] **4.3d The entries query builder** (2026-10-24, 81ce702)
  - **The entries query builder** turns typed field filters into SQL against `jfbEntryValue`, using only the shapes of ADR-0003 (`IN` subqueries, keyset paging, the value-index walk for a sort by a value, one-pass search with a count up to 1,000) under the per-statement time budget.
  - **Text keys** (ADR-0038): a filter on text seeks `textKey` with the column's collation and then compares the key again in a binary collation, because SQL Server's case-insensitive collation can treat keys as equal that rules keep apart (`ß` and `ss`). A key that may have been cut (`EntryTextKey.MayBeCut`) compares the full text too. (Added from the review of 4.3c, WORKLOG 2026-10-24.)
  - Done when:
    - integration tests cover every filter kind, with the correctness cases of 1.3;
    - a filter for `straße` does not find `strasse`, nor the reverse, on SQLite and in the SQL Server job, and a filter for a text longer than its key finds only the entries with that full text;
    - on SQLite, `EXPLAIN QUERY PLAN` tests show every shape of the query builder on its index, with and without planner statistics: no `SCAN` of `jfbEntryValue` and no temporary b-tree for a sort by a value (ADR-0003);
    - a statement past its time budget is interrupted and answered with `query_timeout`, and a submission made meanwhile still succeeds; a search with more than 1,000 matches reports "more than 1,000".
- [x] **4.3e The outbox repositories: action runs and emails** (2026-10-24, d5cae4c)
  - **Repositories:** action runs and emails (the processor is 8.1, the mail queue 8.2).
  - **Every state change** of an action run or an email is one conditional statement that names the row, its token and its state, and no repository reads and then writes in one transaction (ADR-0007): the claim with its order condition, the outcomes, expired leases, the late finisher and stopping the rest.
  - Done when: integration tests cover CRUD for both repositories and every state change, and a change with a stale token or state changes nothing (ADR-0007).
- [x] **4.3f The file store, the audit log and daily stats** (2026-10-24, 7f3acd3)
  - **The file store:** the physical `IFormBuilderFileStore` per ADR-0006, in a folder that is not web-accessible. Uploads arrive in 6.1, hardening in 7.3.
  - **The audit log and daily stats:** port the pattern (§3.3), with their repositories.
  - Done when:
    - integration tests cover storing, reading and deleting files, with a failed or cut-off save leaving nothing behind (ADR-0006), and CRUD for the audit and stats repositories;
    - tests prove there are no field values in audit details, and that counters are correct under parallel increments.
- [x] **4.4a Host options and start-up checks** (4.4 split in two, WORKLOG 2026-10-24; port the pattern, §3.3) (2026-10-24, f066f70)
  - **Host options** (ADR-0008, ADR-0042): one `JironFormBuilder` tree, bound and validated at start (`ValidateOnStart`), which takes in the `Files` of 4.3f. It holds the definition limits of 4.2a (`Limits:Definitions`), which the reader and the structural validation already take, `Designer:SettingLocks` (applied by 5.1 and 8.1) and `LockedSections` (applied by 4.4b). Every message names the key and what is allowed, and an unknown key is refused. A lowered limit never makes a stored definition unreadable: the site reads what it stored with the highest limits a host may set.
  - **Globalization** (ADR-0005): start-up refuses invariant globalization, which turns normalisation off and loses IANA time zones on Windows.
  - **The IntelliSense schema:** ship a JSON schema for `appsettings` (with a `!` exception in `.gitignore`), and test that its `$ref`s resolve from the file itself (Tandem's first one resolved them from the options class, Tandem 11.1b).
  - Done when: unit tests cover every host validator and default; a lowered `Limits:Definitions` limit refuses a definition that the default allows; an invalid host config fails start-up with a clear message; a site with invariant globalization fails start-up with a clear message. Both start-up failures are also shown in a real host process.
- [x] **4.4b The backoffice settings sections** (port the pattern, §3.3) (2026-10-24, f873d90)
  - **Model:** typed sections (§4.8) with defaults, validators and JSON schema versions with migrations, plus global and per-site scope.
  - **Host rules:** locks win over the backoffice: `LockedSections` and the rules of ADR-0008 (locked, restricted, clamped), applied where a value is used, without rewriting stored rows.
  - **Runtime:** a cached reader invalidated by a cache refresher, and a `SettingsSaved` notification.
  - **Globalization** (ADR-0005): saving a time zone the server cannot resolve is refused.
  - Done when: unit tests cover every validator and default; migrating an old payload works; a lock overrides the backoffice value; an unknown time zone cannot be saved.
- [x] **4.5a The form service: create, save and publish** (4.5 split in four, WORKLOG 2026-10-24) (2026-10-24, a664533)
  - **Create from blank,** with the configured new-form defaults: the consent field, a `general` setting for every site at once, since a new form belongs to no site (§4.8). The notify-the-creator default needs the email action, so it comes with it (8.3).
  - **Save and publish:**
    - save draft with a concurrency check;
    - publish: run the structural validation (4.2b), update the field catalogue, and bump the public revision only when the public projection changed. Publishing is a pipeline that later steps extend: the form check joins in 9.5b, the mirror file in 12.5;
    - unpublish;
    - rename and move (the move of "Copies and moves").
  - **Reading:** the service reads stored versions with `ReadStored`, so a host that lowers a definition limit never makes a stored form unreadable; saving or publishing it is refused until it fits (ADR-0042).
  - **Notifications** for these lifecycle events, and an audit entry for each change in the unit that makes it. Its new statements follow the guard rule of ADR-0038: every check of other rows is a locking read on SQL Server, which `SqlGuardTests` enforces.
  - Done when: unit and integration tests cover each operation, including:
    - "a form above a lowered definition limit still opens, and saving it returns `limit_exceeded`";
    - "a stale save returns `concurrency_conflict`".
- [x] **4.5b Versions** (2026-10-24, 119aa26)
  - **Versions:** list, diff, restore (as a new version), pin and comment.
  - **The draft-version cleanup,** the first part of the retention job (D8, ADR-0007), with its `general` setting: how long draft saves stay in a form's History, for every site at once (§4.8).
  - **Notifications** for these lifecycle events and audit entries; new statements follow the guard rule of ADR-0038 (`SqlGuardTests`).
  - Done when: unit and integration tests cover each operation, including "restore creates a new version".
- [x] **4.5c Copies, templates and import** (2026-10-24, 8d48ffd)
  - **Create from a template:** ship the built-in "Contact" template now, as embedded JSON (12.3 adds the others).
  - **Create by import.**
  - **Copies:**
    - duplicate, with or without actions, remapping every key and every rule, action and outcome reference;
    - copy actions to another form, remapping fields by alias and reporting the unmapped ones.
  - **Notifications** for these lifecycle events and audit entries; new statements follow the guard rule of ADR-0038 (`SqlGuardTests`).
  - Done when: unit and integration tests cover each operation, including "duplicate remaps rule references".
- [x] **4.5d Trash, delete and folders** (2026-10-24, 1528309)
  - **Trash and delete:** trash, restore, delete permanently (cascading entries, files, versions, permissions, references and daily stats; references and stats name the form without a foreign key, ADR-0033), empty trash. The delete removes the form's row before its daily stats, in the same unit, so a count that waited for the row counts nothing (ADR-0041).
  - **Folders.** Trashing a folder takes the folders and forms in it to the trash with it, and restoring it brings back what went with it (the repository of 4.3a marks one row at a time, ADR-0036).
  - **Notifications for every lifecycle event** and audit entries. Its new statements follow the guard rule of ADR-0038: every check of other rows is a locking read on SQL Server, which `SqlGuardTests` enforces.
  - Done when: unit and integration tests cover each operation, including:
    - "delete removes the files from the file store";
    - "delete leaves no reference or daily stat of the form".
    - "a stale save returns `concurrency_conflict`".
- [x] **4.6a The permission model and its evaluation** (4.6 split in two, WORKLOG 2026-10-24) (2026-10-25, ea94a41)
  - **The model** (ADR-0013, ADR-0048):
    - permissions: design forms, manage actions, view entries, view sensitive data, edit entries, delete entries, export, manage library, manage settings, manage permissions, use privacy tools, as flags with stable names and values;
    - granted per user group globally, per folder or per form, combined as a union within the restriction boundary of ADR-0013; a grant without its dependency, and a global-only permission on a folder or form, are refused;
    - grants and restrictions change through the access service, with the locks of §4.8 (`JironFormBuilder:Access:Grants`, checked at start-up, and `access` in `LockedSections`) and an audit entry in the unit of each change; a user group that Umbraco deletes takes its grants with it;
    - the evaluation says which groups a move gives more access, which the move endpoints of 4.7 refuse without `managePermissions`.
  - **The evaluation** reads a snapshot of the grants and the folder tree, cached on each server and dropped by a cache refresher after every change of a grant, a restriction, a folder or a user group.
  - **The "who may see this form" filter** for trees, search and the inbox: the form list's query applies it in the database, before paging, and the folder list keeps only the folders that lead to something the user may see; the inbox (11.3) applies the same filter.
  - Done when: unit tests cover the evaluation, including restrictions, dependencies, moves and administrators. Integration tests show that the cached evaluation follows each change, that a form list and a folder list as each persona of ADR-0013 hold only what the persona may see (a restricted folder included), and that the start-up check refuses invalid host grants.
- [x] **4.6b Authorization policies and the personas' grants** (4.6 split in two, WORKLOG 2026-10-24) (2026-10-25, 5989315)
  - Authorization handlers for the management API: the Forms section requirement and one policy per permission, singletons (§3.4), evaluated globally or against a form or folder. A user who holds nothing on the resource fails in a way that 4.7 answers with `404`, and one who lacks only the permission in a way that it answers with `403` and the permission's name.
  - The E2E global setup (3.4) grants each test role its Jiron Form Builder permissions, and seeds a restricted folder with its own grant to a separate group (ADR-0013).
  - Done when: integration tests authorize every policy as each persona, signed in through Umbraco, globally and against a form and a folder (the restricted one included); the E2E users have their grants, which a browser test reads from the test host.
- [x] **4.7a The management API base, the library tree and folders** (4.7 split in two, WORKLOG 2026-10-25) (2026-10-25, 9280ca1)
  - **The base:**
    - a base controller, `[MapToApi("jfb")]`, and the auth policies from 4.6;
    - ProblemDetails with stable codes, and a test that each refusal keeps its `code` in the body (Umbraco's error handling and validation answers can drop it, §3.3);
    - the OpenAPI document `jfb`, with a snapshot test that writes `docs/api/jfb.json`;
    - the TypeScript client in the backoffice package, generated from that committed snapshot rather than from a running site, so the client and the demo never wait on each other.
  - **The first endpoints** (§4.7) over the 4.5 services: the library tree (folders and forms as each user sees them, with the user's effective permissions on each item, ADR-0013), folders (get, create, rename, move, trash, restore, delete) and the trash (list and empty). A move that gives a group more access needs `managePermissions` (ADR-0013, ADR-0048).
  - **The permission matrix test:** it enumerates **every** management endpoint by reflection, asserts there is more than zero, asserts each one declares a Jiron Form Builder permission, or the Forms section requirement for what any user of the section may call (the setup checklist, health results, the user's own settings), or is on the reviewed exception list (only the form picker, authorised by content access, ADR-0013; a test fails when the list grows). It calls each as a user who can see the resource but lacks that permission, expecting `403`; for routes on a form, folder or entry, as a user who holds nothing on it, expecting `404` (ADR-0013); and each endpoint with the section requirement as a user without the Forms section, expecting `403`. It fills route parameters by type, because an `int` route given a GUID answers `404` before the permission check. Every later step that adds endpoints is covered by it automatically.
  - **Pending migrations** (ADR-0033): while `IFormBuilderMigrationState` reports the plan as behind, every management endpoint answers `503` with the code `migrations_pending`.
  - Done when: the integration tests for the endpoints and the matrix test pass, and the client is generated and committed. A test shows `503 migrations_pending` from every management endpoint while the plan is behind, and normal answers once it has run.
- [x] **4.7b Forms and search in the management API** (4.7 split in two, then 4.7b in two, WORKLOG 2026-10-25) (2026-10-25, 48599eb)
  - **The endpoints** (§4.7) over the 4.5 services: forms (search, get, create, save draft, publish, unpublish, rename, move, trash, restore, delete). A form's move that gives a group more access needs `managePermissions` (ADR-0013, ADR-0048), and so does a restore that puts a form or a folder somewhere else than it was, such as the root when its folder is in the trash. A save that changes the actions needs `manageActions` (ADR-0013). Each joins 4.7a's permission matrix, which covers it by itself, and its `503 migrations_pending` test.
  - Done when: the integration tests for the endpoints pass; the permission matrix and the `503 migrations_pending` test call every new endpoint (each test names the endpoints it called); the OpenAPI snapshot and the client are regenerated and committed.
- [x] **4.7c Versions in the management API** (4.7b split in two, WORKLOG 2026-10-25) (2026-10-25, 6de9368)
  - **The endpoints** (§4.7) over the 4.5 services: versions (list, get, diff, restore, pin, comment). A restore that changes the actions needs `manageActions` (ADR-0013). Each joins 4.7a's permission matrix, which covers it by itself, and its `503 migrations_pending` test.
  - Done when: the integration tests for the endpoints pass; the permission matrix and the `503 migrations_pending` test call every new endpoint (each test names the endpoints it called); the OpenAPI snapshot and the client are regenerated and committed.
- [x] **4.8a Schema v1 before the freeze** (4.8 split in six, WORKLOG 2026-10-25) (2026-10-25, 744772a)
  - **The review:** three subagents read the Core diff of Phase 4 for SOLID, naming, async and load-balancing safety (no static mutable state) before the split. Their findings are 4.8a to 4.8f, each part fixing its own; 4.8g has the fixes reviewed.
  - **The schema's findings,** fixed while step 1 can still change (§4.5, ADR-0033):
    - **indexes for the access paths:** the references by content item, which every content save replaces (ADR-0017); one form's daily statistics over a range of days, and their delete by form, through a key that leads with the form; the uploads that wait for an entry, oldest first, through one index over the entry and the time;
    - **stored names in one letter case:** every enum stored as text (entry states, version kinds, the states and kinds of action runs and emails, trigger kinds, template kinds, permission targets, storage kinds) in camelCase, as the definitions, events and audit actions are, written and read by one converter;
    - **column names:** the action run's `seq` becomes `sortOrder`, the field catalogue's `type` becomes `fieldType` (as the action run's `actionType`), `replyTo` becomes `replyToAddress`, and the hash columns are named one way;
    - **an email's last error** in two columns, code and message, as an action run's;
    - **a template's changes recorded** in `updatedUtc` and `updatedByUserKey`, beside who saved it and when, as forms and folders record theirs.
  - Done when: the schema tests (`MigrationPlanTests` against `ExpectedSchemaV1`, the constraint tests) and the repository tests pass; SQLite's query plan of each query that a new index serves names that index (as `EntryQueryPlanTests` does); a stored enum in another letter case is refused when read; the SQL Server DDL snapshot and docs/database.md are regenerated and reviewed.
- [x] **4.8b The repositories after the review** (4.8 split in six, WORKLOG 2026-10-25) (2026-10-25, ee97307)
  - **Locks and budgets** (ADR-0036, ADR-0039, ADR-0040):
    - the audit log's filtered list and its count run under the query budget and answer `query_timeout`, so a long search never holds the SQLite gate;
    - the due list of action runs reads without locks, and the claim goes on checking under its lock;
    - a create checks its alias and its key with one `NOT EXISTS` each, one per unique index (forms and the library tables).
  - **Units of work:** a nested unit keeps the outer unit's cancellation and deadline, a write inside a read is refused, and the migration state is read through the gate with its reruns.
  - **Safer statements:** the folder update that no service calls, and that could undo a restriction set meanwhile, goes; a trash delete whose last statement finds nothing rolls its unit back instead of committing.
  - **One home each:** one helper builds the statements' arguments and one holds a row, for every repository; consent texts get a repository of their own; the reruns' waits are immutable; docs/database.md describes the daily counts and the entry numbering as the code does them.
  - Done when: a test for each change (the audit query stopped by the budget, the due list's statement without lock hints, the query plans of the alias and key checks on SQLite, the kept deadline, the refused write, the migration state after a busy read, the rolled-back delete); the repository tests pass.
- [x] **4.8c The services after the review** (4.8 split in six, WORKLOG 2026-10-25) (2026-10-25, 6207439)
  - **A delete that stops part way keeps its audit entry** (ADR-0047): the first unit that deletes anything of a form writes `form.deleting` with it, and the last writes `form.deleted` as now.
  - **Files on a load-balanced site** (ADR-0041): at start-up, a server whose file root is the default local folder logs an error that names `JironFormBuilder:Files:Root` when it is a subscriber or Umbraco lists more than one active server (a lone server is elected the scheduling publisher, so its role alone cannot tell; WORKLOG 2026-10-25).
  - **A restore lands where it was checked** (ADR-0051): Core works out where a restored form or folder lands, the endpoints check that landing, and the write refuses with `409` when it finds another, so a folder put in the trash meanwhile cannot skip the check that a restore gives no group more access.
  - **Imports:** an import that adds more than 200 notes records each note's code once, with its count; a re-imported alias of 64 characters gets its number within 64.
  - **Caches** (ADR-0043): concurrent misses of the access and settings caches share one load, which a change discards.
  - **Smaller fixes:** removing a group's grants runs in a unit of its own; renaming a folder keeps the access snapshot; the docs that contradict the code (where the new-form consent field goes, how a site registers a file store) say what the code does.
  - **Names:** a form's visibility (not its "scope"), the snapshot's invalidator, one spelling of the field catalogue, one record per kind of delete, no `GC.KeepAlive` as a discard; the file root read through its options section; a helper only the tests use moved to the tests; the empty visibility frozen.
  - Done when: a test for each fix (a delete stopped after its first batch leaves `form.deleting`; the start-up error on a load-balanced server, and none on a single server or with a root set; a restore refused when its folder went to the trash meanwhile; an import with 201 option lists; a re-import of a 64-character alias; one load for concurrent misses; the group removal inside a caller's unit); the service and API tests pass.
- [x] **4.8d The form service in parts** (4.8 split in six, WORKLOG 2026-10-25) (2026-10-25, a86aa92)
  - The form service (ADR-0044) holds more than twenty operations over fifteen dependencies and becomes public in 15.1. Split it by job: the lifecycle with the trash, the versions, and the exchange (copies, templates, import and export). Every service of Core shares one change pipeline (a unit of its own, the cancelable notification, the write with its audit entry, the published notification), one check that a call runs in its own unit, and one trash check.
  - Done when: the form, folder, access, settings and management API tests pass unchanged in what they check; each part has its own interface and registration, and the boot test resolves each; architecture.md shows the parts.
- [x] **4.8e The definition model's limits, immutability and checks** (4.8 split in six, then 4.8e in two, WORKLOG 2026-10-25) (2026-10-25, 9f7c201)
  - **Limits:** the export file's reader stops at 100 problems as every other reader does, through one shared limit.
  - **Immutable as ADR-0034 promises:** the reader gives read-only lists, the value types hand out read-only views, the public projection's lists are read-only, and how records compare their lists (by sequence, or by reference as written down) is decided once in an ADR amendment.
  - **Checks:** one-per-form types are counted per type, and the field messages are listed once, for both checks.
  - Done when: a test for each (an export file with thousands of bad option lists answers 100 problems; a read list cannot be changed through a cast; records compare as the ADR says; two one-per-form types in one form; a message added to the record but not to the list fails a test); the shared vectors pass in both runtimes; the definition, schema and logic tests pass.
- [x] **4.8f The definition model's cost and names** (4.8 split in six, then 4.8e in two, WORKLOG 2026-10-25) (2026-10-25, e171aa9)
  - **Cost:** the rules compare numbers as parsed values without building text, the pattern engine reuses its buffers, and the rule reader reads nested groups once; 6.1 gains a ceiling on an answer's length, checked before the rules run.
  - **Names before the model is public,** with the JSON names unchanged: the condition's operator, the options' option list key, the members' access, whether the reference includes the year, and the casing of IP; inside Core, the element kind and the change kind of a comparison, Logic's own item names, the export format's file and `OnePerForm`.
  - **One home each:** the two readers' strict opening steps written once; the publish checks and the hash outside the serializer; write errors reported as what they are; a null culture key refused as documented; static data frozen; the stored serializer built once.
  - Done when: a test for each (a rule compares a number answer of 10,000 digits in under 50 ms); the shared vectors pass in both runtimes; the definition, schema and logic tests pass.
- [x] **4.8g The phase review's fixes: caches, units and the services** (4.8g split in three, WORKLOG 2026-10-25) (2026-10-25, 56e8c5b)
  - **The review** (the first part of 4.8i, run before the split): two subagents read the fixes of 4.8a to 4.8d and those of 4.8e and 4.8f for SOLID, naming, async and load-balancing safety, and a third took the inventory of the wiring sweep. Their findings are 4.8g and 4.8h, each part fixing its own; 4.8i has these fixes reviewed.
  - **Caches** (ADR-0043): a change that comes while a shared load ends never leaves the value that load read in the cache, and the shared load runs outside its first caller's context (the caller's unit, its Umbraco scope, its cancellation), as the ADR promises.
  - **Units** (ADR-0036): a budgeted query stopped through its unit's token answers as cancelled, not with the provider's error, and a nested one keeps its own caller's token as well; the change pipeline itself checks that a change runs in a unit of its own, so no service can forget it.
  - **The schema before the freeze** (ADR-0033): a form's delete finds the uploads that wait for an entry through an index that leads with the form, instead of reading every waiting upload of every form in each batch.
  - **One home each:** the members that the form service's parts share leave the lifecycle part, and the form documents helper is split by job; the refusal of a landing that changed is worded once; every stored name is camelCase, the scan state and the option list kind included; every update of a repository names the user who made it.
  - **Names:** the trash refusals' factories, a form filter's visibility, the deletion candidate, the access snapshot invalidator wherever it is injected, the helper that links a unit's tokens.
  - **Wiring** (the sweep): a booted site registers the start-up check of the file root.
  - Done when: a test for each fix (a change during the end of a load leaves nothing stale; a load started inside a unit runs outside it, and keeps running when that caller stops waiting; a nested budgeted query cancelled through its unit answers `OperationCanceledException` on SQLite; a change outside a unit of its own refused by the pipeline; SQLite's query plan of a form's waiting uploads names the new index; a scan state or an option list kind in another letter case refused; every repository update requires its user; the start-up check among a booted site's handlers); an architecture test fails when a repository parses or writes a stored enum other than through the one converter; the schema tests pass, and the SQL Server DDL snapshot and docs/database.md are regenerated and reviewed; the repository, service and API tests pass.
- [x] **4.8h The phase review's fixes: the writer, the model and the logic** (4.8g split in three, WORKLOG 2026-10-25) (2026-10-25, 58aca50)
  - **The writer** (ADR-0034): the save checks refuse a value outside its enum and a missing required value at their path as `invalid_value` before the writer runs, depth alone decides `limit_exceeded`, and no list of the model takes a null item; the export file's reader tells a document that nests too deep from one that is not JSON, as the definition reader does.
  - **One home each:** each runtime reads the number grammar once (the parser builds on the parts the comparison reads); the comparison's cases live in the shared vectors, not in each runtime's tests; the records' list equality is the list's own; the size refusal is built only for a document that is too large, and bytes the strict encoder made are not checked again.
  - **Cost:** a text condition folds the answer once, not once per operand, in both runtimes.
  - **Static data that cannot change:** `StaticDataTests` allows only types that cannot change, instead of refusing a list of those that can; the static serializer options are read-only; the shape reader's cache of defaults and the export envelope's lists cannot change.
  - **Names:** the comparison's change kind (`ChangeKind`, so that `Kind` never names the element in one layer and the change in the other), the entries of the field message list, the copy of the problem list, and a line in the TypeScript logic on how its names match Core's.
  - **Tests the review found missing:** the TypeScript pattern's stack at the instruction limit with dense splits, which refuses to overflow quietly; a reader error inside a nested group, named at its full path.
  - Done when: a test for each (an undefined rule effect answers `invalid_value` at its path from the save checks, and `Write` refuses it with that message; a null operator and a null option value are refused at their paths; a null page is refused by the list; a definition that nests too deep still answers `limit_exceeded`; an export file that nests too deep answers `limit_exceeded`; the shared vectors hold `-0` against `0`, negatives in order and `12` against `12.0001`; `StaticDataTests` fails on a concurrent dictionary and on serializer options that can change; a text condition against 20 operands folds the answer once); the shared vectors pass in both runtimes; the definition, schema and logic tests pass.
- [x] **4.8i Phase review** (4.8 split in six, then 4.8e in two, then 4.8g in three, WORKLOG 2026-10-25) (2026-10-25, 4921ba5)
  - A subagent reviews the fixes of 4.8a to 4.8f for SOLID, naming, async and load-balancing safety (done before the split: its findings are 4.8g and 4.8h), then the fixes of 4.8g and 4.8h the same way. Fix what it finds.
  - Check the coverage report (≥ 85% on Core).
  - Tag the end of the phase as `baseline/schema-v1` and push the tag. Step 16.5 packs this tag as the "old version" for the upgrade test.
  - **The schema is frozen from here:** `CreateSchemaV1` is never edited again, and every change is a new migration step. A test pins the hash of the v1 snapshot DTOs.
  - The wiring sweep names what this phase built whose reader or caller comes later: `general.timeZone` (4.4b; read in 6.1, ADR-0043), `Designer:SettingLocks` (4.4a; applied in 5.1 and 8.1), the sections that `LockedSections` may lock before they exist (ADR-0042), the settings and access changes whose endpoints come in 12.4a, the exchange service (9.2), the permission policies whose endpoints come later, and the restricted and clamped host rules (Phases 7 and 13).
  - Write the handover note.
  - Done when: pushed, with the phase-review gate.

### Phase 5: Field types, option lists and the logic engines

- [x] **5.1a The field type registry and the traits** (5.1 split in four, WORKLOG 2026-10-25) (2026-10-25, b860e77)
  - **The registry:** a collection builder with `FormBuilderFieldTypeBase<TSettings>` (`builder.JironFormBuilder().FieldTypes()`, ADR-0055) that holds the built-in types of §4.3.2 from its start, so that a site adds, replaces or removes a type whatever order the composers run in. Each type declares its alias, icon, palette group, storage kind (the one the field catalogue records from 4.5a on, ADR-0044), how rules compare its answers, and which common properties and error kinds it has. Start-up refuses two types with one alias, naming both, and a declaration that contradicts itself.
  - **Settings schemas from attributes:** the settings class of each built-in type (ADR-0002's table) with `[FormBuilderSetting]` (label, description, editor, order, group, merge tags, and whether visitors see the setting), read into the type's settings schema with each setting's kind and default.
  - **Publishing** (ADR-0035): the structural validation, the public projection and the field catalogue read each type's traits from the registry instead of 4.2b's built-in table, and a type's public settings come from its schema.
  - Done when: unit tests pass, including a custom type registered by a test composer and a type removed from the collection (a field of the removed type is `unknown_type` at publish); 4.2b's support matrix runs against the registry's traits and restates the rest of 4.2b's table (storage kinds, rule kinds, public settings, one per form, server-only defaults), item by item; start-up refuses a second type with an alias already taken and a declaration that contradicts itself, each with a message that names the type.
- [x] **5.1b Settings checks** (5.1 split in four, then 5.1b in two, WORKLOG 2026-10-25) (2026-10-25, edfafee)
  - **Each type's check of its own settings** (ADR-0035): publishing reads a field's settings as its type's settings class, applies the rules its attributes give (ranges, lengths, allowed values, required settings) and then the type's own check (for example a minimum above the maximum), each problem at its path under `…settings`.
  - **The rules in the schema:** each setting's rules are part of its settings schema, so 5.1c serves them to the designer, which checks them as the server does.
  - Done when: unit tests cover each attribute rule and the own check of each built-in type that has one, at its path; a publish through the form service refuses a field whose settings break a rule, at its path, and publishes it once the settings are put right.
- [x] **5.1c The field types endpoint and availability** (5.1b split in two, WORKLOG 2026-10-25) (2026-10-25, 62b6deb)
  - **Settings schemas exposed to the backoffice as JSON:** the field types of §4.7's designer support, each with its palette group, icon, the common properties it supports and its settings schema, with the permission matrix, the regenerated client and English and German keys for the built-in types' names and settings.
  - **An availability check** ("not configured: …"): a type that needs configuration the site lacks says why, and the backoffice receives the reason, so the palette can grey the type out (§4.3.2).
  - Done when: the endpoint's integration tests pass, with the permission matrix; a type that is not configured reaches the backoffice with its reason; the client is regenerated, and every new string has an English and a German key.
- [x] **5.1d Field values** (5.1 split in four, then 5.1d in two, WORKLOG 2026-10-25; 5.1c until 5.1b's split) (2026-10-25, 764e76a)
  - **Field values:**
    - mapping from storage kinds to `jfbEntryValue` columns, for the storage kind each type declares (ADR-0044);
    - the parse and validate contract;
    - the longest answer the type takes, which the submission pipeline checks before the rules run (6.1, ADR-0030; added in 4.8f).
  - Done when: unit tests cover every storage kind's columns, the contract through a custom type and a built-in one, and the longest answer of every built-in type.
- [x] **5.1e Changing types** (5.1d split in two, WORKLOG 2026-10-25) (2026-10-25, c39611f)
  - **Changing types:** a "change type" compatibility map.
  - Done when: unit tests cover the compatibility map with what each change loses.
- [x] **5.1f Designer locks** (5.1 split in four, WORKLOG 2026-10-25; 5.1d until 5.1b's split, 5.1e until 5.1d's) (2026-10-26, fa64243)
  - **Designer locks** (`Designer:SettingLocks`, ADR-0042): start-up refuses a lock that names a field type or setting the site lacks, or a value the setting refuses; the settings schema the backoffice reads marks a locked setting read-only or hidden; the form service refuses a save that gives it another value (`409 setting_locked`); the runtime uses the locked value.
  - Done when: a designer lock of a field setting refuses a save with another value and wins at run time; start-up refuses a lock on a type or a setting the site lacks and one whose value the setting refuses, each naming the key; the schema the backoffice reads marks a locked setting read-only, and a hidden one hidden.
- [x] **5.2a The rules engine** (C# and `packages/logic`; 5.2 split in two, WORKLOG 2026-10-26) (2026-10-26, 6bd2b73)
  - Implement the rules of §4.3.3 over a whole form in both runtimes: the effects on pages, groups and fields (a repeatable group's child fields row by row), actions and outcomes, with form-level validation rules and the "hidden means gone" propagation (a field hidden because its group or page is hidden). Groups nest only to a limited depth, checked before evaluation, because both evaluators recurse (ADR-0030); publishing refuses a deeper group through the same check.
  - **Vectors (≥ 300 cases),** starting from the 163 of 1.5 (ADR-0005):
    - every operator against every storage kind;
    - empty and unchecked values, and multi-values;
    - relative dates with an injected "today" in two time zones;
    - nested groups and hidden propagation;
    - the `require` effect, conditions that compare two answers, context conditions (culture, member, query value, today) and switched-off rules.
  - **The mutation check** is a script in the repository, so later reviews run it again, and it also covers what the port added: the instruction count, `own()`, the instant parser and the time zone check (ADR-0030).
  - Done when: both runtimes pass every vector, and a scripted mutation check flips each TypeScript operator in turn and shows at least one failing vector for each. The mutation check also covers the text, decimal, date and pattern modules in TypeScript and the parts where .NET differs in C# (the Turkish i's, noncharacters, the folded blocks); every survivor is either written down as an equivalent change or killed by a new vector. The tables check passes.
- [x] **5.2b The checks' contract and the text, number and date types** (C# and `packages/logic`; 5.2 split in two, then 5.2b in two, WORKLOG 2026-10-26) (2026-10-26, a64434f)
  - **The contract** (ADR-0062): a type's check sees whether the field is required (by its own setting or a rule) and today in the site's time zone; the public projection gives every public setting its effective value, so the browser checks what the server checks; an email's confirmation has a message kind of its own, `confirmation`; `checkField` in `packages/logic` runs the built-in types' checks over the same answers.
  - **The built-in types' checks** (added in 5.1d, ADR-0058) of `text`, `textarea`, `email` (its confirmation in both runtimes, its domains on the server), `url`, `number`, `slider`, `rating`, `scale`, `date`, `time` and `datetime`: lengths and words, ranges and steps, date windows and an email's confirmation, each with its error kind, in both runtimes (ADR-0058).
  - **Vectors:** `validation.json` holds every check of these types at its edges, and both runners run it.
  - Done when: both runtimes pass every validation vector, and 5.2a's mutation check covers the checks in TypeScript, with every survivor written down as an equivalent change or killed by a new vector. A unit test per built-in type of this step shows each check of its settings through its `Validate`, at its edge, and one shows that the types without checks of their own find nothing (ADR-0058); unit tests show the projection's effective settings and an email domain the field refuses on the server (ADR-0062).
- [x] **5.2c The choice, people and file types' checks** (C# and `packages/logic`; 5.2b split in two, WORKLOG 2026-10-26) (2026-10-26, e24e0a0)
  - **The built-in types' checks** (added in 5.1d, ADR-0058, ADR-0062) of `dropdown`, `radio`, `checkboxes`, `grid`, `ranking`, `country`, `name`, `address` and `file`: selections and the options a choice offers, the required parts of a name and an address as ADR-0056 reads them, and file rules (the files' names and sizes from the pipeline), each with its error kind, in both runtimes (ADR-0058), together with the checks of a field's `validation` list that need no other entry (`pattern`, `matchesField`).
  - **Vectors:** `validation.json` holds every check of these types and both of the field's checks at their edges, and both runners run it.
  - Done when: both runtimes pass every validation vector, and 5.2a's mutation check covers the checks in TypeScript, with every survivor written down as an equivalent change or killed by a new vector. A unit test per built-in type of this step shows each check of its settings through its `Validate`, at its edge (ADR-0058).
- [x] **5.3a Calculations** (C# and `packages/logic`; 5.3 split in three, WORKLOG 2026-10-26) (2026-10-26, d597469)
  - Implement the calculation parser and evaluator of §4.3.3, with no `eval`, no reflection and documented rounding (the exact decimals of ADR-0005: division to 20 places and `round` half away from zero, `out_of_range` past 60 digits), the date functions `daysBetween` and `yearsBetween` (leap days and a daylight-saving change in the vectors), and a limit on how deeply calculations nest (ADR-0030).
  - **In the form engine** (ADR-0061): each calculation is worked out in dependency order over the answers as the rules see them (hidden fields empty), its result is rounded to its `format.decimals`, and rules read the rounded value (ADR-0005).
  - **Publishing** refuses a calculation that names no field or depends on itself (ADR-0035), and a formula that both runtimes cannot read, at its path.
  - **Vectors:** `calculations.json`, run by both runners.
  - Done when:
    - the vectors pass in both runtimes, including the date functions, and division and rounding at their edges (thirds, halves, negative halves, division by zero, out of range), and a calculation result rounded to its decimals and read rounded by a rule (ADR-0005);
    - a fuzz test of 10,000 random formulas in each runtime never throws an unhandled exception and never takes longer than 50 ms per input;
    - publish tests refuse a calculation that names no field, one that depends on itself (directly, through another calculation and through a rule) and a formula that does not parse, each at its path;
    - 5.2a's mutation check covers the calculations in TypeScript, with every survivor written down as an equivalent change or killed by a new vector.
- [x] **5.3b The merge-tag language** (C# and `packages/logic`; 5.3 split in three, WORKLOG 2026-10-26) (2026-10-26, ddbf6f9)
  - Implement the grammar, the filters and the escaping contexts of §4.3.3 (ADR-0014), plus unknown-tag handling (unknown tags render empty with a warning, malformed ones render as their text with an error, nothing throws), the limits, lists, and formatting with the culture patterns the server writes for dates, numbers and currencies.
  - **Vectors:** `merge-tags.json` (the `template` case), run by both runners, over sources given as data.
  - Done when:
    - the vectors pass in both runtimes: the grammar, every filter, every context's encoder, explicit filters in each context, lists, the culture patterns, the limits and every error path;
    - a fuzz test of 10,000 random templates in each runtime never throws an unhandled exception and never takes longer than 50 ms per input;
    - an injection test shows that merge tags cannot produce HTML, URL or JSON breakouts;
    - 5.2a's mutation check covers the template module in TypeScript, with every survivor written down as an equivalent change or killed by a new vector.
- [x] **5.3c The merge-tag service and its extension points** (C#; 5.3 split in three, then 5.3c in four, WORKLOG 2026-10-26) (2026-10-26, 66eed38)
  - **The service** `IFormBuilderMergeTagService` (§4.9): renders a template for a place over a scope (the form, the culture, the site, the entry, the query and context values), and checks one without values, with the problems of ADR-0064 and those of the place.
  - **Which sources each place may use** (ADR-0014): the places of ADR-0014's table, each with its contexts and sources; per-visitor sources only where the page is not cached; a site's own filters only where only the server renders.
  - **`IFormBuilderMergeTagSource` and `IFormBuilderMergeTagFilter`** (§4.9), registered through `builder.JironFormBuilder()`: a source declares its name, its paths for the picker, where it may be used and whether its values are per visitor; a filter its name and its arguments. Start-up refuses a name that is taken or outside the grammar; a source or filter that fails renders empty and is logged without values.
  - **The sources that need neither answers nor content:** `form`, `entry` (`number`, `reference` in the form's format, `date`, `state`), `query` (only the names the form allows), `context` and `date`.
  - **The culture patterns from .NET's culture data** (ADR-0064): the server writes each culture's formats from .NET, with the date and time separators resolved, over the Gregorian calendar.
  - Done when:
    - unit tests show the `form`, `entry`, `query`, `context` and `date` sources, `entry.reference` in its formats, and a custom source and filter added through the interfaces (one that throws renders empty and is logged);
    - unit tests show the sources and filters each place may use, with a refused one for each place that limits them;
    - start-up refuses a source or a filter whose name is taken or outside the grammar, naming it;
    - a unit test formats dates, times, numbers and amounts with the formats written from .NET's data for every culture .NET knows and prints what .NET prints for the same patterns, listing exactly any culture whose data falls outside the pattern subset.
- [x] **5.3d Answers as merge tags** (C#; 5.3c split in four, WORKLOG 2026-10-26) (2026-10-26, 68fd8df)
  - **The `field` source** (ADR-0014): `field.<alias>` (the answer as displayed), `.value` and `.label`, a repeatable group's child as a list across its rows, with sensitive fields, hidden fields and former aliases as ADR-0014 decides; each field type gives its answers' display text (option labels in the culture, numbers and dates in the culture's formats, file names, the parts of a name or an address).
  - **`entry.answers`:** every visible answered field, a table in HTML and `Label: answer` lines elsewhere, leaving out what ADR-0014 leaves out in each place.
  - Done when: unit tests show the `field` source for every built-in type (its display text, `.value` and `.label`, a repeatable group's child as a list), a former alias resolving to its field, a hidden field empty, `entry.answers` in HTML and in text, and a sensitive field masked and left out as ADR-0014 decides, in each place.
- [x] **5.3e The Umbraco sources** (C#; 5.3c split in four, WORKLOG 2026-10-26) (2026-10-26, 0ba5067)
  - **`page`, `member`, `site`, `dictionary` and `entry.url`** (ADR-0014): the content page and its ancestors' properties in the culture, the member and its properties, the site's name and base URL, dictionary items with their fallback, and the entry's link in the backoffice.
  - **The base URL** (§4.8, ADR-0008): a per-site general setting, by default the site's first `https` domain in Umbraco, otherwise Umbraco's application URL, never the request's Host header.
  - **Dictionary texts in the answers** (added in 5.3d, ADR-0066): a field's label, an option's label and a grid row's statement that name a dictionary item (`#key`, ADR-0002) show the item's text in the culture in `field.*` and `entry.answers`.
  - Done when: unit tests show each of these sources (a page's name, URL, property and an ancestor's property; a member's name, email and property, and none without a member; the site's name and base URL with each default; a dictionary item in the culture and its fallback; the entry's link from the base URL), and a field's label and an option's label from the dictionary in `field.*` and `entry.answers`.
- [x] **5.3f Answer recall** (C# and `packages/logic`; 5.3c split in four, and the copied actions moved to 5.3g, WORKLOG 2026-10-26) (2026-10-26, 45ec725)
  - **Answer recall** in both runtimes: only fields above the text or on earlier pages, hidden fields empty, with the answers' display text of 5.3d in TypeScript too.
  - Done when: the vectors pass in both runtimes, including answer recall (a field above, one on an earlier page, a hidden one, and a later one refused).
- [x] **5.3g Copied actions** (C#; 5.3f split in two, WORKLOG 2026-10-26) (2026-10-26, aac2993)
  - **Copied actions** (added in 4.5c, ADR-0046): the report of copying actions to another form also lists each merge tag in a copied action that names a field the target lacks, with its path.
  - Done when: a test copies an action whose subject names a field the target lacks and finds that tag, with its path, in the copy's report.
- [x] **5.4a The checks from Text to Country** (C# and `packages/logic`; 5.4 split in three, WORKLOG 2026-10-26) (2026-10-26, 9e98db7)
  - **Parsing and validation** of the types of §4.3.2 from Text to Country (ADR-0069): an email address, a phone number in a lenient international format and a web address held to their forms in both runtimes, and country names from .NET culture data (built in 5.3d, ADR-0066).
  - **Grid and ranking** (added in 1.1): the required-rows rule and unique positions, in both runtimes through `validation.json` (added in 5.2b, ADR-0062).
  - **Smaller settings:** word limits, excluded dates, the camera option on file fields, and place limits on options (the counting itself comes with the pipeline in 6.1).
  - **Numbers** accept at most 15 significant digits, so the stored double is exact (ADR-0003): at most 15 digits in all, leading zeros not counted, and at most 15 after the point (ADR-0005), with a message kind of their own, `digits` (ADR-0069). The value of a no-JS `<input type="number">`, which may use an exponent, is converted exactly to the wire form before the limits apply.
  - Done when:
    - both runtimes pass every validation vector, including a grid with a missing required row and a ranking with two options in one position, and 5.2a's mutation check covers the new checks in TypeScript, with every survivor written down as an equivalent change or killed by a new vector;
    - unit tests per type of this step cover valid, invalid and edge values: the forms of an email address, a phone number and a web address, the smaller settings, and country names in English and German;
    - a unit test refuses a 16-digit number with its own message, and another a number with 16 places; a unit test converts a no-JS number with an exponent exactly to the wire form before the limits apply.
- [x] **5.4b Storing answers** (C#; 5.4 split in three, WORKLOG 2026-10-26) (2026-10-27, 4a64c8c)
  - **Storage** of the types of §4.3.2 (ADR-0058): each value a type read in its storage kind's column, the items of a multi-value answer by `ValueIndex`, "Other" options' texts among them, a repeatable group's rows by `RowIndex`, and a file field's upload tokens linked to the uploads of that form and field that no other entry took (ADR-0006).
    - **Grid and ranking** (added in 1.1): each grid row has its own key and is stored like a child field (`ParentFieldKey` = the grid, §4.5), so it filters, sorts and exports like any field; a ranking stores one value per position (`ValueIndex`).
    - **Consent text hashing into `jfbConsentText`** (ADR-0009): the wording a consent field shows in a culture, with only the merge tags that are the same for every visitor (ADR-0014), hashed as the form posts it and stored once with the entry, so a consent value reads back with the exact wording the visitor agreed to.
  - **Reading back:** an entry's stored values read back typed, field by field: a number as a number, a date as a date, a yes or no, a file, the parts of a name or an address, a grid's rows, a ranking in its order and a repeatable group's rows.
  - Done when: unit tests per type cover the values each answer is stored as, at their edges; an integration test stores every type and reads it back typed, including a grid's rows, a ranking's positions, a repeatable group's rows, an "Other" text, a file and a consent with its wording.
- [x] **5.4c The advanced types' own checks** (C#; 5.4 split in three, WORKLOG 2026-10-26; then 5.4c in two, WORKLOG 2026-10-27) (2026-10-27, 6d83c77)
  - **Files and signatures** (ADR-0071): file (its upload tokens linked in 5.4b, its checks built in 5.2c) and signature (PNG checks on size, dimensions and magic number), through a field type's check of an upload, which 6.1's `uploads` endpoint runs on a file's first bytes before it stores the file (ADR-0006): the most bytes a field's file may have, the file field's extensions, and the signature's PNG header, its size in pixels and its size in bytes.
  - **Composite:** name and address (their parts read and checked in 5.2c, stored in 5.4b), and repeater (its rows held to their limits, with a message kind of their own, `rowCount`).
  - **The types without answers of their own:** the calculation, whose value the server works out (5.4d), the content types and the captcha placement ignore what is posted for them.
  - Done when: unit tests per type cover valid, invalid and edge values, including a signature that is no PNG, one too large and one too big in pixels, a repeatable group with too few and too many rows, and the calculation, the content types and the captcha placement ignoring what is posted for them.
- [x] **5.4d A submission's answers** (C#; 5.4c split in two, WORKLOG 2026-10-27) (2026-10-27, 66cbd4c)
  - **The whole form at once** (ADR-0061, ADR-0071): the step between a post's answers and the entry's answers 5.4b stores, which 6.1's pipeline runs: every field read by its type, the rules over what was read, then each visible field's checks; a repeatable group's rows counted as the runtime shows them and held to its limits, its child fields read and checked row by row, each error in its row; the errors by field and row.
  - **Computed and hidden:** calculation (server recompute: the value the form engine works out is the one stored, whatever was posted), hidden (server-only sources resolved on submit, never rendered).
  - Done when: unit tests show a repeatable group's rows counted, with too few and too many rows, and a child field's error in its row; a calculation's posted value replaced by the server's; and a hidden field whose member value is resolved on submit and never appears in the public projection; the rendered markup's check of that hidden field is 6.2's.
- [x] **5.5a Option lists, sources and the fixed kinds** (C#; 5.5 split in five, WORKLOG 2026-10-27) (2026-10-27, 71b919e)
  - **The model:** a list's kind as a closed set, stored as its camelCase word (ADR-0033); each kind's configuration and items read and checked, each problem at its path, for a library list and for a source configured on a field (publishing checks a field's source configuration through its kind, ADR-0002).
  - **Resolving options** (§4.9): a field's options, or a library list's, in a culture: per-culture labels with their fallback, the current page, and cascading parents (the parent's answers filter the options, by an option's `parentValues` or by the source itself); a list is kept until it changes, on every server.
  - **Kinds:**
    - manual;
    - countries;
    - code (`IFormBuilderOptionSource`, registered through `builder.JironFormBuilder()`), with optional parent filtering;
    - **presets** (added in 1.1): months, weekdays, agreement and satisfaction scales, and "yes, no, not sure", in English and German (14.2 adds the other five), with month and day names from culture data.
  - Done when: unit tests per kind of this step pass: a manual list's labels per culture with their fallback, the countries in English and German, each preset in English and German, a code source with parent filtering, and one that throws, which gives no options and is logged without values; publishing refuses a field's source configuration that its kind refuses, at its path; start-up refuses a code source whose alias is taken or outside the grammar, naming it; a changed list's options show at once.
- [x] **5.5b Content and data type lists** (C#; 5.5 split in five, WORKLOG 2026-10-27) (2026-10-27, 6037f87)
  - **Content:** the root from a picker or "the current page", a document type filter, the value and label fields, children or all descendants, the order, published pages only, and pages that need a member to sign in left out.
  - **Data type:** the values of a dropdown, checkbox list or radio button list data type, in its order.
  - **Caching with invalidation on content publish** (and when a data type is saved), on every server.
  - Done when: integration tests over a site's content pass for each content setting (a picked root and the current page, the document type filter, the value and label fields, descendants, the order), leave out an unpublished page and a member-protected one, read a data type's values, and show a published change and a saved data type's change at once (cache invalidation on publish).
- [x] **5.5c URL and file lists** (C#; 5.5 split in five, WORKLOG 2026-10-27) (2026-10-27, 263fa38)
  - **URL JSON** (§4.10): `https` only (`http` only when the host allows it), hosts only from `JironFormBuilder:Webhooks:AllowedHosts`, private, loopback and link-local addresses refused after DNS resolution (SSRF), no redirects to other hosts, timeouts and response caps; a JSON path mapping to the list and to each option's label and value; kept for the list's `CacheSeconds`, with the last copy in use while the address does not answer. `IFormBuilderHttpTransport` lets only the `Testing` assembly reach the stub on `127.0.0.1:5560`.
  - **File:** a CSV or TXT upload, `value|label`, read into the list's items.
  - Done when: tests per kind of this step pass: the URL kind (the mapping at its edges, the keep time, the last copy while the address fails) and the file kind (both formats, quotes, a byte order mark, empty and repeated lines, too many lines, a value too long), including SSRF refusal for URL lists: a host not allowed, `http`, a name that resolves to a loopback, private or link-local address, a redirect to another host, an answer over the cap and one too slow; a test proves that production refuses loopback.
- [x] **5.5d Answers and labels from resolved options** (C# and `packages/logic`; 5.5 split in five, WORKLOG 2026-10-27) (2026-10-27, 0b24d40)
  - **Answers against resolved options** (added in 5.2b, ADR-0062): a choice whose options come from a list or another source holds its answers to the options the source resolves, and a cascading option to the parent answers it belongs to, in both runtimes; 5.4d's reader resolves the options of a submission.
  - **Merge tags show resolved labels** (added in 5.3d, ADR-0066): `field.*` and `entry.answers` show the label the source resolves for a chosen value, in the culture, as they show a field's own options.
  - Done when: a choice with options from a list refuses an answer the list lacks, and a cascading option whose parent answer it does not belong to, in both runtimes (ADR-0062), through the validation vectors, and 5.2a's mutation check covers the new checks in TypeScript, with every survivor written down as an equivalent change or killed by a new vector; a merge tag of a choice with options from a list shows the option's label in the entry's culture (ADR-0066).
- [x] **5.5e Lists from an import** (C#; 5.5 split in five, WORKLOG 2026-10-27; then 5.5e in two, WORKLOG 2026-10-27) (2026-10-27, 9f0e78e)
  - **Lists from an import** (added in 4.5c, ADR-0046): an import creates the lists its envelope carries and the site lacks, through the kind's own checks (allowed hosts, file contents), and only for a user who may manage the library; for anyone else, and for a list the checks refuse, it keeps the key and reports `option_list_missing`.
  - Done when:
    - an import test creates a carried manual list for a library manager, reports it for a user who may not manage the library, and reports a carried URL list whose host is not allowed without creating it;
    - pushed.
- [x] **5.5f The review's fixes to a submission's answers** (C#; 5.5e split in two, WORKLOG 2026-10-27; then 5.5f in four, WORKLOG 2026-10-27) (2026-10-27, a2fef5a)
  - **Core coverage** measured with instrumentation that rewrites no file: Smart App Control on this machine refuses the assemblies coverlet rewrites, so the tool changes, in an ADR, and the 85% floor stays.
  - **What the phase review found in reading a submission** (ADR-0080):
    - an upload token that two rows of a repeatable group post stands for one upload only, so the later row's answer is invalid (the entry's writer refused it with an exception);
    - a ranking posted without JavaScript names its options in its parts: the names are cleaned as answers are, a name longer than an option value is refused before it is read, and two names that clean to one are read as one;
    - a hidden field prefilled from the query with more than it takes counts as not prefilled, with its alias logged, and falls back to its default;
    - a checkbox's `false` is read as unticked, as ADR-0058 says, instead of refused as too long;
    - a cascading choice compares its options' parent values with the parent's answers in a set, not a list.
  - Done when: a unit test per finding shows it (two rows posting one token, the later one invalid; a ranking with an unclean name, an overlong one and two names that clean to one; a query prefill over the limit falling back to the default, with the alias logged; a posted `false` unticked; 10,000 cascading options against 10,000 parent answers held in under a second), and Core coverage measured with the new tool is at least 85%.
- [x] **5.5g The review's fixes to option lists and calls to other hosts** (C#; 5.5f split in four, WORKLOG 2026-10-27) (2026-10-28, 52b8c7d)
  - **What visitors give a source** (ADR-0081): a cascading field's parent answers are held to the options the parent offers before they reach a source or a cache key, so no posted text becomes either; with the parent's options unknown, the child's are unknown too. The options service keeps a library list's options per parent answer only for none or one, and drops parent values that no option could have. Step 6.1's `options` endpoint does the same with its `parent` and `culture`.
  - **Failures** (ADR-0081): a stored list of a kind this version does not know reads as unreadable, logged by its key, instead of throwing; a URL address that fails with no copy kept is asked again only after `RetryAfter`; a body that breaks off or does not decompress is a failed call with its reason.
  - **Ports** (ADR-0082): a call to an allowed host goes only to its scheme's standard port, unless the host's entry in `JironFormBuilder:Webhooks:AllowedHosts` names a port (`host:port`), redirects included.
  - **Smaller:** the option list cache refresher's payload read back from JSON as servers carry it; a form's create telling its alias taken by the exception's type, so an attempt of the unit that the database runs again reads nothing of the attempt before (ADR-0081).
  - Done when: a test per finding shows it: a parent answer the parent lacks reaching neither the source nor a cache key, and a parent whose options are unknown leaving the child's unknown; two parent answers asked twice calling the source twice; a stored row of an unknown kind read as unreadable and logged; a failing address asked twice within a minute calling once, and again after it; a body that breaks off and one that does not decompress; an allowed host on another port refused, allowed with the port named, and a redirect to another port refused; the start-up check of a `host:port` entry; the refresher's payload through JSON; an import whose unit the database retries.
- [x] **5.5h The review's fixes to merge tags and the two runtimes** (C# and `packages/logic`; 5.5f split in four, WORKLOG 2026-10-27) (2026-10-28, 20638e8)
  - **What the phase review found:** `{{ field }}` without an alias in a visitor text threw instead of rendering empty with a warning; a site source's traits (`IsPerVisitor`, `Places`) are read once at start-up, where a failing one stops the start with its name; the display reader of a name's, an address's and a ranking's parts in TypeScript reads what C#'s reads (a part named twice, names that look like numbers, lone surrogates), through vectors; the option labels of answers are resolved once per field and display, and a submission's hidden fields in one scope; the culture data every server of a site needs is the same (docs).
  - **Wiring found by the sweep:** a site composer's `builder.JironFormBuilder().OptionSources()` (add, replace, remove, and a taken name refused at start-up) on a booted site.
  - Done when: a test per finding shows it (`{{ field }}` in a visitor text rendering empty with a warning and checked with a problem, in both runtimes; a site source whose traits throw stopping the start with its name; the parts vectors passing in both runtimes; one label resolution per field across a display's tags; one scope per submission's hidden fields), 5.2a's mutation check covers the changed TypeScript, with every survivor written down as an equivalent change or killed by a new vector, and the booted-site test of `OptionSources()` passes.
- [x] **5.5i The second review's fixes** (C# and `packages/logic`; 5.5i split in two, WORKLOG 2026-10-28) (2026-10-28, 16c2ec9)
  - **What the second review of 5.5f to 5.5h found:**
    - **a submission** (ADR-0080): an upload is claimed by its token's text rather than by the upload, so two tokens for one upload both reach the entry's writer, which refuses the entry with an exception; an upload an entry already holds is not refused before the writer either; a hidden field's query value that fails any of its type's limits, not only one too long, counts as none and is logged as too long; the answer limits' public docs do not say that part names count;
    - **option lists** (ADR-0081, ADR-0082): a merge tag's option labels are resolved for the scope's parent answers as they stand, so a hidden field's default, rendered before the answers are checked, lets a posted parent answer reach a source and become a cache key; a body the real transport cannot decompress (Brotli, zlib) is not `AnswerUnreadable`; ADR-0082's options say a port entry also keeps the standard port, which its decision does not;
    - **merge tags** (ADR-0083): the labels kept for a scope go stale when a site's type that keeps its value on the server and has options is a parent and resolves after them, since "a resolved value is never a parent's" holds for the built-in types only; the start-up check and the registry each read a site source's traits; a raw lone surrogate in a stored value with parts makes Core's reader throw, where the website's gives no parts; the public answers that change between a submission's hidden renderings say nothing of it;
    - **tests missing:** a token shared by two fields; a too-long query value in a repeatable group's row; the longest text counting a part's name and not that of a part without texts; a cascading parent in a repeatable group; two renderings reading one scope's labels at once; a vector with a carriage return between a value's members and one with an escaped backslash before a closing quote.
  - Done when: a test per finding shows it, failing on the code before (two tokens for one upload, in two rows and in one answer, `invalid` at the later value; an upload an entry holds `invalid`; a hidden field with two query values taking its default, logged as a value its type cannot take; a hidden default's label tag sending a source only parent answers the parent offers; a parent of a site's type resolved between two defaults giving the second the labels for its value; a corrupt Brotli and a corrupt zlib body `AnswerUnreadable` through the real transport; a site source's traits read once across the start-up check and the registry; a raw lone surrogate giving no parts in both runtimes), the missing tests are written, and 5.2a's mutation check covers the changed TypeScript.
- [x] **5.5j The third review's fixes** (C#; 5.5j split in two, WORKLOG 2026-10-28) (2026-10-28, de7c4eb)
  - **What the third review of 5.5i found:**
    - **merge tags** (ADR-0081, ADR-0083): a child whose parent is a sibling in its repeatable group has its options asked for every row's parent answers together, where the submission's check asks row by row, so two rows whose parents' sources share an option value show one row's label in both, and the request skips the lists' cache; a parent in a hidden repeatable group still gives its answers; the docs say a rendering asks a source at most once for each field it shows, that labels are read only for a tag that shows the field, and that a source keeps nothing past a rendering, which the label path itself does not;
    - **the registry** (ADR-0083): a source collection or a filter collection the container cannot create leaves the other out too; ADR-0083 still says the registry and the start-up check each read the traits, and the problems' docs that each names its type;
    - **a submission** (ADR-0080): a site's type that keeps its value on the server and cannot read a query value within its limits is `invalid` at a field the visitor cannot see; the Brotli catch also takes an `ObjectDisposedException`; ADR-0080 says an answer takes its uploads when its values stand, which means only that its tokens do, and that 5.5i first held a query value to every limit, where only the log changed; two doc comments name too few refusals;
    - **tests missing:** an answer that fails after one of its tokens stood claims nothing; the usable side of the sources' and filters' shared names; a parent from a source, one whose options are unknown, a hidden one and a chain of parents leading back in merge tags; a parent in a row in a hidden default; the start-up check reading the traits before the registry is made; the concurrent renderings' test on the real clock.
  - Done when: a test per finding shows it, failing on the code before (two rows whose parents' sources share a value each showing its own label, with the source asked once for each row's answer; a parent in a hidden group giving none; one collection the container cannot create leaving the other's sources or filters usable; a site's server-side type's unreadable query value taking its default), the missing tests are written, the docs say what the code does, and the mutation check covers the changed upload claim and the rows' labels.
- [x] **5.5k Phase review** (5.5e split in two, WORKLOG 2026-10-27; then 5.5f in four, WORKLOG 2026-10-27; then 5.5i in two, WORKLOG 2026-10-28; then 5.5j in two, WORKLOG 2026-10-28) (2026-10-28, 3cb9ccb)
  - **Phase review:** review the phase's code (the field types and their checks, the rules, calculations and merge tags in both runtimes, a submission's answers and the option lists), fix what it finds (5.5f to 5.5j), and write the handover note. A second review reads the fixes of 5.5f to 5.5h again (its findings are 5.5i), a third reads 5.5i's fixes (its findings are 5.5j), and a fourth reads 5.5j's fixes.
  - **The wiring sweep's later work:** the steps that build the option lists' screens (9.4, 12.1) get what they lack: the endpoint that serves the option kinds' settings schemas and the code sources' names with the permission matrix, English and German texts for the `#jfbOption*` keys with a check that every key has both, and the source picker.
  - Done when: pushed, with the phase-review gate.

### Phase 6: Rendering, the website runtime and headless

- [x] **6.1a The public API's frame and the form endpoint** (6.1 split in five, WORKLOG 2026-10-28) (2026-10-28, 483ea64)
  - Set up §4.6 at a configurable base path, with CORS, ProblemDetails codes, request limits and the OpenAPI document `jfb-public` (with a snapshot test). Every endpoint declares its rate-limit policy, body limit and time limit, and a test reads the OpenAPI document and fails when one is missing (§4.6); the time limits answer `504 timeout` from here on, the policies are enforced from 7.1, and the body limits refuse a declared length above them, and any body for an endpoint that takes none, with `413 payload_too_large` from here on, while counting a body without a declared length as it is read comes with the first endpoints that take one (6.1d, 6.1e), all per ADR-0012 (amended in ADR-0084).
  - **Endpoints built here:** the form definition. Of the others of §4.6, 6.1c builds `options`, 6.1d `uploads`, 6.1e `entries`, and 6.1f `validate` and `receipt`. The rest are built and tested by the steps that need them: `render` (6.2l), `challenge` (7.1), `drafts` and `resume` (8.6), `verify` (8.5), `events` (11.5).
  - **Published forms** (ADR-0044): the form endpoint reads a form's published version through the form service, cached on each server with one shared load per miss (ADR-0043, amended in 4.8c) and refreshed on every server through a form cache refresher, which the service's publish, unpublish, rename, trash, restore and delete raise. A trashed form is answered as a missing one and takes no submission and no upload (ADR-0047; the submission in 6.1e, the upload in 6.1d).
  - **Designer locks** (added in 5.1f, ADR-0060): the form endpoint and the pipeline read the published form through `IFormBuilderFormService.GetPublishedAsync`, which applies the host's designer locks, and the definition's `ETag` follows what it answers, so a lock that a restart brings or lifts reaches visitors and their caches without a new publish (the pipeline's reading in 6.1e).
  - **The site's time zone** (ADR-0043): the opening and closing times, the rules' `today` and relative dates read `general.timeZone` of the request's site through the settings service (§4.8; the host-name fallback comes with 14.4). A stored zone the server cannot resolve (a site moved to a server without it) counts as UTC, and the site logs a warning that names the setting. The public answer carries the zone for the browser's `today`, the first setting visitors see, so this step adds the allowlist test of ADR-0008: it fills every section and checks the public output against the allowlist. (The submissions that read it come in 6.1e.)
  - Done when: integration tests cover the form endpoint for success, and for a form that is missing or not published; CORS allowed and denied; while the migrations are pending, the form endpoint answers `503 migrations_pending` (ADR-0033); the `jfb-public` snapshot test passes, and so does the test that fails when an endpoint lacks a declaration; an endpoint whose time limit runs out answers `504 timeout`. With `general.timeZone` set to `Pacific/Auckland` the public answer carries that zone, and a stored zone the server cannot resolve counts as UTC with a warning; the allowlist test passes (ADR-0043). A publish on one server reaches the other server's cached form through the refresher, and a trashed form answers like a missing one (ADR-0044, ADR-0047). With a designer lock added between two starts of the site, the form endpoint answers the locked value under a new `ETag` (ADR-0060).
- [x] **6.1b Visitor texts and consent wordings** (6.1 split in five, WORKLOG 2026-10-28; then 6.1b in two, WORKLOG 2026-10-28) (2026-10-28, ef84767)
  - **Visitor texts with merge tags** (added in 5.3f, ADR-0068): the form endpoint gives each visitor text that holds tags as its template with the values of its paths other than `field.*`, resolved for the page and the culture, and what answer recall needs of each field a text recalls: its alias, label, options' labels (a country's and an address's countries too), rows in the culture, and whether its tags show masked or nothing; with the culture's words for yes and no. A test renders a label that recalls an earlier field from the endpoint's data with `recallAnswers` and gets the text the server renders for the same answers, and the projection names no field as sensitive.
  - **Consent wordings** (added in 6.1a, ADR-0009): the form endpoint gives each consent field the `jfbConsentText` hash of the wording it shows in the answer's culture, as the submission posts it (the comparison comes in 6.1e), so the runtime never hashes a wording itself; a test changes a consent's wording through the dictionary and gets another hash.
  - Done when: the recall test above passes; a consent's hash in the form endpoint's answer is the `jfbConsentText` hash of the wording it shows, and another once the dictionary changes that wording; the `jfb-public` snapshot test covers the answer's new parts.
- [x] **6.1c The options endpoint** (6.1 split in five, WORKLOG 2026-10-28; then 6.1b in two, WORKLOG 2026-10-28) (2026-10-28, 9a1f0bd)
  - **Endpoints built here:** `options`.
  - **Cascading options** (added in 5.5g, ADR-0081): the `options` endpoint holds its `parent` answers to the options the parent field offers, as the submission reader does, and its `culture` to the site's cultures, so no text a visitor gives becomes a cache key or reaches a source unchecked; a test asks with a parent answer the parent lacks and gets the options of no answer, and the source never sees the text.
  - Done when: integration tests cover the `options` endpoint for success, and while the migrations are pending it answers `503 migrations_pending`; the `options` endpoint asked with a parent answer the parent field lacks answers the options of no answer, and the source never sees the text (ADR-0081); the `jfb-public` snapshot and declarations tests cover it.
- [x] **6.1d Uploads** (6.1 split in five, WORKLOG 2026-10-28) (2026-10-28, 3dc25a0)
  - **Endpoints built here:** `uploads` (per ADR-0006: the no-form middleware before routing, the multipart body read as a stream, strict limits and the magic-number check, using the 4.3 file store; the challenge part is verified from 7.1 on; hardening in 7.3).
  - **The host's file limits** (§4.8, added in 5.1b): `Files:MaxFileBytes` and `Files:BlockedExtensions` come with the uploads, and publishing then refuses a file field whose `maxBytes` is above the host's limit, or whose extensions the host blocks, at its path under `settings` (the file type's own check, ADR-0056).
  - Done when: integration tests cover the `uploads` endpoint for success, and while the migrations are pending it answers `503 migrations_pending` and stores nothing; oversize bodies. The upload test of 1.4b is ported, with the ASP.NET temp folder watched: a 60 MB upload to a 50 MB field is cut off with nothing buffered, a renamed `.exe` is refused on its first bytes with nothing stored, and a declared length over the limit is refused before a byte is read. A trashed form takes no upload (ADR-0047). A publish refuses a file field whose `maxBytes` is above `Files:MaxFileBytes`, and one that allows an extension the host blocks, each at its path. The `jfb-public` snapshot and declarations tests cover the endpoint.
- [x] **6.1e The submission pipeline and the entries endpoint** (6.1 split in five, WORKLOG 2026-10-28; then 6.1e in two, WORKLOG 2026-10-28) (2026-10-28, 769ddc0)
  - **Endpoints built here:** `entries`, with the JSON body limits of ADR-0012; 6.1f builds `validate` and `receipt` over the same pipeline.
  - Implement the pipeline of §4.4 end to end (spam layers come in Phase 7): availability, validation, idempotency (the server generates a `submissionId` when none is posted), the single transaction, outcomes. It reads the published form as 6.1a's designer locks say, the site's time zone as 6.1a's says, and refuses a trashed form (ADR-0047); places on options come in 6.1g and unique answers in 6.1h. The form endpoint's `open`, which 6.1a gives by the opening and closing times, turns false once the entry limit is reached too (added in 6.1a, ADR-0084).
    - **Empty submissions are refused:** a submission with no answer at all fails validation with a form-level message.
    - **Uploads to a form that takes no entries** (added in 6.1d, ADR-0087): `uploads` checks the form's opening and closing times and its entry limit as the submission does, before a byte of the file is read, and answers with the same codes.
    - **Answer lengths** (ADR-0030, amended in 4.8f): every answer is held to the longest answer its field type takes (5.1), checked before the rules and the validation run, so no rule, pattern or comparison runs over more text than a field takes; a longer answer fails validation at its field.
    - **Reading before the rules** (added in 5.1d, ADR-0058): every answer is read by its field type before the rules run, so rules compare what was read (a number posted with an exponent among them); a field's unreadable answer, missing required value and failed checks are reported only once the rules leave the field visible.
    - **What the checks see** (added in 5.2b, ADR-0062): each visible field's check gets whether it is required, by its own setting or a rule, today in the site's time zone, and for a file field the name and size of the uploaded file each token stands for; the field's `pattern` and `matchesField` checks run after its type's, with the other fields' values as the rules see them.
    - **Values kept only until the actions have run** are stored only when an action run applies (§4.4; their deletion comes with the processor in 8.1).
    - **Redirect outcomes** (ADR-0035): the rendered URL is checked again by the publish rule, and one that fails it is not followed; the visitor gets the default thank-you message and the site logs a warning without the answers.
  - Done when: integration tests cover `entries` for success; validation errors by alias; a replayed submission returning the same receipt (also once the form has closed or its entry limit is reached) and a changed one returning `409`; `form_changed`, `form_closed` and the entry limit, with the form endpoint answering `open: false` once the limit is reached; oversize bodies; an empty submission refused. An answer longer than its field type takes fails validation at its field, and no rule or pattern runs over it (ADR-0030). A JSON body with an escaped lone surrogate answers `400 invalid_request`, not `500` (ADR-0005). A number posted as `1.5e3` is stored as `1500` and a rule compares it as such, and a hidden field's unreadable answer does not fail the submission (ADR-0058). A consent posted with the hash of a wording that has changed since (through the dictionary) answers `form_changed` and stores nothing. A calculation result too long for a number is stored as text only, with `Number` null (ADR-0005). While the migrations are pending, every public endpoint answers `503 migrations_pending` and stores nothing (ADR-0033). A redirect outcome whose merge tags render `//` and another host is not followed (ADR-0035). With `general.timeZone` set to `Pacific/Auckland`, a form open until a local midnight takes a submission a minute before it and answers `form_closed` a minute after it by the test clock, and a date field whose minimum is today accepts the local date there (ADR-0043). A trashed form stores no entry (ADR-0047). A required name made required by a rule fails at its field with `required` when a part is missing, a date field whose window is relative to today is checked against the site's today, and an uploaded file whose extension the field does not take fails at its field with `fileType` (ADR-0062). A submission is held to a designer lock added between two starts of the site (ADR-0060). An upload to a form that has closed, or whose entry limit is reached, is refused with the submission's code before a byte of the file is read, and stores nothing (ADR-0087). The `jfb-public` snapshot and declarations tests cover the endpoint.
- [x] **6.1f The validate and receipt endpoints** (6.1e split in two, WORKLOG 2026-10-28) (2026-10-28, c3d439a)
  - **Endpoints built here:** `validate` and `receipt`, with the JSON body limits of ADR-0012, over 6.1e's pipeline.
  - **Validate** (§4.6, ADR-0012): one page's answers read, ruled and checked as the submission reads, rules and checks them, with the answers of the pages before it, by every check but `uniquePerForm` (6.1h checks that it is left out); `422 validation_failed` with the errors of that page's fields by alias; nothing is stored.
  - **Receipt** (§4.6, ADR-0012): the receipt and the outcome of the entry that a `submissionId` names on this form, and the same `404 not_found` for every miss, so submission ids cannot be probed; 7.1 binds it to the entry's challenge or server key.
  - Done when: integration tests cover `validate` and `receipt` for success; validation errors by alias, of the posted page's fields only; a `receipt` for an entry returns the receipt and the outcome that `entries` answered with, and a `receipt` for an unknown submission or for another form's answers `404 not_found`; oversize bodies; a JSON body with an escaped lone surrogate answers `400 invalid_request` (ADR-0005); `validate` stores nothing, and while the migrations are pending both answer `503 migrations_pending` and store nothing (ADR-0033). The `jfb-public` snapshot and declarations tests cover the endpoints.
- [x] **6.1g Places on options** (6.1 split in five, WORKLOG 2026-10-28; 6.1f until 6.1e split in two; then 6.1g in two, WORKLOG 2026-10-28) (2026-10-28, 8cc251b)
  - **Places on options** (§4.4): taken in the transaction, given back on reject, spam, delete and erase.
  - **The places follow the entries** (ADR-0090): an option's places taken are the values of the form's entries that hold places (not test entries, drafts, spam or rejected ones) at the field that equal the option's value, a repeatable group's rows and a field's several choices each taking one. Every write of an entry that changes such a value or whether its entry holds places (a submission, a state change, a change of its values, a delete; 13.2's anonymising changes values) changes the option's count in the same transaction, after locking the form's row, so a form's places change one transaction at a time on both databases. A limit set after entries came in counts them. A submission checks its options' places while its answers are read, and again inside its transaction once its values are counted; an option without enough places fails at the field, and at each row past the places, with `full`, and the transaction stores nothing. `validate` answers `full` too, and takes nothing.
  - Done when: integration tests cover 50 parallel submissions for the last 10 places of an option (exactly 10 accepted, and the option's count equal to its entries' values afterwards), a rejected entry giving its place back, and a replayed submission returning the same receipt once the option it took is full. Entries marked as spam or deleted give their places back, an entry moved back takes its place again, and a change of an entry's values moves its place. A repeatable group's rows and a field's several choices each take a place, and the rows past the places fail at their paths. An option of a library list takes places as its list sets them. A limit set after entries came in counts them. `validate` answers `full` for an option without a place left and takes nothing. A test entry takes no place.
- [x] **6.1h Unique answers** (6.1g split in two, WORKLOG 2026-10-28) (2026-10-28, 88b3656)
  - **Unique per form** (added in 5.2, ADR-0002): a field's `uniquePerForm` check fails when another entry of the form already holds the same answer, compared as entries filter (cleaned and folded, `TextKey`), checked inside the submission's transaction, so that two submissions at once cannot both pass; a deleted or erased entry no longer counts.
  - Done when: of 10 parallel submissions with the same answer to a unique field, exactly one is accepted and the others fail at that field, and an answer that differs only in case or surrounding spaces counts as the same. `validate` passes an answer that another entry already holds, so it cannot tell whether an answer is taken (ADR-0012).
- [x] **6.2a Website assets and `<jfb-assets>`** (6.2 split in six, WORKLOG 2026-10-28) (2026-10-28, e2f6b23)
  - **Serving the assets** under `/jfb/assets/` itself, from the resources embedded in the Web assembly (ADR-0026): only names the assembly holds, compared exactly (no case folding, no decoded `%2F`, nothing outside `jfb/assets/`):
    - the precompressed Brotli or gzip copy, chosen by `Accept-Encoding`;
    - `Cache-Control: public, max-age=31536000, immutable` under a `?v=` fingerprint;
    - `X-Content-Type-Options: nosniff`.

    Umbraco's static files send no compression and no caching headers (§3.4).
  - **`<jfb-assets>`:** the tag helper that writes the module script (and, from 6.3a, the stylesheets) once per page under the fingerprint, with the nonce option (`<jfb-assets nonce>` and `IFormBuilderCspNonceProvider`, ADR-0004). 6.2b includes it automatically.
  - Done when: TestServer tests pass, including the compressed and cacheable asset responses, every built website asset found in the assembly with both its copies, and `404` for an unknown, differently cased or traversing asset path; `<jfb-assets>` named twice on a page writes its tags once, with the nonce of its attribute or of a registered `IFormBuilderCspNonceProvider`, and without a nonce when there is neither.
- [x] **6.2b The view component and the markup contract** (6.2 split in six, WORKLOG 2026-10-28; then 6.2b in two, WORKLOG 2026-10-28) (2026-10-28, 89549d8)
  - **Rendering:**
    - the view component and the `<jfb-render>` tag helper, which includes the assets once per page automatically unless turned off (6.2a), with its `context-…` values (§4.9) for the context values the form lists;
    - the views of ADR-0015 in the default view folder, each with its contract header, and a form's own overrides: Form, Page, Group, Field, Navigation, Progress, ErrorSummary, Outcome and Notice, with the controls of the text, number and content types and of the hidden field (6.2d, 6.2e and 6.2f add the others; the themes and their resolution order come in 6.3a);
    - the form picker model `FormBuilderFormReference`;
    - the markup contract (`data-jfb-*` attributes, deterministic per-request instance ids), with the model data the runtime reads, taken from the form endpoint's answer (6.1a, 6.1b) so the Razor and the headless paths read one model, and every element in the state its rules give for the default answers (ADR-0015);
    - the page's culture.
  - Done when: TestServer tests render a form through `<jfb-render>` and through `Component.InvokeAsync`, two forms on one page under their own instance ids, with the hooks of ADR-0015 that these views hold, the assets once and the model data equal to the form endpoint's answer. A TestServer test renders a form whose hidden field takes its value from the signed-in member, and that value never appears in the rendered markup (moved from 5.4). A view in the form's own override folder renders in place of the default one; a `context-…` value the form lists renders, and one it does not list renders nothing. The rendered page is looked at in Edge.
- [x] **6.2c Visitor texts, members-only forms and pending migrations** (6.2 split in six, WORKLOG 2026-10-28; then 6.2b in two, WORKLOG 2026-10-28) (2026-10-29, 707d336)
  - **Rendering:**
    - member gating messages in the page's culture;
    - **visitor texts** (added in 5.3f, ADR-0068) through the merge-tag service in the visitor-text place, each with the page, group or field whose text it is, and on a no-JS post with the posted answers (6.2h), so a label that recalls an earlier answer shows what the runtime shows;
    - **pending migrations** (ADR-0033): while the plan is behind, a form renders nothing for visitors and a short note for a signed-in backoffice user, and the rest of the page renders as usual;
    - **the General section's required-marker style and default labels** (§4.8, ADR-0093), which a form's `inherit` marker and its labels left empty take, per site.
  - Done when: a TestServer test renders a label that recalls an earlier answer with the text the server renders for the same answers. A TestServer test shows a page whose form cannot render while the migrations are pending: the page answers `200` without the form for a visitor (ADR-0033), and with the note for a signed-in backoffice user. A members-only form shows its notice in the page's culture to a visitor who is not signed in. With a site's required-marker style set to `required`, a form whose marker is `inherit` marks its required fields with the asterisk on that site only, and a site's default Send label shows on a form without one of its own. The rendered page is looked at in Edge.
- [x] **6.2d The choice controls** (6.2 split in six, WORKLOG 2026-10-28; then 6.2b in two, WORKLOG 2026-10-28; then 6.2d in two, WORKLOG 2026-10-29) (2026-10-29, f46fae8)
  - **The choice types' controls** (`fields/<type>.cshtml`), following the specimen in `docs/ux/website/fields.html` and the classes its README asks step 6 to adopt: the choices (a list, horizontal, cards, yes or no buttons or a switch, scale, rating) with the "Other" input, the dropdown (one answer or several), the grid, the ranking with its move buttons, the single checkbox, the consent with its wording hash, and the country.
  - **Options from a library list or another source** are resolved on the server for the page's culture and site, and a cascading field's for its parent's answers on the page, as the `options` endpoint resolves them (ADR-0081, ADR-0086), so a page without JavaScript offers them too.
  - Done when: a contract test renders a form with every choice type (a radio in each layout, checkboxes, a dropdown with one answer and with several, yes or no as buttons and as a switch, scale, rating, grid, ranking, checkbox, consent and country), each empty, answered, with an error and hidden by a rule, and checks ADR-0015's hooks on each: a fieldset with its legend for a group of choices, an id per option, the name repeated for several answers, the "Other" input, a grid row's names, and the consent's hash input equal to the form endpoint's hash. A field whose options come from a library list, and a cascading field below an answered parent, render the options the `options` endpoint answers for the same answers. axe finds no violation on the rendered page in Edge, which is looked at.
- [x] **6.2e The part, date and output controls** (6.2 split in six, WORKLOG 2026-10-28; then 6.2b in two, WORKLOG 2026-10-28; then 6.2d in two, WORKLOG 2026-10-29; then 6.2e in two, WORKLOG 2026-10-29) (2026-10-29, ebed620)
  - **The controls of the types with parts, dates and outputs** (`fields/<type>.cshtml`), following the specimen in `docs/ux/website/fields.html` and the classes its README asks step 6 to adopt: dates and times (`date`, `time`, `datetime`), the slider with its output, a name's and an address's parts (`<alias>.<part>`, ADR-0058), the native file input, the signature's typed alternative, the calculation's output and the captcha's place.
    - **Limits relative to today** become the browser's `min` and `max` for the site's today (ADR-0043), as the server checks them (ADR-0062).
    - **The calculation's value** is worked out on the server for the page's answers, as the pipeline works it out (§4.3.3), so a page without JavaScript shows it too.
  - **A custom type's control** from its own `fields/<type>.cshtml`, found the same way (ADR-0015).
  - Done when: a contract test renders a form with each of these types (a date, a time and a date and time with limits, the slider, a name, an address, a file field, a signature, a calculation and the captcha's place), each empty, answered, with an error and hidden by a rule, and checks ADR-0015's hooks on each: a fieldset with its legend for a field with parts, an id per control and per part, a part named `<alias>.<part>`, a date's limits for the site's today, and a calculation showing the value the pipeline works out for the same answers; a custom type's control renders from its own view; axe finds no violation on the rendered page in Edge, which is looked at.
- [x] **6.2f The repeater and the kitchen sink** (6.2 split in six, WORKLOG 2026-10-28; then 6.2b in two, WORKLOG 2026-10-28; then 6.2d in two, WORKLOG 2026-10-29; then 6.2e in two, WORKLOG 2026-10-29) (2026-10-29, 684fbfa)
  - **The repeater's rows** (`fields/repeater.cshtml`), following the same specimen and classes: each row a fieldset with its legend and its child fields named `<alias>[<row>].<child>` (ADR-0015), the rows the page's answers hold (a post's, or the rows a first render starts with), and the Add and Remove buttons as submit buttons that the no-JS post reads (6.2i). The post state carries a repeatable group's rows and their errors at their paths, which 6.2h's surface action fills.
  - Done when: a contract test renders a kitchen-sink form (every built-in field type, each empty, answered, with an error and hidden by a rule) and checks ADR-0015's hooks on each; a custom type's control renders from its own view; axe finds no violation on the rendered page in Edge, which is looked at.
- [x] **6.2g Visitor error messages** (6.2 split in six, WORKLOG 2026-10-28; then 6.2b in two, WORKLOG 2026-10-28; then 6.2d in two, WORKLOG 2026-10-29; then 6.2e in two, WORKLOG 2026-10-29; then 6.2g in three, WORKLOG 2026-10-29) (2026-10-29, d23add7)
  - **The message a visitor sees for each error** (§4.3.3, D10, `docs/ux/copy.md`): a field's own message for the kind of error (`messages`), a check's own message, a validation rule's message, and otherwise the package's default for the kind and the field's type, in the page's culture, with the values the field's settings give (lengths, words, limits, steps, a date window relative to the site's today, choices, file types, sizes and counts, rows) in the culture's formats as merge tags show numbers and dates (ADR-0066); and the form-level message of an empty submission. English and German now; 14.2 adds the other five languages.
  - **One set of words for both runtimes:** `tests/vectors/messages.json` holds the defaults and how they take their values, and Core and the runtime's shared logic (`packages/logic`) pass it now; 6.4d works out the runtime's messages and 6.4e shows them on the page (strengthened in this split), so the browser and the server never disagree about a message (D10).
  - **The error summary** (ADR-0022): its heading says whether one answer or several need a change (`docs/ux/copy.md`), and a link to a default message names its field, so every link says which question it is (WCAG 2.4.4); an editor's own message shows as written. The no-JS post of 6.2h shows these messages.
  - Done when: Core passes `tests/vectors/messages.json`, whose cases cover every error kind each built-in type declares in English and in German, with values in each culture's formats, a plural, a date window relative to today and a list of file types; a test finds a default in both languages for every kind every built-in type declares, and fails for one left out. Tests show a field's own message, a check's own message and a validation rule's message shown instead of the defaults, the empty submission's message, the summary's heading for one answer and for several, and a summary link to a default message naming its field.
- [x] **6.2h The no-JS post** (6.2 split in six, WORKLOG 2026-10-28; then 6.2b in two, WORKLOG 2026-10-28; then 6.2d in two, WORKLOG 2026-10-29; then 6.2e in two, WORKLOG 2026-10-29; then 6.2g in three, WORKLOG 2026-10-29) (2026-10-29, 6c22fd8)
  - **The no-JS post:** through the surface controller, with post/redirect/get and re-rendered errors, server-side paging for multi-page forms, and the outcomes (message, redirect, message then redirect). Our own `<form>` with `SurfaceControllerHiddenInput`, the redirect with the instance fragment, and the `UmbracoRouteValues` check, per ADR-0004. Every response of the surface action is `no-store` (ADR-0004; 6.2k adds the rest of the cache rules). Errors show in the messages of 6.2g.
    - **Answers without JavaScript** (added in 5.1d, ADR-0058): inputs are named `<alias>` and `<alias>.<part>`, and a long text's line breaks reach the pipeline as the runtime sends them (CRLF becomes LF), so both paths store the same text.
    - **Rows as posted** (added in this split, ADR-0098): a repeatable group's rows, named `<alias>[<row>].<child>`, reach the pipeline as the runtime's rows do, and the post state carries each row's answers and errors at their paths; adding, removing and moving come in 6.2i.
    - **A switch and a consent without JavaScript** (added in 6.2d, ADR-0096): a yes or no switch that was shown on the posted page and posts nothing is a `no`, as the runtime sends it, and a consent's `jfb-consent-<alias>` input is its wording's part.
  - Done when: Playwright with JavaScript **disabled** submits a valid form, an invalid one and a three-page one, with two forms on one page; a name's parts, a long text with line breaks and a repeater's rows are stored as the runtime's post to `entries` stores them. A no-JS post whose consent wording changed since the page was rendered shows the form again with that consent unticked and a notice. A yes or no switch left off is stored as `no`, as the runtime's post stores it (added in 6.2d). TestServer tests show the surface auto-route answering `400` and every response of the surface action `no-store`.
- [x] **6.2i Rows and moves without JavaScript** (6.2 split in six, WORKLOG 2026-10-28; then 6.2b in two, WORKLOG 2026-10-28; then 6.2d in two, WORKLOG 2026-10-29; then 6.2e in two, WORKLOG 2026-10-29; then 6.2g in three, WORKLOG 2026-10-29) (2026-10-29, 323f224)
  - **Rows and moves without JavaScript:** a repeatable group's "Add" and "Remove" and a ranking's moves are submit buttons that render the page again with the answers kept; a move posts `jfb-nav` = `up:<alias>:<n>` or `down:<alias>:<n>`, the option's place in the order shown (ADR-0096, added in 6.2d).
  - Done when: Playwright with JavaScript **disabled** adds and removes a repeater row, and a ranking's moves reorder it with the answers kept (added in 6.2d). TestServer tests show an Add past the group's most, a Remove below its least and a move past an end changing nothing, each rendering the page again with every answer kept (added in this split).
- [x] **6.2j No-JS posts with files** (6.2 split in six, WORKLOG 2026-10-28; then 6.2b in two, WORKLOG 2026-10-28; then 6.2d in two, WORKLOG 2026-10-29; then 6.2e in two, WORKLOG 2026-10-29; then 6.2g in three, WORKLOG 2026-10-29; then 6.2j in two, WORKLOG 2026-10-29) (2026-10-29, 05961ac)
  - **No-JS posts with files** (ADR-0006): the `?jfb-post=<key>.<instance>` marker in the action, the limits middleware before routing with its drain allowance, and the upload's file rules in the surface action. The message for a post that is too large is designed once, and a file that passed its checks is kept across a re-render for other errors, through a signed token in the form.
  - Done when: with JavaScript disabled, Playwright in Edge shows a file that is too large reported at its field (not a reset connection), a valid file stored, a renamed `.exe` refused, and a valid file kept when another field fails (ADR-0006).
- [x] **6.2k A signature typed without JavaScript** (6.2 split in six, WORKLOG 2026-10-28; then 6.2b in two, WORKLOG 2026-10-28; then 6.2d in two, WORKLOG 2026-10-29; then 6.2e in two, WORKLOG 2026-10-29; then 6.2g in three, WORKLOG 2026-10-29; then 6.2j in two, WORKLOG 2026-10-29) (2026-10-29, a45fdd2)
  - **A signature typed without JavaScript** (added in 6.2e, ADR-0097): the page posts the typed name as `<alias>.typed`, and the surface action turns it into the stored signature, held to the signature's checks (ADR-0071), so a visitor without JavaScript can sign. Turning a name into the signature's PNG on the server needs a text renderer of its own (no image library may be added, CLAUDE.md rule 4), decided in an ADR: how a name in any script is drawn, and what is stored when it cannot be.
  - Done when: a signature given by typing the name with JavaScript disabled is stored as the signature (ADR-0097, added in 6.2e).
- [x] **6.2l The output cache rules and the render endpoint** (6.2 split in six, WORKLOG 2026-10-28; then 6.2b in two, WORKLOG 2026-10-28; then 6.2d in two, WORKLOG 2026-10-29; then 6.2e in two, WORKLOG 2026-10-29; then 6.2g in three, WORKLOG 2026-10-29; then 6.2j in two, WORKLOG 2026-10-29) (2026-10-29, ecebe94)
  - **The output cache rules of ADR-0004:** `no-store` on every response of the surface action and on every render that is per visitor (a members-only form's notice or form, and the note while the migrations are pending, ADR-0095), not a `GET`, or carries `jfb-sent`; the `IWebsiteOutputCacheVaryByProvider` for `jfb-sent`; never an `IWebsiteOutputCacheRequestFilter`.
  - **The `render` endpoint** of §4.6 (for `form_changed`), through the same view component.
  - Done when: with Umbraco's output cache on, tests show a cached page byte-identical for two visitors and holding nothing per visitor; a page with a members-only form, for a visitor and for a member, and a page with a form while the migrations are pending, for a visitor and for a signed-in backoffice user, never stored; `?jfb-sent` showing the outcome and never stored, for any value; no response to a post stored; the surface auto-route answering `400`; and `render` returning the published form with the given instance id. The `jfb-public` snapshot and declarations tests cover `render`.
- [x] **6.3a The theme registry, view resolution and the base rules** (6.3 split in three, WORKLOG 2026-10-29) (2026-10-30, a20508d)
  - **Mechanics:**
    - a theme registry (`IFormBuilderTheme`): the three built-in themes with their alias, name, stylesheet, class map, token defaults, dark scheme and view folder, each stylesheet and its tokens coming with the theme's look in 6.3b and 6.3d (ADR-0015, ADR-0105, added in this split);
    - a form's theme: its `appearance.theme`, else the default theme of the General section for its site, a setting that comes with this feature (code default `default`), else `default`; a theme that is gone renders with `default` (ADR-0015, added in this split);
    - view resolution in the order per form → theme → default, in the layout of ADR-0015;
    - versioned view-contract headers;
    - the class map: a theme's classes added to the slots of ADR-0015 through the view models, never in place of ours (ADR-0015, added in this split).
  - **The functional base rules** that every theme ships, `unstyled` included, in a stylesheet no theme can leave out (ADR-0015): the honeypot out of view, visually hidden text, `[hidden]` honoured, and the two below. `<jfb-assets>` and a form's automatic assets write it and the form's theme's stylesheet once per page (added in this split).
  - **Where a press without JavaScript lands** (added in 6.2i, ADR-0101): a row's Add or Remove and a ranking's move open the page at the row added, the group or the option's new place. Every theme, `unstyled` included, gives those targets (`.jfb-row`, `.jfb-repeater`, `.jfb-ranking > li`) a block-start scroll margin, so the line above stays in view and a site's sticky header does not cover them.
  - **A phone's width** (added in 6.2k): every theme, `unstyled` included, lets a group's fieldset shrink below its widest control and keeps a select within its field, so no control widens the page. Without a theme, the German room hire form's group of name, email and address was 10 px wider than a 375 px window.
  - Done when (added in this split): a contract test renders a kitchen-sink form (every field type) in every built-in theme and checks ADR-0015's hooks, with each theme's classes beside ours; TestServer tests show a form's theme taken from the form, from the General setting and from the code default, a theme that is gone rendering `default`, each view found in the order form → theme → default, and the stylesheets written once per page; in Edge without JavaScript at 375 px, in every built-in theme, the honeypot is out of view and out of the accessibility tree, a ranking's move opens the page with the option before the moved one in view, and no demo form scrolls sideways in English or German.
- [x] **6.3b The default theme** (6.3 split in three, WORKLOG 2026-10-29) (2026-10-30, 0580101)
  - **`default`:**
    - CSS custom properties and an opt-in dark scheme;
    - the error summary plus inline errors, character counters;
    - styled native controls and choice cards;
    - the design from 2.3.
  - **Right to left** (added in 1.1): the themes use logical CSS properties only, take `dir` from the culture, and mirror directional icons. Building it into the first themes costs little; adding it later would mean touching every rule.
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before styling the themes (ADR-0019, ADR-0020).
  - Done when (added in this split, each also part of 6.3d's check, which was 6.3c's before its split): Playwright screenshots of the kitchen-sink form in `default` at 375 and 1440 px, light and dark, and once with `dir="rtl"`, are reviewed in `docs/ux/review-6.3.md`, which has a section per skill with its output and findings; axe is clean; the CSS is within budget; a lint check refuses physical `left`/`right` properties in the theme CSS; no demo form scrolls sideways at 375 px in English or German without JavaScript.
- [x] **6.3c The unstyled theme** (6.3 split in three, WORKLOG 2026-10-29; then 6.3c in two, WORKLOG 2026-10-30) (2026-10-30, 729eff7)
  - **`unstyled`:** semantic markup and classes only, plus the functional base rules every theme ships (the honeypot out of view, visually hidden text, ADR-0015).
  - **Markup that reads without a theme** (added in this split): every control shows what it means with no theme's stylesheet. A rating's points show their numbers, which a theme that draws symbols hides; a ranking's moves show their arrows in every theme, through the base rules; a scale's points sit in a row with the words at its ends under its two ends, apart. The review added (ADR-0107): a grid's column heads stay hidden where no theme lines the columns up, a ranking's places show once, a point with a word at its end is named without a space before the comma, and the lists the themes draw without markers say they are lists.
  - **Right to left**, as in 6.3b (added in the split of 6.3).
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` again before styling the themes and reviewing them (ADR-0019, ADR-0020, added in the split of 6.3).
  - Done when (added in this split, each also part of 6.3d's check): Playwright screenshots of the kitchen-sink form in `unstyled` at 375 and 1440 px, and once with `dir="rtl"`, are reviewed in `docs/ux/review-6.3.md`, which has a section per skill with its output and findings; axe is clean; in Edge, `unstyled` shows a rating's numbers, a ranking's arrows and a scale's end words apart, while `default` still shows its symbols in their place; in every theme, `unstyled` included, the honeypot is out of view and out of the accessibility tree; the CSS is within budget; a lint check refuses physical `left`/`right` properties in the theme CSS. In every theme at 375 px without JavaScript, a ranking's move opens the page with the option before the moved one in view. No demo form scrolls sideways at 375 px in English or German, with or without JavaScript, in any theme.
- [x] **6.3d The bootstrap5 theme, and the review of the themes** (6.3 split in three, WORKLOG 2026-10-29; then 6.3c in two, WORKLOG 2026-10-30) (2026-10-30, e1229ac)
  - **`bootstrap5`:** Bootstrap 5 classes. Bootstrap itself is not shipped; the demo and tests load it (MIT, dev-only) to check the theme. The review added (ADR-0108): the theme's stylesheet draws from Bootstrap's own variables and follows its colour modes; the class map's plain button keeps its contrast in Bootstrap's dark mode; the file input takes the theme's classes for a control; the demo's copy of Bootstrap is held to the published files' hashes.
  - **Right to left**, as in 6.3b (added in the split of 6.3).
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` again before styling this theme and reviewing all three (ADR-0019, ADR-0020, added in the split of 6.3).
  - Done when: Playwright screenshots of a kitchen-sink form (every field type) per theme at 375 and 1440 px, and once with `dir="rtl"`, are reviewed in `docs/ux/review-6.3.md`, which has a section per skill with its output and findings; axe is clean; in every theme, `unstyled` included, the honeypot is out of view and out of the accessibility tree; the CSS is within budget; a lint check refuses physical `left`/`right` properties in the theme CSS. In every theme at 375 px without JavaScript, a ranking's move opens the page with the option before the moved one in view (added in 6.2i). No demo form scrolls sideways at 375 px in English or German, with or without JavaScript, in any theme (added in 6.2k).
- [x] **6.4a Reading answers in both runtimes** (6.4 split in nine, WORKLOG 2026-10-30; then 6.4a in two, WORKLOG 2026-10-30) (2026-10-30, a9eee6f)
  - **The reading stage in TypeScript,** the twin of Core's (ADR-0058): before the rules and the checks, `packages/logic` holds each answer to its type's limits (how many texts and how long each, refused as `maxLength` or `invalid` as the server refuses them) and reads it into the wire form as the type does: a number as a browser's number input sends it or the visitor typed it, converted exactly as Core converts it (ADR-0005); dates, times and date-times that must parse; one value where the type takes one, and no value twice; choices with their "Other" text; parts given once; a grid's rows; a ranking's places; yes or no; a consent with its wording's hash; a country's code. An answer that cannot be read is `invalid` alone and gives the rules no value, as on the server.
  - **What the browser needs to know,** from the form endpoint, so in the page's model data and on a headless page alike: each field's kind for the rules and for its checks, whether it can be required, and whether its type is the built-in one of its alias; the culture's formats and the words for yes and no on every form, not only on one whose texts hold merge tags (ADR-0085, ADR-0099).
  - **The error summary after a question** (added in 6.3b, ADR-0106): an entry whose label already ends in a question mark, an exclamation mark, a full stop or a colon takes no colon after it ("Where would you like to sit? Choose an answer"), in the server's words, the runtime's and the vectors alike, in every visitor language.
  - Done when:
    - a new suite, `tests/vectors/reading.json` (every built-in type's limits and readings, unreadable answers, numbers as browsers send them), passes in NUnit against Core and in Vitest against `packages/logic`, and the mutation check catches every mutant of the reading stage in both runtimes;
    - Vitest and NUnit pass `tests/vectors/messages.json` (added in 6.2g's split), whose vectors include an error summary entry after a label that ends in a question mark (added in 6.3b);
    - integration tests show the form endpoint's kinds, formats and words for a form with merge tags and one without, and the OpenAPI document and the public API's docs describe them.
- [x] **6.4b The runtime's frame and the reading of the page** (6.4 split in nine, WORKLOG 2026-10-30; then 6.4a in two, WORKLOG 2026-10-30; then 6.4b in two, WORKLOG 2026-10-30) (2026-10-30, c12c8d8)
  - **The frame:** the module finds every form of the markup contract on the page (ADR-0015), reads its model data, and enhances each once; `JironFormBuilder.init(root)` enhances forms injected later (§4.9). A form whose contract version the runtime does not know is left to the no-JS post. `jfb:ready` fires once per form.
  - **The chunk with the logic** (added in this split; ADR-0004 loads the error display on demand): the entry holds the frame, and the shared logic with what the runtime does with it loads as one chunk on a form's first interaction, so a page whose visitor never touches a form loads the frame alone, and the core keeps §4.12's 16 KB, which the shared logic by itself nearly fills. A form whose chunk cannot load is left to the no-JS post.
  - **Reading the page** (moved here from validation in this split): the runtime reads the page's controls into the answers as the no-JS post reads their names (ADR-0058, ADR-0100), through 6.4a's reading stage, so values are cleaned with the same functions before they are sent, and numbers sent in the wire form (ADR-0005).
  - Done when:
    - Vitest covers the frame and the reading of the page's controls;
    - a new suite, `tests/vectors/posts.json` (a page's names and values as a browser posts them: a field's parts, a repeatable group's rows, a consent's wording, a yes or no switch that posts nothing, the context and query values, line breaks, names of no field), passes in NUnit against the no-JS post's reader and in Vitest against the runtime's (added in this split);
    - Playwright on the demo shows `jfb:ready` once per form, a form injected later enhanced through `init(root)`, the logic's chunk loaded only once the visitor touches a form, and no console error (added in this split);
    - the size check passes with every chunk.
- [x] **6.4c Rules on the page** (6.4 split in nine, WORKLOG 2026-10-30; then 6.4a in two, WORKLOG 2026-10-30; then 6.4b in two, WORKLOG 2026-10-30; then 6.4c in two, WORKLOG 2026-10-30) (2026-10-30, 77cde6c)
  - **Rules:** progressive enhancement, plus the rules from `packages/logic`, over the answers 6.4b reads from the page. The form engine's model of the form is made in the browser from the form endpoint's answer as Core makes it from the definition (`DefinitionLogic`), held to it by a fixture both runtimes read (added in this split), and the rules run in the context the server gives them, as far as a page can know it: the site's clock and time zone, the culture, and the query values the form lists.
  - **What the visitor sees:** a field a rule shows, hides or requires changes as the server would render it: `hidden` and `data-jfb-hidden`, its controls disabled, a hidden page's and group's fields with it; `required` on the controls the server marks, and the required marker the form's style shows. The views give the runtime what it needs for that as hooks of the markup contract (ADR-0015), where the server alone can say it (added in this split).
  - Done when:
    - Vitest covers rules (the model of the form, the context, and the state on the page);
    - the model of the form passes its fixture in NUnit against `DefinitionLogic` and in Vitest (added in this split);
    - Playwright on the demo covers rules (a field shown, hidden and required as answers change) in English and German;
    - axe is clean on the demo's forms in the states the rules give them (added in this split);
    - the size check passes with every chunk.
- [x] **6.4d Checks and messages on the page** (6.4 split in nine, WORKLOG 2026-10-30; then 6.4a in two, WORKLOG 2026-10-30; then 6.4b in two, WORKLOG 2026-10-30; then 6.4c in two, WORKLOG 2026-10-30; then 6.4d in two, WORKLOG 2026-10-30) (2026-10-30, 0c13eb6)
  - **Validation:** progressive enhancement, plus client validation from `packages/logic`, over the answers 6.4b reads and in the state 6.4c's rules give: each shown field's errors as Core's pipeline decides them, the checks that only the server can make (unique answers, uploads, options from a source the page does not hold) left to it.
    - **Messages:** the server's (6.2g), held to the same words by `tests/vectors/messages.json`; English and German now; 14.2 adds the other five languages. As the server shows them (added in this split): an editor's own message, the field's for the kind, the check's or the validation rule's, as written; otherwise the default, and in the error summary the field's label and a row's legend before it.
    - **Delivery:** one small file per language, loaded for the page's culture only, to keep the §4.12 budget.
  - Done when:
    - Vitest covers validation (the checks over the page's answers and their messages);
    - a fixture of the server's errors (added in this split: forms, answers as a browser posts them without JavaScript, and each field's message and summary link as the server renders them after the post, written by an integration test) passes in Vitest against the runtime's;
    - the size check passes with every chunk, the language files among them.
- [x] **6.4e Errors on the page** (6.4 split in nine, WORKLOG 2026-10-30; then 6.4a in two, WORKLOG 2026-10-30; then 6.4b in two, WORKLOG 2026-10-30; then 6.4c in two, WORKLOG 2026-10-30; then 6.4d in two, WORKLOG 2026-10-30) (2026-10-30, ef7dfd5)
  - **What the visitor sees:** the server's inline errors and error summary, in the server's markup with the theme's classes, from 6.4d's checks and messages; a refused send moves the focus to the summary, and an answer fixed clears its error. `jfb:invalid` fires with the field and the kind of error, never the answer.
  - Until 6.4i, a form that passes the client's checks still sends through the no-JS post.
  - Done when:
    - Vitest covers validation on the page (the errors shown, the summary and its focus, the event);
    - Playwright on the demo covers validation (inline errors as the visitor leaves a field, the summary and its focus after a refused send, an error cleared when fixed) and a hidden field never blocking the send, in English and German, and shows that only the page's language file loads;
    - axe is clean on the demo's forms with errors shown by the runtime;
    - the size check passes with every chunk.
- [x] **6.4f Live calculations** (6.4 split in nine, WORKLOG 2026-10-30; one letter on as 6.4a, 6.4b, 6.4c and 6.4d split in two, WORKLOG 2026-10-30; then 6.4f in two, WORKLOG 2026-10-30) (2026-10-30, dd0ec48)
  - **Live calculations:** a calculation's result follows its answers as the visitor types (§4.3.3), worked out in the browser as the server works it out, over the answers the runtime reads into the wire form as Core's types read them (ADR-0058), and shown in the field's format; an unreadable answer, an empty date or a failed division leaves it without a value.
  - Calculations load on demand as their own chunk (§4.12), only on a form that has one: the form engine takes the calculation language when a form needs it (added in the split of 6.4f).
  - **An unreadable answer in both runtimes:** an answer its type cannot read, or over its limits, leaves a calculation that reads it without a value on the server too, as §4.3.3 says, where the reading stage had made it count as unanswered (0); the shared vectors hold both runtimes to it (added in 6.4f, ADR-0114).
  - **A slider's value** beside its thumb and in its `aria-valuetext` follows the thumb as the server writes it (added in 6.4f: the room hire works its cost out of one, and no step kept them).
  - Done when: Vitest covers the runtime's part, and a fixture of the server's own pages holds its results and sliders to the server's, element for element (added in 6.4f); Playwright on the demo covers calculations (a value that follows its answers, an unreadable answer leaving it without a value, the value the server stores, and a form without a calculation never loading the calculation chunk, added in the split of 6.4f) and a slider's value following its thumb (added in 6.4f); axe is clean; the size check passes with every chunk.
- [x] **6.4g Answer recall** (6.4 split in nine, WORKLOG 2026-10-30; one letter on as 6.4a, 6.4b, 6.4c and 6.4d split in two, WORKLOG 2026-10-30; then 6.4f in two, WORKLOG 2026-10-30; then 6.4g in two, WORKLOG 2026-10-30) (2026-10-30, 63f87fe)
  - **Answer recall:** texts that use earlier answers update as the visitor types (§4.3.3), through `recallAnswers` over the answers the runtime reads into the wire form as Core's types read them (ADR-0058, ADR-0068). The merge tags load on demand as their own chunk, only on a form that has such a text (added in the split of 6.4f).
  - **The error summary's links** name a label that recalls answers as the page shows it now (added in the split of 6.4d).
  - Done when: Vitest covers the runtime's part, and a fixture of the server's own pages holds the recalled texts to the server's, element for element (added in the split of 6.4g); Playwright on the demo covers answer recall (a label, a help text and a text block that follow the answers, a hidden field recalled as empty, and the error summary's link naming a recalled label, added in the split of 6.4g); axe is clean; the size check passes with every chunk.
- [x] **6.4h Defaults with merge tags** (6.4 split in nine, WORKLOG 2026-10-30; one letter on as 6.4a, 6.4b, 6.4c and 6.4d split in two, WORKLOG 2026-10-30; then 6.4f in two, WORKLOG 2026-10-30; then 6.4g in two, WORKLOG 2026-10-30) (2026-10-30, cf95911)
  - **A default value with merge tags** is filled in the browser from what the page knows, as ADR-0093 leaves it (added in the split of 6.4a): the form endpoint's answer gives each such default's template and the values of the tags the server knows, and the page fills in the rest (its query values) as the form is shown, before the visitor touches it (added in the split of 6.4g).
  - Done when: Vitest covers the runtime's part; an integration test covers the form endpoint's answer for defaults with merge tags (added in the split of 6.4g); Playwright on the demo covers a default with merge tags filled in; axe is clean; the size check passes with every chunk.
- [x] **6.4i Submitting** (6.4 split in nine, WORKLOG 2026-10-30; one letter on as 6.4a, 6.4b, 6.4c, 6.4d, 6.4f and 6.4g split in two, WORKLOG 2026-10-30) (2026-11-01, bc24d70)
  - **Resumes (2026-11-01):** the owner raised the budget to 60 KB (ADR-0146); this step resumes at 7.4i, with every check below.
  - **Blocked (2026-10-30):** built on the branch `wip/6.4i-sending`, sending takes the website runtime with every feature to 41.38 KB gzip, past the 40 KB of section 4.12, a budget only the owner can change; the work so far and its design wait there (ADR-0120, `docs/OWNER-TODO.md`).
  - **Submitting:**
    - fetch submission with an in-flight lock and a stable `submissionId`;
    - the error summary with focus management, and `aria-live` announcements;
    - a submission id that does not depend on `crypto.randomUUID()`, which exists only in secure contexts (ADR-0004);
    - the send states of ADR-0022's amendment: the busy primary button (`data-jfb-busy`, `aria-disabled`, "Sending your answers" in the live region), the error notice just above the buttons that takes the focus after a refused or failed send (`rate_limited`, `server_error`, `timeout`) with every answer kept, and the redirect notice with "Go now" and "Stay here";
    - the outcome in place of the form, with `receipt` for an id that already holds an entry with other answers, so a fallback post never stores a second one (added in the step's review, ADR-0120);
    - the challenge asked for as the page wakes, renewed once on a `401`, and the honeypot's value in the body (moved here from 7.1e: since ADR-0138 no send passes without a challenge, WORKLOG 2026-11-01);
    - `jfb:submitting`, `jfb:submitted` and `jfb:error` (§4.9);
    - the page after a send of the form's placement (the picker's `redirect`, ADR-0129): today it reaches the pipeline only in the post's encrypted route token, which the entries endpoint's body does not carry, so a send with JavaScript carries it too, protected as the token protects it, and goes there (found in 6.8's review).
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before building the send states (ADR-0019, ADR-0020).
  - Done when:
    - Vitest covers the runtime's part, the submission id included;
    - an integration test and Playwright show a send with JavaScript from a picked form going to the picker's page after a send, and a value a visitor changed refused;
    - Playwright on the demo covers submission (the outcome, the error summary from a `422` with its focus, the server's errors in place), and the send states (a second press during a slow send sends nothing; a `429` and a `500` show the notice above the buttons, move the focus to it and keep every answer; a redirect outcome with "Go now" and "Stay here");
    - Playwright shows a submission from a plain-HTTP origin, where `crypto.randomUUID()` does not exist;
    - axe is clean in every send state; the size check passes with every chunk.
- [x] **6.4j `form_changed`** (6.4 split in nine, WORKLOG 2026-10-30; one letter on as 6.4a, 6.4b, 6.4c, 6.4d, 6.4f and 6.4g split in two, WORKLOG 2026-10-30) (2026-11-01, b0442d4)
  - **Resumes (2026-11-01):** the owner raised the budget to 60 KB (ADR-0146); this step resumes at 7.4i, with every check below.
  - **Blocked (2026-10-30):** `form_changed` is the answer of a send, which 6.4i builds, and it adds to the website runtime past section 4.12's budget, which only the owner can change (ADR-0120, `docs/OWNER-TODO.md`).
  - `form_changed` handling with answer restore (§3.1), through the `render` endpoint, leaving a consent whose wording changed unticked (ADR-0004).
  - Done when: Vitest covers the runtime's part; Playwright shows `form_changed` on a page served stale from the output cache (the published form swapped in, the answers kept, and a consent whose wording changed left unticked with a notice); axe is clean with the notice; the size check passes with every chunk.
- [x] **6.4k CSP, cookies and chunk names** (6.4 split in nine, WORKLOG 2026-10-30; one letter on as 6.4a, 6.4b, 6.4c, 6.4d, 6.4f and 6.4g split in two, WORKLOG 2026-10-30; 6.4k split in two, WORKLOG 2026-10-30) (2026-10-30, d567886)
  - **The constraints of ADR-0004,** checked on the runtime as 6.4a to 6.4h built it: the CSP nonce, no cookies, and chunk file names with a content hash (split off 6.4l, whose code waits for the runtime's budget, ADR-0120).
  - Done when:
    - Playwright shows no CSP violation with `script-src 'self'` on a cached page and with a nonce on an uncached one, lazily loaded chunks included;
    - Playwright shows the page with no cookie set;
    - a test shows every file of the runtime's build but its entry named with a content hash;
    - axe is clean; the size check passes with every chunk.
- [x] **6.4l Fitting the page and the developer surface** (6.4 split in nine, WORKLOG 2026-10-30; one letter on as 6.4a, 6.4b, 6.4c, 6.4d, 6.4f and 6.4g split in two, WORKLOG 2026-10-30; 6.4k split in two, WORKLOG 2026-10-30) (2026-11-02, 6c8df99)
  - **Resumes (2026-11-01):** the owner raised the budget to 60 KB (ADR-0146); this step resumes at 7.4i, with every check below.
  - **Blocked (2026-10-30):** the ground check, step folding, the JS API and its events add to the website runtime past section 4.12's budget, which only the owner can change, and the API's `submit()` sends through 6.4i, which waits with it (ADR-0120, `docs/OWNER-TODO.md`).
  - **Fitting the page** (ADR-0022, amended in 2.4): the ground check (a form without a scheme class, on itself or on an ancestor (ADR-0106), on a dark ground gets `jfb-scheme-dark`; a class the site set is never changed) and step folding (`data-jfb-compact` when a word of a step name is wider than its step, after layout and on every resize).
  - **The developer surface:** the JS API and DOM events (§4.9). The API's `next()` and `back()` and the `jfb:page` event come with the paging in 6.5, and `consent.grant('captcha')` and `withdraw()` with the captchas in 7.2 (moved in the split of 6.4).
  - Done when:
    - Vitest covers the API, the events, the ground check and step folding;
    - Playwright shows the ground check (an unclassed form on a dark page gets `jfb-scheme-dark` and passes contrast, a form the site set to light stays light, and a light page changes nothing) and step folding (a six-step German form folds where a name would break inside a word and unfolds when the window grows);
    - Playwright covers `init(root)`, `get(formEl)` with `validate()`, `submit()` and `values()`, and every DOM event of 6.4;
    - axe is clean; the size check passes with every chunk.
- [x] **6.4m Uploads** (6.4 split in nine, WORKLOG 2026-10-30; one letter on as 6.4a, 6.4b, 6.4c, 6.4d, 6.4f and 6.4g split in two, WORKLOG 2026-10-30; one letter on again as 6.4k split in two, WORKLOG 2026-10-30) (2026-11-02, d6d660c)
  - **Resumes (2026-11-01):** the owner raised the budget to 60 KB (ADR-0146); this step resumes at 7.4i, with every check below.
  - **Blocked (2026-10-30):** uploads add to the website runtime past section 4.12's budget, which only the owner can change, and an upload is sent with the entry that 6.4i sends, which waits with it (ADR-0120, `docs/OWNER-TODO.md`).
  - **Uploads with progress,** loaded on demand: against `uploads`, the challenge part first; size and type are checked in the browser before sending, with signature test vectors shared with the server; a reset during an upload is a failed upload the visitor can retry (ADR-0006). The upload's own controls follow 2.4's design (`data-jfb-enhanced`, "Choose files", "Take a photo" on touch screens, a file dragged over, each file's progress and state).
  - **The file field's `thumbnails` and `camera` settings** (added in 6.8i by the Phase 6 wiring sweep): nothing reads them yet. `FileControlModel` and the markup carry both; an image shows its thumbnail only with `thumbnails` on, and "Take a photo" shows only with `camera` on.
  - **Design skills:** load the three skills before building the upload's controls (ADR-0019, ADR-0020).
  - **Refused for now** (added in 7.4h, ADR-0148): an upload answered `503 uploads_full` or `429 rate_limited` says at its field to try again later, and keeps the other answers.
  - Done when: Vitest passes the signature vectors, which NUnit passes against the server's checks too; Playwright shows an oversize file and a renamed `.exe` refused before any upload request is sent, an upload cut off by the server reported as a failed upload with a retry, a file uploaded with its progress and sent with the entry, and each of `thumbnails` and `camera` changing what shows when turned off; axe is clean; the size check passes with every chunk.
- [x] **6.4n The signature pad** (6.4 split in nine, WORKLOG 2026-10-30; one letter on as 6.4a, 6.4b, 6.4c, 6.4d, 6.4f and 6.4g split in two, WORKLOG 2026-10-30; one letter on again as 6.4k split in two, WORKLOG 2026-10-30) (2026-11-02, 271fcd6)
  - **Resumes (2026-11-01):** the owner raised the budget to 60 KB (ADR-0146); this step resumes at 7.4i, with every check below.
  - **Blocked (2026-10-30):** the pad adds to the website runtime past section 4.12's budget, which only the owner can change, and each signature is sent through 6.4i's sending, which waits with it (ADR-0120, `docs/OWNER-TODO.md`).
  - **The signature pad,** loaded on demand, with the typed alternative, whose PNG carries the name typed in its `jfb:typed-name` text chunk as the server's does (ADR-0103, added in 6.2k); drawn by pointer, touch and pen, cleared and drawn again.
  - **Its error marks** (added in 6.8c, WORKLOG 2026-10-31): as the runtime shows a signature's error it marks the typed name's input `aria-invalid` as the server's view does, since the group's fieldset carries no invalid state since 6.8c, and the themes draw the pad's error border from that mark (`.jfb-signature:has([aria-invalid='true'])`).
  - **A form with a signature takes its files on the page** (added in 6.4m, ADR-0152): `takesUploads` left such a form to the post until the pad's answer could be sent from the page; this step lifts that, with a test.
  - **The page checks what it takes** (found here, ADR-0152): a required file or signature the page takes shows its error with the others at the first press, a file on its way counts as an answer, and a send stops at a file that failed, in this step's tests.
  - **Design skills:** load the three skills before building the pad (ADR-0019, ADR-0020).
  - Done when: Vitest shows the typed alternative's PNG reads back on the server as the server's own (its text chunk, its size and its pixels' limits), and that a refused signature's typed name is marked as the server marks it; Playwright draws a signature, clears it, types one instead and sends each; axe is clean; the size check passes with every chunk.
- [x] **6.4o The other field behaviours** (6.4 split in nine, WORKLOG 2026-10-30; one letter on as 6.4a, 6.4b, 6.4c, 6.4d, 6.4f and 6.4g split in two, WORKLOG 2026-10-30; one letter on again as 6.4k split in two, WORKLOG 2026-10-30) (2026-11-02, 912ba50)
  - **Resumes (2026-11-01):** the owner raised the budget to 60 KB (ADR-0146); this step resumes at 7.4i, with every check below.
  - **Blocked (2026-10-30):** the behaviours add to the website runtime past section 4.12's budget, which only the owner can change, and each is sent and stored through 6.4i's sending, which waits with it (ADR-0120, `docs/OWNER-TODO.md`).
  - **Loaded on demand:** the repeater (rows added and removed in place, their names and ids as the server's, the focus kept), character counters, choice cards and the "Other" input.
  - **A cascading field's options** (ADR-0085, ADR-0086): asked of the `options` endpoint when its parent's answer changes, with the page's content key, which the markup then carries; an answer no longer offered is cleared (added in the split of 6.4a).
  - The ranking field's move buttons with live announcements, and the grid's stacked layout on phones.
  - **A form with a repeater or a ranking takes its files on the page** (added in 6.4n, ADR-0152): `takesUploads` leaves such a form to the post while their buttons post; this step lifts that with a test, and a refused file in a row names its row where `messageOf` words it (it passes `row: null` today).
  - **A text area's `autoGrow` setting** (added in 6.8i by the Phase 6 wiring sweep): `TextAreaControlModel` carries it and nothing reads it. A text area with it on grows with its text, and one with it off keeps its rows.
  - **Design skills:** load the three skills before building these behaviours (ADR-0019, ADR-0020).
  - Done when: Playwright on the demo covers every field type's interaction that 6.4b to 6.4n did not (the repeater's rows at their limits, a counter past its limit, a card chosen, "Other" with its text, a text area growing with `autoGrow` on and keeping its rows with it off, a ranking moved by its buttons with the announcement, the grid answered on a phone, a cascading field's options following its parent), each sent and stored as the server reads it; axe is clean; the size check passes with every chunk.
- [x] **6.4p The kitchen sink by keyboard, and the runtime's review** (6.4 split in nine, WORKLOG 2026-10-30; one letter on as 6.4a, 6.4b, 6.4c, 6.4d, 6.4f and 6.4g split in two, WORKLOG 2026-10-30; one letter on again as 6.4k split in two, WORKLOG 2026-10-30) (2026-11-02, 019c028)
  - **Resumes (2026-11-01):** the owner raised the budget to 60 KB (ADR-0146); this step resumes at 7.4i, with every check below.
  - **Blocked (2026-10-30):** it reviews the states that 6.4i, 6.4j and 6.4l to 6.4o add, all waiting for section 4.12's budget, which only the owner can change (ADR-0120, `docs/OWNER-TODO.md`).
  - **The review:** the three skills review the runtime's states on the demo (errors, busy, the notices, the redirect, `form_changed`, uploads, the signature, rows and moves) at 375 and 1440 px, light, dark and right to left, in `docs/ux/review-6.4.md` with a section per skill (ADR-0019, ADR-0020).
  - Done when:
    - `docs/ux/review-6.4.md` has a section per skill with its output, findings and what changed;
    - a **keyboard-only** run completes the kitchen-sink form;
    - axe is clean in every state the review shows;
    - Playwright shows no CSP violation with `script-src 'self'` on a cached page and with a nonce on an uncached one with every chunk loaded;
    - the size check passes with every chunk;
    - the whole website suite passes in one run.
- [x] **6.5a The summary page and the journeys** (6.5 split in four, WORKLOG 2026-10-30) (2026-10-30, e8eb6f5)
  - **Moving through pages** as a post moves (ADR-0100): back without losing answers, each page checked on the server before the next, and the progress styles (the steps, the bar, none) on every page the rules show.
  - **The summary page** ("check your answers", §4.3.4): before sending, a page of every answer the rules leave shown, grouped by page, with a "Change" for each that opens its page with every answer kept, and the send from there.
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before building the summary page (ADR-0019, ADR-0020).
  - Done when: Playwright journeys pass for multi-page forms and the summary page; a three-page journey passes with `dir="rtl"`; axe is clean on every page of them.
- [x] **6.5b A copy of the answers** (6.5 split in four, WORKLOG 2026-10-30) (2026-10-30, 6288e0e)
  - **Keep a copy** (added in 1.1): the outcome option of §4.3.4 shows the submitted answers with a print button and a "fill in again" link, reusing the summary page's rendering:
    - only in the response to the submission: after a no-JS post, the redirect carries a short-lived, single-use signed token, never answers in the URL;
    - never cached (`no-store`), sensitive values masked, print styles only.
  - **Design skills:** load the three skills before building the copy and its print styles (ADR-0019, ADR-0020).
  - Done when: Playwright shows the copy of the answers after a no-JS submission and in print media (`emulateMedia`), and a reused or expired token shows nothing; axe is clean on the copy.
- [x] **6.5c Prefill** (6.5 split in four, WORKLOG 2026-10-30) (2026-10-31, a36ab8f)
  - **Prefill:**
    - from members, with "show" and the tamper-proof "hide" mode;
    - from allowed query values;
    - from context values.
  - Save and resume needs the email outbox, so it comes in 8.6.
  - Done when: a tamper test proves that a posted value for a hidden member-prefilled field is ignored; Playwright shows a field prefilled from the signed-in member, from an allowed query value and from a context value, and a query value the form does not allow left out.
- [x] **6.5d Moving through pages in the browser** (6.5 split in four, WORKLOG 2026-10-30) (2026-11-02, c3f789b)
  - **Resumes (2026-11-01):** the owner raised the budget to 60 KB (ADR-0146); this step resumes at 7.4i, with every check below.
  - **Blocked (2026-10-30):** paging in the browser adds to the website runtime past section 4.12's budget, which only the owner can change, and the copy after a JS submission comes from 6.4i's sending, which waits with it (ADR-0120, `docs/OWNER-TODO.md`).
  - **Moving through pages in the browser:** each page checked on the server (`validate`) before the runtime moves on, the JS API's `next()` and `back()` and the `jfb:page` event (§4.9), moved from 6.4 in its split.
  - **The copy of the answers after a JS submission,** in the outcome that 6.4i shows in the form's place.
  - **A form of several pages takes its files on the page** (added in 6.4m, ADR-0152): an upload's token names its challenge, which a post cannot claim, so such a form posts its files, and takes a signature only typed, until the pages move here (ADR-0153).
  - Done when: Playwright shows the copy of the answers after a JS submission; Playwright moves through a three-page form with `next()` and `back()` and sees `jfb:page` on each move (added in the split of 6.4); axe is clean; the size check passes with every chunk.
- [x] **6.6a The definition's types** (6.6 split in four, WORKLOG 2026-10-31) (2026-10-31, acbea4d)
  - **The definition:** the endpoint with `ETag` (built in 6.1b; its `304` tests stay), and TypeScript types for the public projection, generated from the public OpenAPI document (or C#, or a JSON schema) and tested for drift, shipped in the package and in `schemas/`; the runtime's and the logic's hand-written projection types are checked against them, so a change to the projection fails the build until they follow.
  - **Port 5521** is the static page's (§4.11): the e2e refusal hosts move off it.
  - Done when: the drift test fails on a changed projection and passes on the committed types; a pack test finds the types in the package and `schemas/` holds them; the runtime and logic type-check against them; the definition endpoint's `ETag` and `304` tests pass; nothing in the e2e support uses 5521 but the static page.
- [x] **6.6b Server-key submissions** (6.6 split in four, WORKLOG 2026-10-31) (2026-10-31, 6db4e41)
  - **Server-key submissions** (§4.6, ADR-0012): `Authorization: Jfb-Key <key>` against the configured names and SHA-256 hashes, compared in fixed time; such a submission skips the browser challenge, the captcha and the honeypot (each as it arrives in 7.1 and 7.2), never validation, the content rules or the domain limits; it is limited per key, records the key's name as `jfbServerKey`, which `developer.contextValues` cannot list, and the receipt endpoint accepts the key that submitted.
  - Done when: server-key tests pass (a right key, a wrong one, a key for a hash of another length, the fixed-time comparison, the partition a key's requests count in, which 7.1's limiter applies, the recorded name, the receipt, a context name starting with `jfb` refused, a key never logged).
- [x] **6.6c The Content Delivery API** (6.6 split in four, WORKLOG 2026-10-31) (2026-10-31, 5a64ea7)
  - **The Content Delivery API:** the form picker returns `{key, alias, name}`, and with expansion, the projection. This step builds the picker's property editor on the server, its value converters and its Delivery API output; 6.7 builds its data type's settings, its backoffice picker, blocks and "used on".
  - Done when: the Delivery API test passes (a page that picks a form answers `{key, alias, name}`, with expansion the projection in the request's culture, a form not published or not headless answers none).
- [x] **6.6d The element endpoint** (6.6 split in four, WORKLOG 2026-10-31; then 6.6d in four, WORKLOG 2026-10-31) (2026-10-31, 6b3fcbb)
  - **The renderer's JSON** (ADR-0128): `GET forms/{key}/element` (§4.6) answers the view model of the published form's first render, as the render endpoint renders it, as JSON with a `contract` number and the definition endpoint's answer as the exact text of the model data, without what only a post without JavaScript uses (the action, the encoding, the route token and post values, a button's `formaction`); `no-store`. Its JSON schema comes from the view models' own serializer metadata and the element's TypeScript types from the schema, both drift-tested (as ADR-0125).
  - Done when: integration tests cover the endpoint's answer for a multi-page form and for a field of every built-in type (each control's `kind`, the ids below the instance asked for, the model data equal to the definition endpoint's answer, no post-only part), a form for members (its notice for a visitor, the form for a member), a closed form's notice, the culture and the theme asked for, `400` for each malformed parameter, `404` for a form the site lacks or that does not allow headless use (as the definition endpoint answers it) and `no-store`, every answer fitting the schema; the OpenAPI snapshot holds the endpoint with its limits; the schema and the types pass their drift tests.
- [x] **6.6e The element's markup** (6.6 split in four, WORKLOG 2026-10-31; then 6.6d in four, WORKLOG 2026-10-31) (2026-10-31, a180fe7)
  - **The renderer:** `<jfb-form>` (packages/element, with `api`, `form`, `culture`, `content`, `theme` and `context-<name>` attributes) fetches the element endpoint and writes the built-in views' markup from it in the light DOM, the same markup as the server's views for the same form (a fixture of whole forms that the integration tests write and the element's tests read), adds the theme's stylesheets and the runtime once per document, and dispatches `jfb:rendered`.
  - Done when: the element's markup matches the server's for every form of the fixture (a field of every built-in type, a multi-page form, the notices, in the default and `bootstrap5` themes, in English and German), compared as DOM trees without the server's post-only parts; ADR-0015's contract test passes on the element's output; Vitest covers the attributes, the instance ids, the contract check and the assets added once.
- [x] **6.6f The element on a headless page** (6.6 split in four, WORKLOG 2026-10-31; then 6.6d in four, WORKLOG 2026-10-31) (2026-10-31, e878d5a)
  - **The static site on 5521** (ADR-0011, ADR-0031): the browser harness serves a static page there, cross-origin to the host and allowlisted, that loads the element from the host.
  - Done when: a static page on 5521 (cross-origin, allowlisted) renders a multi-page form through the element, with the honeypot out of view and out of the accessibility tree, and the runtime enhances it as on a Razor page (a rule shows a field, a check shows its error); a page on an origin that is not allowlisted renders nothing and says why in the console; axe is clean.
- [x] **6.6g The element's pages and sending** (6.6 split in four, WORKLOG 2026-10-31; then 6.6d in four, WORKLOG 2026-10-31) (2026-11-02, 3996aff)
  - **Resumes (2026-11-01):** the owner raised the budget to 60 KB (ADR-0146); this step resumes at 7.4i, with every check below.
  - **Blocked (2026-10-31):** the element's form moves through its pages and is sent by the runtime's paging (6.5d) and sending (6.4i), which wait for section 4.12's budget, which only the owner can change (ADR-0120, ADR-0128, `docs/OWNER-TODO.md`).
  - **Through the runtime:** the element's form is moved through its pages in the browser, each page checked on the server (`validate`) before the next, and sent, by the runtime's paging (6.5d) and sending (6.4i).
  - **`form_changed` on the element** (added in 6.4j, ADR-0004): it fetches its form again with the answers kept; until then the runtime shows the notice of a failed send there, as a form with no address cannot post.
  - **Its files** (added in 6.4m, ADR-0152): the element writes the same file field, so a file goes up from its form as from a page's.
  - Done when: a static page on 5521 (cross-origin, allowlisted) renders and submits a multi-page form through the element, each page checked with `validate` before the next, a file going up and being sent with the entry; axe is clean.
- [x] **6.7a The form picker's settings** (6.7 split in five, WORKLOG 2026-10-31) (2026-10-31, abc6a27)
  - **The data type "Jiron Form Builder form picker":** single or multiple, allowed folders or forms, optional theme override and redirect override. Plus the value converters (Razor and the Content Delivery API, one form or several, with the overrides) for each setting; a value the settings do not allow is refused when the page is saved.
  - Done when: integration tests cover each setting: one form and several in Razor and in the Delivery API; a form in an allowed folder (or below it) accepted and one outside refused; an allowed form accepted and another refused; several forms refused by a single picker; the theme override rendering the form in that theme; the redirect override taking the visitor to its page after a send without JavaScript (with JavaScript in 6.7e, which has a demo page with the picker).
- [x] **6.7b "Create a form block for me"** (6.7 split in five, WORKLOG 2026-10-31) (2026-10-31, a4ff7f2)
  - **"Create a form block for me":** an idempotent service that creates a "Form" element type and data type, and never changes existing types; its endpoint is "Setup and health" in §4.7.
  - Done when: integration tests pass: the element type and the data type created on the first call, a second call changing nothing, an existing type or data type with the same alias left as it is, and "creating a form block type is refused with `403` for a user who has `manageSettings` and the Forms section but not Umbraco's Settings section", a user seeded in that test.
- [x] **6.7c "Used on"** (6.7 split in five, WORKLOG 2026-10-31) (2026-10-31, 6543134)
  - **"Used on":** reference tracking per ADR-0017, with the references endpoint of §4.7, so that trash and delete can name the pages that use a form (9.2). References name a form without a foreign key (ADR-0033), so a reference write skips a form that no longer exists (ADR-0047).
  - Done when: integration tests pass, including "a page that picks the form is listed as a reference, and stops being listed after the pick is removed", "saving a page that picks a form deleted meanwhile writes no reference to it", a form picked in a block listed, a page outside the user's start nodes counted but not named, and the rebuild finding every page that picks a form.
- [x] **6.7d Eviction** (6.7 split in five, WORKLOG 2026-10-31) (2026-10-31, 2e081fc)
  - **Eviction:** an `IWebsiteOutputCacheTagProvider` tags the pages that use a form with `jfb-form-<key>` from the "used on" index, and publishing a form evicts that tag (ADR-0004).
  - Done when: integration tests pass, with output caching on, including "publishing the form re-renders a cached page that picks it at once", and a cached page that does not pick the form kept.
- [x] **6.7e The picker's endpoints** (6.7 split in five, WORKLOG 2026-10-31; then 6.7e in three, WORKLOG 2026-10-31) (2026-10-31, 09d41ce)
  - **The endpoints** the picker and its data type settings call (§4.7 "Content"): the library's folders and forms level by level, a search, the forms of given keys, and the site's themes. They are authorised by content access, not by Jiron Form Builder permissions (ADR-0013): any backoffice user with Umbraco's Content, Media, Members or Settings section may call them, the Forms section not needed. They answer names and keys only: published forms outside the trash that are not restricted, plus restricted ones a group of the user holds a grant on, and their folders; for a data type, only within its allowed folders (and below) and allowed forms. The permission matrix lists them as its reviewed exception, each with its own access test.
  - Done when: integration tests pass: an editor with the Content section and without the Forms section lists, searches and looks up published forms that are not restricted and restricted ones their group holds a grant on, and never an unpublished, trashed or other restricted form; a data type's allowed folders (and below) and allowed forms limit every answer; the themes are those the data type's settings accept; a user with none of the four sections is refused with `403`; the permission matrix names exactly these endpoints as its exception; the OpenAPI snapshot and the regenerated client are updated.
- [x] **6.7f The picker in the backoffice** (6.7e split in three, WORKLOG 2026-10-31; then 6.7f in two, WORKLOG 2026-10-31) (2026-10-31, 39889b6)
  - **The backoffice:** the picker itself (the library's forms and folders, within the allowed ones) through the generated client, with the UFM block-label component; English and German.
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before building it (ADR-0019, ADR-0020).
  - Done when: Vitest covers the picker (one form and several, the folders level by level, the search, only the allowed ones, a picked form that is gone, read-only) and the block label (the form's name, and a form that is gone); the English and German keys are held by the language test; the picker is looked at in Edge.
- [x] **6.7g The picker's settings in the backoffice** (6.7f split in two, WORKLOG 2026-10-31) (2026-10-31, f9b2829)
  - **The backoffice:** the picker's data type settings (one form or several, the allowed folders and forms on the picker's own sidebar, the theme from the site's themes, the page after a send through Umbraco's document picker) through the generated client; English and German. The allowed folders are shown by name, so an endpoint beside 6.7e's names folders by key, with the same access rule (ADR-0133).
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before building it (ADR-0019, ADR-0020).
  - Done when: Vitest covers its settings (one form or several, the allowed folders and forms, the theme, the page after a send); integration tests cover the folders endpoint (the folders a user may see, by key, and never a trashed one or a restricted one without a grant; the access rule of 6.7e) and the permission matrix names it in its exception; the OpenAPI snapshot and the regenerated client are updated; the English and German keys are held by the language test; the settings are looked at in Edge.
- [x] **6.7h The picker on the demo** (6.7e split in three, WORKLOG 2026-10-31; 6.7g until 6.7f split in two, WORKLOG 2026-10-31) (2026-10-31, d36fcb0)
  - **The demo:** a page type with a block list that allows the form block (6.7b), a page with the block, and a thanks page for the picker's page after a send.
  - Done when: Playwright shows an editor picking a form in a block on the demo and the page rendering it, and the form sent with JavaScript going to the picker's redirect page; axe is clean on the picker and on its settings.
- [x] **6.8a The review's fixes to the public API and the post** (C# (6.8 split in five, WORKLOG 2026-10-31)) (2026-10-31, f2114d3)
  - **What the phase review found** (the security pass over the public API, and the reader of rendering and the post without JavaScript; amend the ADRs named):
    - the element endpoint passes every `context-<name>` query name to the rendering, whose process-wide log-once set keeps each name it has not seen with a warning, so anyone grows the server's memory and log without bound: names the form does not list are dropped before rendering, and no set keeps a name a request chose;
    - a receipt asked without a server key answers an entry a server key made, which ADR-0126 gives that key alone;
    - the entries endpoint checks unique answers beside every other error, so a post that stores nothing tells whether an address is taken: the check runs once everything else passes (amends ADR-0091), on the post without JavaScript too;
    - a post without JavaScript stores its files before the form's availability is checked, so a closed or full form keeps them: availability first, nothing stored when it refuses (ADR-0087's rule);
    - a post from another origin to a form for anyone, with a signed-in member's cookie, is stored as that member's entry with their hidden prefilled values: while a member is signed in, a post whose `Origin` is present and foreign is refused, as the public API refuses it (amends ADR-0100);
    - the server keys are read again on every request (`IOptionsMonitor`), while every other host option is read once and checked at start, as the schema says: read and checked at start;
    - §4.8's host configuration names `Rendering:IncludeAssetsAutomatically`, which no option reads, so the site refuses it at start: the option is built, the default of `<jfb-render assets>` and the view component, with its schema entry and docs.
  - Done when: integration tests pass for each: 1,000 distinct unlisted context names write at most one warning for the form and keep no name, and a listed one still renders; a receipt without a key for an entry a key made answers `404 not_found`; a post with an empty required field and a taken unique answer answers `422` with the required error only and stores nothing, on both ways of sending; a post with a file to a closed form and to a full one stores no file; a foreign-origin post with a signed-in member answers `403` and stores nothing, while one without an `Origin` and an anonymous one still pass; a changed server key needs a restart and an invalid one stops the start; with `IncludeAssetsAutomatically` off a placement writes no assets unless it asks for them, and §4.8's `Rendering` section starts a site.
- [x] **6.8b The review's fixes to the website runtime** (TypeScript (6.8 split in five, WORKLOG 2026-10-31)) (2026-10-31, d3f7ece)
  - **What the phase review found** (the reader of the runtime and the element, and the accessibility pass):
    - an error the server showed at a field the visitor has not changed refuses every later send, also when the runtime's own checks pass, against ADR-0113 (an optional upload the server refused, now empty, can never be sent): the kept errors join only a send the runtime already refuses;
    - filling a default writes its text into every text control of the field, a choice's "Other" box, a name's or an address's parts and an email's repeat among them, which the server never does, so an entry can store an "Other" answer the visitor took back: only the control named as the field is filled;
    - a text block that recalls an answer through `| html` stops following it once its line breaks change: line breaks are rebuilt, and no other element is taken;
    - for a member whose own value stands in for a field the server left out (ADR-0124), the runtime refuses no send at all: errors at a field with no control in the form are dropped, the others still refuse;
    - each calculation's `<output>` is a live region that speaks on every key: results speak once the visitor pauses, in one line;
    - a choice group's error state sits on its fieldset, whose role does not support it: moved to 6.8c with its check (WORKLOG 2026-10-31), as `checks.fixture.test.ts` holds the runtime's marks to the server's page, so the runtime and the views change in one step.
  - Done when: Vitest covers each: a send with only a kept server error at an unchanged field posts, and a refused one still lists it; a default from the query leaves a choice's "Other" text and every part of a name empty; a recalled answer that gains and loses a line break follows it, and takes no other element; with a required field missing from the page and another empty, a send is refused and shows only the other's error; typing three characters into a calculation's input announces its result once; Playwright's errors and calculations specs still pass, and the size check passes.
- [x] **6.8c The group's marks and the required text** (views, element and runtime (6.8 split in five, WORKLOG 2026-10-31; 6.8c split in three, WORKLOG 2026-10-31)) (2026-10-31, c0e5148)
  - **What the phase review found** (the accessibility pass):
    - with the asterisk style, a required checkbox group, checkbox grid row or ranking tells a screen reader nothing, as its controls cannot carry `required` and the mark is hidden: the mark's meaning is written as hidden text there;
    - a choice group's error state sits on its fieldset only, whose role does not support it: the views give its controls `aria-invalid` and the error's id, and the runtime marks the group's own controls (`aria-invalid`, `aria-describedby`) as it shows an error and clears them as it takes it away (moved from 6.8b, WORKLOG 2026-10-31), the fixtures written again;
  - Done when: each finding has its test: the hidden "required" text read for a checkbox group, grid row and ranking (an integration test of the views, and the element's markup equal to the server's); `aria-invalid` on a refused group's controls; Vitest: a refused choice group marks its controls and clears them when answered (moved from 6.8b), and `checks.fixture.test.ts` still holds the runtime's marks to the server's; the fixtures written again; the size check passes; and the refused groups are looked at in Edge.
- [x] **6.8d The themes' focus, scroll margin and error borders** (CSS and C# (6.8 split in five, WORKLOG 2026-10-31; 6.8c split in three, WORKLOG 2026-10-31)) (2026-10-31, a44a028)
  - **What the phase review found** (the accessibility pass):
    - in `bootstrap5` the theme-drawn controls (rating, scale, yes or no, choice cards, the ranking's moves) show focus only as a faint shadow, about 1.4:1, which forced colours drop: an outline that holds 3:1 in both schemes and survives forced colours;
    - a link of the error summary can leave the field and its label under a site's sticky header: the fields, labels, legends and controls keep `--jfb-scroll-margin`;
    - a refused scale, and a refused rating in `default`, show no error border: the rules match their markup;
    - `IFormBuilderTheme.DarkScheme` and `Tokens` are declared but nothing reads them: wired, or the step that reads them named in ADR-0105.
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before changing the themes (ADR-0019, ADR-0020).
  - Done when: each finding has its test (the outline measured at 3:1 or more and present in forced colours; the label in view after a summary link under a sticky header; the scale's and rating's error border; `DarkScheme` and `Tokens` read, or ADR-0105 naming the step that reads them), axe stays clean, the stylesheets stay within their budget and lint, and the themes are looked at in Edge.
- [x] **6.8e The keyboard and screen-reader-name pass** (Playwright (6.8 split in five, WORKLOG 2026-10-31; 6.8c split in three, WORKLOG 2026-10-31)) (2026-10-31, 0f473be)
  - **The keyboard and screen-reader-name pass** (Playwright, every theme, both schemes, forced colours where it applies): a visible focus indicator on every kind of control; the keyboard alone through rating, scale, yes or no, cards, the switch, the slider, the ranking, repeater rows and the summary's links, Enter sending; the accessible name, role, required and invalid state of every kind of control and group (grid cells, name and address parts, the "Other" input, the email repeat, the signature's typed name, help, affixes and counters); the summary's link landing with the field's label in view; `autocomplete` present on the personal fields; reduced motion stopping every slide and rise; right to left mirroring the arrows, the switch and the stars.
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before changing the themes (ADR-0019, ADR-0020).
  - Done when: the pass's Playwright specs pass in every theme and scheme, axe stays clean, and the themes are looked at in Edge.
- [x] **6.8f The review's fixes to the content integration** (C# and TypeScript (6.8 split in five, WORKLOG 2026-10-31); 6.8d until 6.8c split in three, WORKLOG 2026-10-31) (2026-10-31, 4ed8d16)
  - **What the phase review found** (the reader of the content integration):
    - "Used on" goes stale when a document type, an element type or a data type changes: removing a picker property, or turning a text property into a picker, changes stored values without a content notification: such a change asks for a rebuild (amends ADR-0131);
    - the rebuild writes each page of a batch from the copy it read before, so a save in between is overwritten with stale rows, and a rebuild asked for during a batch is lost: a page is written only when its rows are not newer, and the place moves on only from the value read;
    - the allowed folders' settings show a folder a user reached through a grant on a form inside it as not available, though the sidebar offered it: the folders endpoint names the folders that lead to the user's forms too;
    - a picker value whose keys are written in another format than the hyphenated one renders on the site but shows as not available in the backoffice and can be picked twice: the backoffice reads every format the server reads, and writes the hyphenated one;
    - the settings take more allowed forms than the picker's queries can carry (1,000), after which every call of that picker fails: the settings refuse more, folders and forms alike.
  - Done when: integration tests pass for each: a picker property removed from a document type, and a data type changed into the picker, leave "used on" right after the rebuild; a save between the rebuild's read and its write keeps the save's rows, and a rebuild asked for meanwhile still runs; a group with a grant on a form only gets that form's restricted folder from `picker/folders`; 1,001 allowed forms are refused; and Vitest shows a value in another key format named and marked chosen.
- [x] **6.8g The second reading's fixes to the post, the runtime and the rebuild** (C# and TypeScript (6.8g split in three, WORKLOG 2026-10-31)) (2026-10-31, d98bcb3)
  - **What the second reading found:**
    - a signed-in member's own post without JavaScript is refused on a page with `Referrer-Policy: no-referrer`, which posts with the origin "null": it counts as the site's own only when the browser also says `Sec-Fetch-Site: same-origin`;
    - the live region stays silent for the first answer's result when the runtime's chunk comes after that answer, and it says results on pages the visitor cannot reach;
    - a grid's marks stay on rows no longer in error when its error moves, and a kept server error of one row spreads to every row on a refused send;
    - one document that cannot be read stops the rebuild for good: it is logged and passed by;
    - in a cluster, a walk straight after a type's change can write from a server's old copies, which nothing repairs: the job holds a new request for one run;
    - a deleted language takes its values without a notification for the content: it asks for a rebuild;
    - the backoffice's key formats are narrower than .NET's: one fixture holds both sides to one list;
    - the rebuild's locking read and a data type's editor changed through Umbraco's own endpoint have no test.
  - The runtime stays within its budget, so these fixes make room for what they add (ADR-0119).
  - Done when: Vitest and integration tests pass for each, a tamper of each fix fails its test, and the runtime's size check passes.
- [x] **6.8h The second reading's fixes to the themes and the pass** (CSS and Playwright (6.8g split in three, WORKLOG 2026-10-31)) (2026-10-31, 5e2ff2a)
  - **What the second reading found in the themes:**
    - a picture choice's scroll margin can put the focused control below the window: the margin never leaves less than the control's own room in view;
    - the form itself has no scroll margin, so a post's landing puts its summary, title or outcome under a sticky header;
    - a refused rating stays in the error's colour after a star is chosen without JavaScript;
    - `bootstrap5`'s own controls show focus only as Bootstrap's shadow of about 1.4:1: they take the theme's outline too.
  - **What it found in the pass, which claims more than it checks:** the invalid state of five refused controls is read from a tree that reports it anyway, so the markup is what is checked, and the refused name is checked too; the honeypot check cannot fail; one changed pixel counts as a focus, with no control that two unfocused shots match, and Back, a row's Remove and the summary's links are not walked; the reduced-motion recorder adds no row and toggles no switch; Enter on a form with a ranking runs with JavaScript only; a lined-up check can pass on two missing controls; and Tab, the keyboard, reduced motion and right to left run in the light scheme only, against 6.8e's "every theme and scheme". Where a kind of control is optional on the specimen, its required and invalid state is held in the rendering tests, and the review says which.
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before changing the themes (ADR-0019, ADR-0020).
  - Done when: the specs hold each of these in every theme and scheme, a tamper of each fails them, axe stays clean, and the themes are looked at in Edge.
- [x] **6.8i Phase review** (6.8g split in three, WORKLOG 2026-10-31; 6.8 split in five, WORKLOG 2026-10-31; 6.8e until 6.8c split in three, WORKLOG 2026-10-31) (2026-11-01, 0239cea)
  - The second reading of 6.8a to 6.8f found what 6.8g and 6.8h fix; a reader of each of those two steps reads their fixes, and what it finds is fixed here.
  - A second reading of 6.8a to 6.8f's fixes, a reader per fix step, fixing what it finds: done by 6.8g and 6.8h with the readers' reports in the WORKLOG.
  - **The wiring sweep's doc work:** the docs list `jfb:rendered`, `<jfb-form>`'s `content`, `theme`, `context-*` and `render()`, and `<jfb-render picked>` and `assets` wherever they list the element and the tag helper; `docs/architecture.md` says the rate-limit middleware comes with 7.1, and marks `EntrySubmitting`, `EntrySubmitted` and `IFormBuilderRenderer` as 15.1's; the element's console message names no registration that does not exist yet.
  - Write the handover note: the wiring sweep's inventory, and what waits for later steps (the general settings' screen in 12.4a, the references and setup endpoints' callers in 9.2 and 9.1, the public endpoints' callers in the blocked 6.4 steps, the rate limits in 7.1).
  - Retry every `[`]` step, and re-check `.private/`.
  - Done when: pushed, with the phase-review gate.

### Phase 7: Spam protection and file security

- [x] **7.1a The rate limits** (7.1 split in five, WORKLOG 2026-11-01) (2026-11-01, 107c9a0)
  - **Limits:** the ASP.NET Core rate limiter per IP, form and site, with bounded partitions; a server key's requests in their key's partition (6.6b, ADR-0126), with a test that a key's limit holds and does not touch the address's; origin checks.
  - No-JS posts are rate limited like every post (ADR-0012), and a test covers that case.
  - Done when:
    - integration tests cover each policy, and a flood from 20,000 distinct IPs neither bypasses the site-wide limit nor grows memory without bound;
    - a `429` keeps the visitor's answers and says when to send again on the no-JS page (Playwright); the runtime's part is 7.1e.
- [x] **7.1b The challenge** (7.1 split in five, WORKLOG 2026-11-01) (2026-11-01, 21e2297)
  - **Signals:**
    - a minimum fill time from the signed challenge (its token and issue time here; its score in 7.1c);
    - the `challenge` endpoint (§4.6), and as the first part of every upload, verified before the file is read (ADR-0006); its use by the runtime for JS posts is 7.1e;
    - the challenge id stored on the entry (unique per form), so a solved challenge can't be replayed.
  - **What cached markup cannot carry** (§4.6): the challenge response also brings the places left per option and the member values for rules. The server has refused full options since 6.1 either way; the runtime's use of them is 7.1e.
  - **Limits:** member-bound challenges on members-only forms (ADR-0004).
  - Done when:
    - integration tests cover the challenge's signature, expiry, form, member and renewal, on entries, validate, receipt and uploads;
    - a replay with the same body and its entry's own, already used challenge returns the original receipt and outcome, also once the form has closed or the option it took is full; the same `submissionId` and the same body with another challenge or server key gets `409` with neither receipt nor outcome;
    - `receipt` finds an entry by its challenge for up to 24 hours past that challenge's expiry and answers `404` after that; the runtime's retry is 7.1e.
- [x] **7.1c The signals and the score** (7.1 split in five, WORKLOG 2026-11-01) (2026-11-01, 25e2d44)
  - **Signals:**
    - a honeypot whose name varies per public revision (its markup and name rendered since 6.3a, ADR-0105; 7.1 reads and scores it);
    - a minimum fill time from the signed challenge;
    - no time signal on no-JS posts, because the markup of anonymous forms is cache-safe (§3.4). They are scored on the honeypot, the blocked ranges and the content rules, and a test covers that case.
  - **Limits:** the blocked address ranges of the spam settings (§4.8).
  - **Scoring:** the spam score with reasons, and silent acceptance into `Spam` (§4.4). A submission put into `Spam` holds no place and no unique answer, and neither check runs for it, so a bot learns nothing from `full` or from a taken answer (ADR-0090, ADR-0091).
  - Done when:
    - integration tests cover each signal;
    - tests cover a blocked IPv4 and IPv6 range;
    - a submission the protection puts into `Spam` is answered as accepted though its option is full and its unique answer is taken, and holds neither (ADR-0090, ADR-0091).
- [x] **7.1d Content rules and the spam folder** (7.1 split in five, WORKLOG 2026-11-01) (2026-11-01, 97d5d82)
  - **Content rules and the spam folder:**
    - Blocked words, blocked email domains, maximum links, per-form strictness, and `IFormBuilderSpamCheck`.
    - The `Spam` state. "Not spam" runs the submitted actions then; on a form with email verification, 8.5 sends an unverified entry to `Unverified` instead.
  - Done when: tests cover each content rule, a custom `IFormBuilderSpamCheck`, and "not spam" running the submitted actions.
- [x] **7.1e The challenge in the runtime** (7.1 split in five, WORKLOG 2026-11-01) (2026-11-02, 3a0d4ca)
  - **Resumes (2026-11-01):** the owner raised the budget to 60 KB (ADR-0146); this step resumes at 7.4i, with every check below.
  - **Blocked (2026-11-01):** the runtime stands 2 bytes below the 40 KB of §4.12, a budget only the owner can change, and this part adds to it and builds on the sending of 6.4i (ADR-0120, `docs/OWNER-TODO.md`). The server's parts are 7.1a to 7.1d.
  - **Signals:** the `challenge` endpoint's use by the runtime as the first part of every upload (its use for JS posts, the honeypot's value and the wait in a `429`'s notice moved to 6.4i, whose sends need them, ADR-0120; this step's checks stay as they are).
  - **What cached markup cannot carry** (§4.6): the runtime then shows the places ("3 places left", "full") and evaluates member conditions with them.
  - Done when:
    - a `429` keeps the visitor's answers and says when to send again, in the runtime (Playwright);
    - a runtime test shows a retry after a lost answer, whose challenge has expired, asking `receipt` and showing the original outcome, and, past the grace, the fresh post's `409` shown as already sent;
    - Playwright shows the places left changing on a cached page after another visitor's submission, and a member condition applied for a signed-in member.
- [x] **7.2a Proof of work on the server** (7.2 split in three, WORKLOG 2026-11-01) (2026-11-01, 64fac1a)
  - **Proof of work:** a SHA-256 puzzle with difficulty per level, a signed and expiring nonce: the challenge carries the nonce and the difficulty, and the server checks the answer once, with the challenge. A post without JavaScript on a form whose protection needs JavaScript scores into Spam with `no_script`, and a server key skips the puzzle (ADR-0012).
  - Done when:
    - integration tests cover a form at the proof-of-work level, directly and through the site's default: its challenge carries the puzzle, a right answer passes, and a wrong or missing one is refused;
    - tests cover the no-JS post's `no_script`, and a server key that skips the puzzle.
- [x] **7.2b Captcha providers on the server** (7.2 split in three, WORKLOG 2026-11-01) (2026-11-01, 8546aac)
  - **Third-party providers:** reCAPTCHA v2 and v3 (ported from Ligata.Forms) and hCaptcha, with their verification, their keys in host configuration only and the outcomes of ADR-0012. Providers are greyed out without keys. Publishing refuses a provider the site lacks (`unknown_type` at `settings.protection.captcha.provider`, ADR-0035).
  - **The captcha placement** (added in 5.1b): the `captcha` field type is not configured (5.1c's availability, "not configured: …") while no captcha provider has its keys, since it marks where a provider's captcha renders.
  - Done when:
    - the field types the designer reads give the captcha placement as not configured, with its reason, on a site where no provider has keys, and as available once one has;
    - unit tests pass against the HTTP stub.
- [x] **7.2c Proof of work and the captchas in the runtime** (7.2 split in three, WORKLOG 2026-11-01) (2026-11-02, ba2f3d0)
  - **Resumes (2026-11-01):** the owner raised the budget to 60 KB (ADR-0146); this step resumes at 7.4i, with every check below.
  - **Blocked (2026-11-01):** the runtime stands 2 bytes below the 40 KB of §4.12, a budget only the owner can change, and this part adds to it and builds on the sending of 6.4i (ADR-0120, `docs/OWNER-TODO.md`). The server's parts are 7.2a and 7.2b.
  - **Proof of work:** solved in a Web Worker with a progress hint for slow devices.
  - **Third-party providers:** the consent modes of §4.10 apply, with the JS API's `consent.grant('captcha')` and `withdraw()` for the `cmp` mode (§4.9, moved from 6.4 in its split).
  - Done when:
    - Playwright, with the provider hosts routed to a local stub (`page.route`, so no real third-party request ever happens, D22), shows **zero requests to them before consent** and the script loading after;
    - proof of work completes in under 2 s at the default level on the demo machine;
    - Playwright shows `consent.grant('captcha')` loading the provider's script on a form in the `cmp` mode, and none on the next page after `withdraw()` (added in the split of 6.4).
- [x] **7.3a Upload checks** (7.3 split in four, WORKLOG 2026-11-01) (2026-11-01, cc8ac17)
  - streaming limits;
  - the allowlist, the global blocklist and magic-number checks;
  - random names.
  - Done when: tests cover a polyglot file, a double extension and an oversize stream aborted early.
- [x] **7.3b Cleanup and scanning** (7.3 split in four, WORKLOG 2026-11-01) (2026-11-01, 266c600)
  - the orphan-cleanup job, and the scanner hook with quarantine.
  - Done when: tests cover a quarantined file and the cleanup of an orphan.
- [x] **7.3c Downloads** (7.3 split in four, WORKLOG 2026-11-01) (2026-11-01, 5cb544f)
  - protected and permission-checked, with SVG and HTML never served inline; ZIP exports safe against path traversal.
  - Done when: tests cover a zip-slip name, a download by a user with and without the permission and by an anonymous caller, and an SVG and an HTML file served as attachments.
- [x] **7.3d A refused upload over HTTP/2** (7.3 split in four, WORKLOG 2026-11-01) (2026-11-01, a6b18ba)
  - Done when: over HTTPS with HTTP/2, Playwright shows that a refused upload's answer reaches the browser (ADR-0006).
- [x] **7.4a The site that protects a form** (7.4 split in nine, WORKLOG 2026-11-01) (2026-11-01, ac18692)
  - **What the review found:** a request names its own page, so a visitor chooses whose protection applies: no page, or a page of the laxest site, escapes a stricter site's puzzle, captcha, blocked ranges and content rules on a form that inherits them.
  - A named page's site applies only when that page holds the form, by the reference index. Otherwise, a request with no such page gets the strictest of the shared settings and every site's: the highest level, and every range, word and domain.
  - A puzzle's answer counts only for a challenge asking at least the bits the site asks now.
  - Done when: tests cover a post with no page and one naming another site's page, on a form placed on a strict site, each held to that site's puzzle, captcha, ranges and words, directly and without JavaScript; a page that holds the form keeps its own site's settings; a challenge of fewer bits is refused; 14.4 says it keeps this.
- [x] **7.4b What the content rules read and the verdict's answer** (7.4 split in nine, WORKLOG 2026-11-01) (2026-11-01, c4b752f)
  - **What the review found:** the content rules skip a name's and an address's parts and a choice's own "other" text, so blocked words and links pass there.
  - **The verdict's answer:** a post with a taken or full answer is told so unless the protection marks it Spam, which tells a bot its verdict. The review of this finding keeps 7.1c's check, as hiding those answers from Spam is what stops silent probing of who has registered: an ADR says why, and what bounds the probing.
  - A `previous` challenge carries its issue time only for a limited age, so one old token cannot switch the time signal off for good.
  - Done when: tests cover a blocked word and a link in a name's part, an address's part and an "other" text, an old `previous` that no longer carries its time, and the ADR is written.
- [x] **7.4c The post without JavaScript** (7.4 split in nine, WORKLOG 2026-11-01) (2026-11-01, a503f37)
  - **What the review found:** an anonymous post stores files on a members-only form before its gate answers; the marker decides whose form limit a post counts against; a typed signature is stored under the read limit; a post without the marker has no file count; text values have no total limit; a refused post is still read to disk; reading has no time limit.
  - Done when: tests cover each: nothing stored for an anonymous post on a members-only form; a marker of another form still counts against the posted form; a typed signature counts as a submit; a post without the marker is held to the field's count; texts past the form's total are refused; a post the limits refuse writes no file; a post that trickles past its time is ended.
- [x] **7.4d The disk, the cleanup and the scanner** (7.4 split in nine, WORKLOG 2026-11-01) (2026-11-01, 8a889a1)
  - **What the review found:** one address can fill the disk with uploads no entry claims, as nothing caps their bytes and the upload policy has no hourly limit; the cleanup can fall behind what the limits allow in; a flood of unclaimed uploads holds a real entry's file behind it in the scan queue; a file that cannot be opened stalls the queue.
  - A host option caps the bytes waiting for an entry, and an upload past it is refused before it is stored. The upload policy gets hourly and per-form defaults. The cleanup keeps up on a timer of its own. A scan takes what an entry holds first, and a file that fails does not stop the others.
  - Done when: tests cover each: the cap with its refusal and its recovery, the new defaults, a backlog the cleanup clears past one run, an entry's file scanned ahead of unclaimed ones, and a file that cannot be opened passed by.
- [x] **7.4e The limits** (7.4 split in nine, WORKLOG 2026-11-01) (2026-11-01, 8e2b2a2)
  - **What the review found:** made-up form keys spend the site's limit and fill the windows; twelve addresses can shut one form for everyone; a form read by its alias has no per-form limit; the challenge's `429` has no test of its own; an invalid server key writes a warning with a path the caller chose, before any limit; a body's error list costs quadratic time and cannot be cancelled.
  - Done when: tests cover each: an unknown key costs no form window and no site count; the per-form default holds a flood and leaves a form open to a few addresses; an alias counts as its form; the challenge's `429` with `Retry-After`; the warning is bounded and names no path; a body of duplicate members answers within a time limit and with a bounded list.
- [x] **7.4f Permissions and what goes out** (7.4 split in nine, WORKLOG 2026-11-01) (2026-11-01, 6aaecee)
  - **What the review found:** a user with `design` and `viewEntries` can publish a field as not sensitive and read its files without `viewSensitive`; an archive cut off just before its end leaves no audit entry; `page.*` merge tags read a page protected from the visitor; the form endpoint serves a members-only form's whole definition; one trailing byte hides a ZIP behind another kind; a server key's uploads leave no trace of the key.
  - Done when: tests cover each: publishing or restoring a lower sensitivity refused without `viewSensitive`, in the permission matrix; the audit entry written before the first byte, with its counts after; a protected page treated as no page; `403 member_required` on the form endpoint; a ZIP with trailing bytes refused; the key's name logged on an upload.
- [x] **7.4g Wiring and docs** (7.4 split in nine, WORKLOG 2026-11-01) (2026-11-01, a4d56e7)
  - **What the sweep found:** the schema says every rate limit defaults to 0; blank captcha keys read as configured; no step names the endpoint that greys out providers without keys, or a reader of the daily `spam` metric; `FormBuilderSpamCheckContext.Page` and `Address` have no test; the blocked extensions' description is stale; `docs/architecture.md` and `docs/database.md` lag behind Phase 7.
  - Done when: a test reads the schema's defaults from `new FormBuilderOptions()`; tests cover blank keys and both context members; 10.1 and 11.5 name what they read; the docs name what Phase 7 stores and runs.
- [x] **7.4h Phase review** (7.4 split in nine, WORKLOG 2026-11-01) (2026-11-01, 4a1b457)
  - A security review of Phases 6 and 7 (STRIDE for the public surface; the readers' findings are 7.4a to 7.4g, and a reader of those fixes reads them here). Fix what it finds, with a test per fix.
  - Write the handover note, with the wiring sweep's inventory.
  - Done when: pushed, with the phase-review gate.
- [x] **7.4i Resume what was blocked** (7.4 split in nine, WORKLOG 2026-11-01) (2026-11-01, 5cdd852)
  - The owner raised the runtime's budget (ADR-0146), so 7.4h's retry of the `[~]` steps finds them free: mark 6.4i, 6.4j, 6.4l, 6.4m, 6.4n, 6.4o, 6.4p, 6.5d, 6.6g, 7.1e and 7.2c `[ ]`, and take them in order, from the work on `wip/6.4i-sending`.
  - Done when: `node scripts/roadmap-status.mjs` shows `blocked=0` and names 6.4i next, pushed.

### Phase 8: Actions and email

- [x] **8.1 The action framework** (2026-11-02, 585ce33)
  - **The model:** a registry with `FormBuilderActionTypeBase<TSettings>`, triggers, rules, order and "stop the rest if this fails". Publishing refuses an action type the site lacks (`unknown_type` at `actions[n].type`, ADR-0035) and runs each type's check of its own settings.
  - **The processor** (ADR-0007): one worker per server, woken after each commit through a scoped notification and a bounded channel of one, with claims by token, leases, back-off, the late finisher and the `Uncertain` state. The distributed job only wakes its server's worker and returns.
  - **Designer locks** of action settings, as 5.1 applies them to field settings (ADR-0042).
  - **Imported actions** (added in 4.5c, ADR-0046): a type says which site configuration it needs (such as a Slack address); an import switches off an action whose configuration this site lacks and adds a note with its path.
  - **The pause switch** (§4.4): while `JironFormBuilder:Actions:Paused` is on, the processor claims nothing, and the kick does nothing.
  - **Values kept only until the actions have run** (§4.4): deleted, with the entry event, once no trigger can still fire for the entry and every run is final.
  - **A form that stores no entries** (added in 6.8i by the Phase 6 wiring sweep): `settings.submission.store: never` (§4.3, ADR-0002) has only its save check so far, and `SubmissionEntries` stores every entry. The whole entry goes the same way as those values, its files too, and at once for a form with no action that can fire.
  - **The retry API,** which refuses to retry an `Uncertain` run without an explicit confirmation (§4.4).
  - **Test runs:**
    - email goes to the current user only;
    - webhooks send to the configured URL with `X-Jfb-Test: true` after a confirmation;
    - content actions create nothing (a dry run that shows what would be created).
  - Fill in the CI load-balancing job.
  - Done when: tests cover ordering, retries with fake time, lease expiry turning into `Uncertain`, the late finisher, and **no double execution** with two processors on one database. The two-processor test injects the faults of 1.6 and fails against a claim without its state condition (a mutant), so it is known to catch doubles. A test shows that every Jiron Form Builder job returns within 10 s, and does nothing while the migrations are pending (ADR-0033). The `loadbalancing` job definition is shown. Tests also show that nothing runs while sending is paused and every queued run goes out once it is not, that a retry of an `Uncertain` run without the confirmation is refused, that a value kept only until the actions have run is still there while a run is retrying and gone once no trigger can still fire for the entry and every run is final, that a designer lock of an action setting refuses a save with another value and wins at run time, and that an imported action whose site configuration is missing arrives switched off with its note.
- [x] **8.2 Email infrastructure** (2026-11-02, 9b2de74)
  - **Sending:** the outbox and sender job through `IEmailSender`, with `CanSendRequiredEmail()` gating so no retries are burnt while email is not configured (Umbraco would drop the mail silently). Queuing wakes the sender at once (D8). The sender also honours the pause switch (§4.4).
  - **Content:** responsive HTML layouts that work in dark mode, one body per mail as ADR-0007 decided (HTML, or plain text when the action chooses it, with the text version always generated), header sanitising, attachment caps, the per-site sender, a test email, and retention purge. The layouts follow the client rules of §3.3: inline styles on table cells, `<!--[if mso]>` tables for Outlook, and no styles on `<body>`.
  - **The limits for visitor-addressed mail** (§4.10): per recipient address (counted on `RecipientKey`, §4.5), per site, and the "limited" delivery state.
  - Done when:
    - tests pass with the pickup folder, including one that shows an HTML mail as one `text/html` part and a plain-text mail as one `text/plain` part, and one that shows header values with line breaks arriving with spaces;
    - a test shows that addresses differing only in case or by a `+tag` count as one recipient, and that a `Limited` mail is never sent or retried and is purged on time;
    - HTML snapshot tests pass;
    - every email is well-formed with inlined CSS (checked by a test);
    - a long unbroken word stays inside a 375 px wide rendering of every layout (Playwright on the `.eml` HTML);
    - tests show the per-address and per-site limits on SQLite and SQL Server, with two spellings of one mailbox that differ only in non-ASCII capitals counting as one, and the "limited" state in the delivery log;
    - a test shows that no email leaves while sending is paused, and that all of them leave once it is not.
- [x] **8.3 The email action** (2026-11-02, 3c8365f)
  - **Addressing:** the recipient kinds, CC/BCC, reply-to a field. An address a visitor typed gets a mail of its own, never a CC or BCC, so the limits of §4.10 count it (ADR-0159).
    - **A recipient map** (added in 1.1): the options of a choice field ("Sales", "Support") map to internal addresses, which stay out of the public projection (§4.3.1).
  - **Content:**
    - subject and body per culture with merge tags;
    - `{{ entry.table }}` (all answers as a table, optionally without the unanswered fields);
    - "include sensitive data" (off by default);
    - attachments;
    - a Razor view option (`Views/JironFormBuilder/Emails/*.cshtml`).
  - **Presets** ("Notify the team", "Confirm to the sender").
  - **The new-form default "notify the creator"** (§4.8; moved here from 4.5 because it needs this action, ADR-0044): a `general` setting for every site at once, which makes a form created from blank or from a template (ADR-0046) start with an email action to the form's creator, unless a template has one already.
  - Done when: tests pass, and an E2E run submits the demo contact form and finds two emails in the pickup folder with the right content, escaping and language. An abuse test also passes: a submission that puts a stranger's address in the email field and a link in the message gets the confirmation through only within the limits, and the link is not clickable in it. A test shows the recipient map choosing the address and the mapped addresses absent from the public projection. A form created from blank or from the Contact template starts with the action to its creator while the new-form default is on, and without it while it is off.
- [x] **8.4 Webhook, Slack and Teams** (2026-11-02, 64c98e1)
  - **The webhook:**
    - JSON, form-encoded or XML;
    - field mapping, metadata;
    - the HMAC signature (`X-Jfb-Signature: t=…, v1=…`, verification documented with code in C#, Node and PHP);
    - secret references, the SSRF guard, timeouts and retries.
  - **Slack and Teams:** presets with formatted text.
  - **Umbraco webhook events:** registration (§4.4).
  - Done when: tests pass against the stub on 5560, the SSRF refusal tests pass, and the signature verification samples are tested.
  - **Not run on this machine:** the PHP sample. There is no PHP here, so `ThePhpSample` is skipped and a static test holds its constants; on CI's runner it runs, and fails without PHP. The C# and Node samples ran. `docs/OWNER-TODO.md` lists it under the first CI run (ADR-0161).
- [x] **8.5 Content and state actions, email verification and approval** (2026-11-03, 45f15ef)
  - **Create or update content:**
    - document type, dynamic or picked parent, field-to-property mapping with type compatibility, publish or save;
    - uploaded files are not copied to media unless a mapping says so, and then into a chosen folder with the same file checks.
  - **Set state** under a rule.
  - **Email verification (double opt-in):**
    - the `Unverified` state and the email with its link;
    - the GET confirm page and the POST confirm (§4.6);
    - expiry and resend;
    - actions run after verification;
    - "not spam" on such a form sends an entry without `VerifiedUtc` to `Unverified` with the verification mail (§4.4).
  - **Manual approval:**
    - The approved and rejected triggers, with bulk and single changes.
    - **Entry events** (registered in 8.4, ADR-0161): a change to approved or rejected publishes `EntryApprovedNotification` or `EntryRejectedNotification`, and a confirmed entry that becomes submitted publishes `EntrySubmittedNotification`, each through `EntryEvents` and tested; a user's change, "not spam" among them, publishes once its undo window is over, so an event never needs calling back.
    - **Letting go after a user's change** (ADR-0158): a state change that leaves no trigger settles the entry, once its undo window is over when a user made it (8.1 leaves a user's "not spam" with no run unsettled for this).
  - Done when:
    - tests pass, and E2E double opt-in works from the pickup email, including a simulated link-scanner GET that verifies nothing; no action runs before the confirmation, and after it the submitted and verified actions run together in the order shown. A test shows that verification mail obeys the visitor-address limits (§4.10);
    - tests cover the approved and rejected triggers, for single and bulk changes;
    - tests with fake time show that the runs a user's state change queues (approve, reject, not spam; single and bulk) start only after the undo window, that Undo inside the window restores every entry's state, gives places back or takes them again, and leaves those runs `Skipped` with `undone` and no email sent, that Undo after the window is refused with `409`, and that changes made by a rule, a "set state" action, verification or automatic approval run at once (ADR-0023);
    - tests show "not spam" on a verification form sending an entry without `VerifiedUtc` to `Unverified` with its mail, and a value kept only until the actions have run surviving while its entry is `Unverified`, in `Spam` or waiting for approval (also when the form has only `submitted`, only `verified` or only `approved` actions), used by the runs that follow, and gone after the last one.
- [x] **8.6a Save and resume on the server, and phase review** (2026-11-03, eea784d)
  - **The flow (ADR-0163):**
    - the `drafts` and `resume` endpoints (§4.6), with the `Draft` state and the resume email through the outbox;
    - the token in the URL fragment, never sent to the server in a query string;
    - expiry, and the page restored;
    - drafts purged when they expire.
    - **Places and unique answers** (ADR-0090, ADR-0091): a draft holds no places and no unique answers; the submit that turns it into an entry takes them as a submission does, and fails with `full` where an option has none left, and with the field's `uniquePerForm` check where another entry holds the answer by then.
  - **Phase review:** write the handover note.
  - Done when:
    - tests cover expiry, a second resume, the submit that turns the draft into an entry (and takes its places then, ADR-0090, and fails at a unique field whose answer another entry took meanwhile, ADR-0091), a resume that gives no file and no consent back, and the visitor-address limits for resume mail (§4.10);
    - pushed, with the phase-review gate.
- [x] **8.6b The runtime's "Save and continue later"** (2026-11-14, 83f1e768)
  - **Resumes (2026-11-14):** the owner raised the budget to 100 KB (ADR-0251); this step resumes from
    `wip/8.6b-resume-control` once 19.1 is ticked, before 20.1, with every check below.
  - **Retried at 9.6b (2026-11-05):** the branch with Phase 9 merged in measures 61.96 KB against 60 KB, and still waits.
  - **Retried at 10.4 (2026-11-07):** the branch merges into Phase 10's main without a conflict and measures 61.95 KB
    against 60 KB, and still waits.
  - **Retried at 11.6 (2026-11-08):** the branch merges into Phase 11's main with one conflict (the parameters of
    `FormModels`, both sides kept), measures 62.15 KB against 60 KB, and still waits.
  - **Retried at 12.6 (2026-11-09):** the branch merges into Phase 12's main with the same conflict, both sides kept,
    measures 62.15 KB against 60 KB (the runtime did not change in Phase 12), and still waits.
  - **Retried at 13.4 (2026-11-09):** the branch merges into Phase 13's main with the same conflict, both sides kept,
    measures 62.15 KB against 60 KB (the runtime did not change in Phase 13), and still waits.
  - **Retried at 14.1 (2026-11-11):** the branch merges into Phase 14's main with two conflicts, both on the server (the
    parameters of `FormModels` and the visitor words of `RenderingWords`), measures 61.53 KB against 60 KB (its words now
    in seven languages, the largest counted), and still waits.
  - **Retried at 15.1 (2026-11-11):** the branch merges into Phase 15's main with the same two conflicts on the server,
    both sides kept, measures 61.62 KB against 60 KB, and still waits.
  - **Retried at 16.5 (2026-11-12):** the branch merges into Phase 16's main with the same two conflicts on the server,
    measures 61.67 KB against 60 KB, and still waits.
  - **Retried at 17.1 (2026-11-13):** the branch merges into Phase 17's main with the same two conflicts on the server,
    measures 61.67 KB against 60 KB (the runtime did not change in Phase 17), and still waits.
  - **Retried at 18.1 (2026-11-13):** the branch merges into Phase 18's main with the same two conflicts on the server,
    measures 61.69 KB against 60 KB, and still waits.
  - **Retried at 19.1 (2026-11-13):** the branch merges into Phase 19's main with the same two conflicts on the server,
    measures 61.71 KB against 60 KB, and still waits.
  - **Blocked on the owner (2026-11-02):** the control and the restoring of answers are built and wait on the branch `wip/8.6b-resume-control`. As the chunk of their own they come to 1.94 KB gzip, which puts the runtime with every feature at 61.95 KB against the 60 KB of ADR-0146, and that ADR leaves anything past 60 KB to the owner. `docs/OWNER-TODO.md` asks.
  - **The control:** the runtime's "Save and continue later" (the only use of browser storage, §4.10), and a link's answers and page restored on arrival, its token taken out of the address at once.
  - **Its switch** in the Settings view's Submission group, under More options, with the link's days (moved from 10.1a: settings come with their feature).
  - Done when: Playwright turns on "Save and continue later" in the form's Settings view (a number of days out of range says its message there) and resumes a three-page form from the link in the pickup email, and a resumed form asks again for the files it had and shows every consent unticked.

### Phase 9: The backoffice I: section, form library and designer

Build from the Phase 2 designs. Every screen in this phase and the next must pass the measurable rules of §4.13 before its step is done.

- [x] **9.1 Section, shell and setup** (2026-11-03, 4bdec12)
  - **The section:** `Jiron.FormBuilder.Section` ("Form Builder", ADR-0171) with its sidebar:
    - the **Forms** tree;
    - **Entries:** Inbox (the entries of every form), Spam;
    - **Library:** option lists, themes, email layouts, templates;
    - **Settings:** General, Email, Spam protection, Files, Access, Integrations;
    - **Privacy:** People (the privacy tools), Retention, Data collection;
    - **Activity:** Deliveries, Audit log.

    The labels, the permission each item needs and the screens behind them are in `docs/ux/information-architecture.md` (ADR-0021).

    The section's pathname is `jfb-forms`, the one `entry.url` links name (`BackofficeLinks.SectionPathname`, added in 5.3e, ADR-0067).

    Menu items appear only for users with the matching permission. Every item is there from this step, and one whose screen comes in a later phase opens one sentence in its place until then (ADR-0165, so that each role's menus are real from the start; 13.4 checks that none is left).
  - **The dashboard:**
    - the setup checklist of §4.13, each item with a detector on the server that knows when it is done;
    - recent entries;
    - health warnings.
  - **Health checks** (also on Umbraco's Health Check dashboard): email can be sent (Umbraco's gate, and a From address, which the gate does not check, ADR-0007), the uploads folder is not web-accessible and, on a site with more than one server, not the default folder on each server's own disk (the start-up error of 4.8c, ADR-0041), the Data Protection key ring is persisted (needed for challenges across restarts and servers), jobs are running, deliveries are failing, sending is paused (§4.4), the migrations are pending (ADR-0033: the dashboard then says so too, and why Jiron Form Builder is paused), a site has no base URL, so its mails carry no links (added in 5.3e, ADR-0008, ADR-0067), and the "used on" index, whose action asks for a rebuild and which says while one is under way (added in 6.7c, ADR-0017, ADR-0131).
  - **Plumbing:**
    - en/de localisation infrastructure, the generated API client, and the global search provider;
    - the safe confirm helper (§4.10), with the source test that bans direct use of Umbraco's confirm modal.
  - Done when:
    - Playwright shows that each role sees exactly its menus, including that the privacy officer sees Privacy's People and Retention without Data collection;
    - a confirm that names a form called `<img src=x onerror=alert(1)>` shows that text literally (Playwright);
    - each checklist detector has unit tests (the full green run on the demo is checked in 12.6, once every linked screen exists);
    - Playwright presses the checklist's button for the form block, which calls `setup/form-block`, and its item turns green (added in 6.8i by the Phase 6 wiring sweep);
    - an integration test shows the pending-migrations health check failing while the plan is behind and passing once it has run (ADR-0033);
    - an integration test runs the "used on" health check's action and finds a rebuild requested from the first document, which the reference job then finishes (added in 6.7c, ADR-0131);
    - a test reads the section's manifest and finds the pathname `BackofficeLinks.SectionPathname`, and unit tests show the base-URL health check warning for a site with no setting, no `https` domain and no application URL, naming the site, and passing for a site with any of them (added in 5.3e);
    - screenshots are reviewed in `docs/ux/review-9.1.md` against §4.13;
    - in each of Umbraco's light, dark and high-contrast themes, axe is clean on the dashboard and a Playwright script measures `--jfb-bo-color-text-muted` (4.5:1) and `--jfb-bo-color-control-border` (3:1) against UUI's surface and background, so the backoffice's own colours follow every theme (`docs/ux/tokens.md`).
- [x] **9.2 The form library** (2026-11-03, b6d8bf6)
  - **The tree:** folders, and create from blank or from the template gallery (a modal with previews; it shows the templates that exist, which are "Contact" from 4.5 until 12.3 adds the rest).
  - **Entity actions:**
    - rename, move;
    - duplicate (with or without actions; for a restricted form the dialog says that the copy takes its folder's access instead, ADR-0046), copy actions to another form (the result lists the references it could not map, each with a link to fix it);
    - import as a new copy (the result shows the import's notes: a theme or option list the site lacks), export JSON (12.5 adds "replace" and the mirror).
  - **Trash:** trash, restore, empty trash. The confirmations of trash and of delete name the pages that use the form, from `referenced-by` (6.7c, added in 6.8i by the Phase 6 wiring sweep).
  - **Search:** forms by name and alias, and fields across all forms by label or alias (from the field catalogue, §4.5).
  - Done when: the Overview's Choose a template… opens the gallery (it opens the Templates item until then, ADR-0165), Playwright journeys for each action pass, including finding a form by one of its field labels, a form used on a page named by both confirmations, the Duplicate dialog of a restricted form saying what the copy's access will be, and an import whose notes are shown, and the client stores have unit tests.
- [x] **9.3a The designer: workspace and editor model** (2026-11-03, 2a8fc56)
  - **The workspace:** the form workspace with the views Design, Settings, Actions, Entries, Insights and History (views without content yet show a short "coming with …" empty state, replaced by their steps). The name and status are in the header; Save draft and Publish in the footer.
  - **The editor model** is pure TypeScript with unit tests (the Ligata.Forms lesson, §3.1):
    - selection;
    - insert, move, duplicate and remove;
    - undo/redo;
    - the rule-safety checks that refuse moves breaking a rule.
  - Done when: Vitest covers every editor-model operation, and the workspace loads and saves a form through the API. Playwright renames a form in the workspace's header, which is where forms are renamed (ADR-0166), and opens a field's result of the header search, which selects that field (`?field=`, ADR-0166).
- [x] **9.3b The designer: canvas and palette** (2026-11-03, f04448e)
  - **The canvas:** pages as steps, groups with columns, fields rendered by small Lit preview elements per type, insert points, and canvas widths for phone, tablet and desktop.
  - **The palette:** search, groups, recently used.
  - **Inline label editing.**
  - **Paste a list of questions** (added in 1.1): one line per question becomes a text field with that label, ready to change type.
  - Done when: Playwright builds a three-page form with the mouse and adds five fields from a pasted list, and screenshots are reviewed in `docs/ux/review-9.3.md`. The template gallery's preview (9.2) draws its fields with these preview elements, and a test finds no other drawing of a field in the backoffice package (ADR-0166).
- [x] **9.3c The designer: drag and drop and keyboard** (2026-11-03, bee8e10)
  - **Moving:**
    - drag and drop per ADR-0016, across groups and pages;
    - "Move to…" for keyboard users;
    - refused moves explained in place.
  - **Keyboard:** arrows, Alt+arrows, Delete with an undo toast, Ctrl+D, Ctrl+Z/Y, and the `?` overlay.
  - **Focus is never lost:**
    - after a move, focus stays on the moved element;
    - after a delete, it goes to a neighbour; after an undo, to the restored element.

    The canvas reconciles its children instead of replacing them (§3.3). Lost focus was a finding in three of Tandem's phase reviews.
  - Done when: Playwright builds a three-page form **with the keyboard only** and asserts the focused element after every operation, and a 300-field form stays within the §4.12 budget.
- [x] **9.4a The inspector: its frame, sections and plain settings** (2026-11-03, c1a8307)
  - **A generic settings renderer** from type schemas, with the editors for text, number (whole and decimal), switch, select (a choice), lists, nested settings, times, date bounds and a formula as text; the richer editors come in 9.4b and 9.4c.
  - **Sections:** Essentials, Validation, Rules, Appearance, Advanced (§4.13).
  - **Beside the canvas** (added in 9.3b, ADR-0168): between 620 and 1024 px the palette folds to its rail while an element is selected, so the canvas keeps its width beside the inspector, and opens again when the selection clears (IA 5.1).
  - **Per-culture texts** with a culture switcher and "missing translation" marks.
  - **Fields:** alias editing with its lock rules.
  - **The element's "…"** (added in 9.3c, ADR-0169): on the selected element's bar, after Move to…, Duplicate and Delete, a "…" with Start a new page after this and Copy alias for a field (Change type… comes with 9.4d); at 620 px the bar sits in the inspector's header (IA 5.1).
  - **Locks:** host-locked settings are shown as locked.
  - **Designer locks** (added in 5.1f, ADR-0060): a setting the field types endpoint marks with a lock shows read-only with its locked value and the configuration key behind it, a hidden one does not show at all; the change-type preview's side of the lock comes with 9.4d.
  - Done when: Playwright sets every setting of the text, number and date types with the plain editors through the inspector, and axe is clean on the inspector with each section open. With a designer lock of the text type's maximum length and a hidden lock of its prefix, Playwright finds the maximum length read-only with its value and key and no prefix in a text field's inspector (ADR-0060). At a 1024 px window, selecting a field folds the palette to its rail and clearing the selection opens it again. Playwright starts a new page after a selected field from its "…", with the keyboard only. On a site with two languages, Playwright edits a field's German label in the inspector and finds the "missing translation" mark on a field without one.
- [x] **9.4b The inspector: options** (2026-11-03, 62cf33c)
  - **The option editor** of the generic settings renderer: drag to reorder, paste many lines at once, images and descriptions for card layouts, a place limit per option; it also edits a grid's rows and columns.
  - **Option sources** (added in 5.5j by the Phase 5 wiring sweep; ADR-0073, ADR-0075, ADR-0076, ADR-0077): the option lists' configuration has a service and a publish check but no endpoint and no texts yet.
    - **The endpoint** serves the option kinds a field's source may use, each with its configuration's settings schema as the field types endpoint serves a type's settings (kinds, rules, `#jfbOption*` labels and descriptions), and the code sources' names with their labels in the registry's order, for any user of the Forms section, with the permission matrix and the regenerated client.
    - **The source picker** in the option editor: the field's own options, a library list (with its kind and how many options it offers), or a kind configured on the field through the generic renderer; for a cascading field, the parent among the earlier choice fields.
    - **The texts:** the backoffice dictionaries get English and German texts for every `#jfbOption*` key the kinds and the standard sources name, and the dictionaries' check holds them to the endpoint's snapshot, as it holds the field types' keys to theirs.
  - Done when: the option sources endpoint's integration tests cover its answer (held to a snapshot in `docs/api/`) and the permission matrix; the dictionaries' check fails for a `#jfbOption*` key without an English or a German text; Playwright picks a library list, a code source and a cascading parent for a dropdown, and the canvas shows the options they resolve. Playwright adds options to a radio field by pasting lines, reorders them by drag and by keyboard, gives one a place limit, and edits a grid's rows and columns.
- [x] **9.4c The inspector: rich text, merge tags, pickers and patterns** (split from 9.4, see WORKLOG) (2026-11-04, d773fa5)
  - **The richer editors** of the generic settings renderer: rich text (Tiptap), merge-tag-aware text with a tag picker, content and media pickers, and a pattern editor with a live tester.
  - **The content and data type kinds** of 9.4b's source picker (ADR-0172): with the content picker, a document type picker and a data type picker in the generic renderer, "Options from" also offers content and a data type, configured through their served schemas.
  - **The tag picker's paths** (added in 5.5j by the Phase 5 wiring sweep; ADR-0065, ADR-0066): `IFormBuilderMergeTagService.Paths` has no caller yet. An endpoint serves the paths a form's merge tags may use in a place (the form's fields', the built-in sources' and the site's own, each with its label), for a user who may edit the form, with the permission matrix and the regenerated client. The tag picker of merge-tag-aware text offers the paths of its setting's place and inserts the tag; 10.2's email editor uses the same endpoint for mail.
  - Done when: the merge-tag paths endpoint's integration tests cover its answer for a form with fields and a site's source in two places (held to a snapshot in `docs/api/`) and the permission matrix, and Playwright inserts a field's tag through the picker. Playwright writes a consent's rich text, picks an image's media, and tries a pattern against a sample in the live tester before it is kept. Playwright takes a dropdown's options from content under a page picked with the content picker, and the canvas shows the pages it resolves.
- [x] **9.4d The inspector: changing a field's type, and every type** (split from 9.4, see WORKLOG) (2026-11-04, d6bad04)
  - **Fields:** change field type (compatible types only, with a preview of what is lost), from the inspector and from the element's "…" (Change type…).
  - **Changing types** (added in 5.1e, ADR-0059): the field types endpoint (5.1c) also serves each type's changes from 5.1e's map, with the permission matrix and the regenerated client, and the preview applies a change to the selected field: its values of lost and narrowed settings, the properties it uses that go, the conditions naming it whose operator goes, and the settings and options it needs.
  - **Designer locks** (added in 5.1f, ADR-0060): the change-type preview treats a locked setting of the target type as set by the lock: neither carried over nor needed.
  - Done when: Playwright configures every built-in type through the UI, and axe is clean on the inspector. Playwright changes a text field with a placeholder and a condition using `contains` into a dropdown, and the preview names the placeholder, the condition and the options it needs before the change; the endpoint's integration tests cover the served changes (ADR-0059). With a designer lock on the dropdown type, the preview neither carries the locked setting over nor asks for it.
- [x] **9.5a The rules builder** (split, see WORKLOG; ADR-0175) (2026-11-04, 45d8fc8)
  - **The builder:** rule rows in plain language ("Show this field when **Country** **is** **Germany**"), any/all groups, operators per field type, and value pickers (options, dates including relative ones, numbers).
    - "Required when", a value taken from another field ("is after **Arrival**"), and the context conditions (language, member, query value, today), all of §4.3.3.
    - A switch that turns a rule off without deleting it.
  - **Where it opens** (ADR-0175): the inspector's Rules section lists an element's rules in words, each opening the builder, and holds Add a rule; the builder's live rule preview (Try it) runs on `@jfb/logic`.
  - Done when: Playwright builds nested rules of every operator kind, a "required when" rule, a comparison of two fields and a context condition, and the designer's live rule preview agrees with the shared vectors (D10).
- [x] **9.5b The form check on the server** (split, see WORKLOG; ADR-0177) (2026-11-04, 6661367)
  - **The check runs on the server and joins the publish pipeline:** errors block publishing, warnings and hints don't. A draft is checked without publishing too, for the designer (the `design` permission), and every issue names the element and the setting it is about by its path, so that every issue can have a "fix it" link in the designer (9.5e).
  - **The checks that the field types, the settings and the merge-tag engine already know:**
    - an email action with no email field to reply to;
    - missing labels or alt texts;
    - duplicate aliases, unknown merge tags;
    - rules on removed fields;
    - a captcha provider that isn't configured;
    - a text that recalls a later field's answer (an error, §4.3.3);
    - a consent text with a merge tag other than `form`, `page`, `site` or `dictionary` (an error, ADR-0014);
    - a redirect that puts answers into its URL (a warning: answers in URLs end up in logs and browser history).
  - Done when: each check has unit tests, publishing with an error is refused through the API, and the draft check returns the errors, warnings and hints with their paths through the API, a form with warnings only still publishing.
- [x] **9.5c Seeing rules, and form-level validation rules** (split from 9.5a, see WORKLOG; ADR-0175, ADR-0178) (2026-11-04, 0a3e9cc)
  - **Seeing rules:** chips on the canvas, and a **rules overview** listing every rule in the form with links.
  - **Form-level validation rules** in the same builder.
  - Done when: Playwright finds a rule's chip on the element it affects and none on the selected element, opens the rules overview from the toolbar's "...", follows a rule's link to its element, finds a hide rule and a require rule that hold for the same answer warned on their row, adds a rule from the overview after choosing what it affects, and finds the empty overview's sentence; and builds a form-level validation rule with its field and message in the builder, whose message the published form shows on that field in the browser when the rule matches.
- [x] **9.5d The form check: the form's logic** (split from 9.5b, then in two, see WORKLOG) (2026-11-04, cb13a79)
  - **The checks that reason about the form's rules,** with the rule search of the rules overview (9.5c) on the server too, held to the same shared vectors in both runtimes (D10):
    - a required field that a rule can hide;
    - unreachable pages;
    - and conditions a change of type left (promised by the decision records).
  - Done when: each check has unit tests, the search passes the shared vectors in both runtimes, and publishing with an error is refused through the API.
- [x] **9.5e The form check: the site, privacy and the promised checks** (split from 9.5d, see WORKLOG) (2026-11-04, 5285826)
  - **The checks that reason about what the site holds and what the form collects:**
    - personal data collected without a consent field (a hint);
    - an outcome redirecting to an unpublished page;
    - a file field without limits;
    - labels or patterns that ask for card numbers or passwords (a warning, §1.4);
    - a required choice whose options can all become full;
    - and anything Phase 1 adds: the decision records promise a dictionary key without a text (a warning), a last outcome with a rule (a warning), a missing theme, a host lock that replaces a stored value, content, media or member groups the site lacks, place or unique limits on a form that keeps no entries, and a merge tag in a label from a source the label refuses.
  - Done when: each check has unit tests, and publishing with an error is refused through the API.
- [x] **9.5f The form check in the designer** (split from 9.5b, see WORKLOG) (2026-11-04, ae4a63a)
  - **The badge, the list and "fix it"** (IA 5.1): the badge in the toolbar ("2 problems", "No problems"), the list in a sidebar with the errors first, and a "fix it" on every issue of 9.5b, 9.5d and 9.5e that selects the element and opens its setting (the palette for a form without fields, the rules builder for a rule); the publish dialog shows the check's result, errors blocking and warnings not.
  - Done when: Playwright covers the badge, the list and the "fix it" links, a publish blocked by an error and one with a warning, and axe finds nothing on the list.
- [x] **9.6a Phase review: the UX walk and its fixes** (split from 9.6, see WORKLOG) (2026-11-05, e67836f)
  - **UX review:** every screen of the phase at 375, 768 and 1440 px, in light and dark, in en and de, each checked against the measurable rules of §4.13, plus a pass of all three design skills (ADR-0019, ADR-0020): `design-taste-frontend`, an `impeccable` critique and audit, and an `emil-design-eng` review. Fix the findings.
  - **Carried in from 9.4c's look** (docs/ux/review-9.4c.md): on the canvas, the selected element's bar sits over the element above it and covers most of a heading's text; decide whether the bar takes room of its own, and hold the choice with a journey.
  - **Carried in from 9.4d's look** (docs/ux/review-9.4d.md): in Umbraco's dark theme, the primary buttons' label meets 4.38:1 on positive green and 4.34:1 on the default blue, under AA's 4.5:1 (axe on Change type's dialog); decide once for every dialog and footer button of the section, and hold it with axe in the dark theme.
  - **Carried in from 9.5f's look** (docs/ux/review-9.5f.md): on a phone the Design view keeps Umbraco's workspace at its narrowest, 620 px, so its toolbar is reached by scrolling sideways; decide how the designer meets a phone, and hold the choice with a test that measures it.
  - Done when: `docs/ux/review-9.md` with screenshots, with a section per design skill, is committed; a journey holds the canvas bar's choice, axe in the dark theme holds the buttons' choice, and a measuring test holds the phone's; and everything is pushed.
- [x] **9.6b Phase review: the budget, the wiring sweep and the handover** (split from 9.6, see WORKLOG) (2026-11-05, ee04734)
  - Write the handover note.
  - **The backoffice budget:** set the backoffice bundle's gzip budget in the size check from the measured size plus a stated margin, and record it in §4.12.
  - Done when: the size check enforces the backoffice budget, and everything is pushed, with the phase-review gate.

### Phase 10: The backoffice II: settings, actions, preview and publishing

- [x] **10.1a The form settings view: its groups** (split from 10.1, see WORKLOG) (2026-11-05, 5a46a03)
  - The groups of §4.3.4 with help text: submission, availability, spam, appearance and developer settings, open or closed as IA 5.2 says, each closed group with its summary line, and the server's validation messages at their setting. Settings come with their feature: the Privacy group comes with 11.5 (insights), 13.1 (retention, and Store entries "For a number of days") and 13.3 (IP handling), "Save and continue later" with 8.6b, and "One entry per member" with 14.3, which build what they configure.
  - **Captcha providers:** the spam group greys out a provider without keys, from `GET captcha-provider` (each provider's name, label, `isConfigured` and settings, never a key), with its row in the permission matrix (added in 7.4g); the site's defaults, time zone, themes and member groups come from `GET form/{key}/setting-choices`, for who may design the form (ADR-0184).
  - **The form check's Fix it** (ADR-0181, added in 9.5f): a problem in the form's settings, which 9.5f leads to this view, opens the group and the setting it names, with the focus on it.
  - Done when: Playwright covers each of these groups, including validation messages, and follows the form check's Fix it of a setting to it.
- [x] **10.1b The form settings view: after sending** (split from 10.1, see WORKLOG; its live preview split off to 10.1c) (2026-11-05, 32812ac)
  - The outcomes editor of §4.3.4 and IA 5.2: an ordered list with rules, each outcome edited in a sidebar; the canvas's After sending opens it.
  - **The outcome's rule** in the rules builder, whose effect is `use` (§4.3.3), as the builder edits an element's.
  - **The form check's Fix it** (ADR-0181, added in 9.5f): a problem in an outcome, which 9.5f leads to this view, opens the outcome and the setting it names, with the focus on it.
  - Done when: Playwright covers the After sending group, including validation messages and an outcome's rule, follows the form check's Fix it of an outcome to it, and opens an outcome's editor from the canvas's After sending.
- [x] **10.1c The form settings view: an outcome's live preview** (split from 10.1b, see WORKLOG) (2026-11-05, 2129fa4)
  - The outcome's sidebar shows the outcome beside its settings as visitors get it with a sample entry made from the form's fields, as each setting changes: the server renders it, as it renders a real entry's, from the draft.
  - Done when: Playwright edits an outcome's message and its preview shows the sample entry's answers in place of its merge tags, a sensitive field's masked; integration tests cover the preview endpoint, with its row in the permission matrix and the OpenAPI snapshot.
- [x] **10.2a The actions view: the list and an action's sidebar** (10.2 split in four, see WORKLOG) (2026-11-06, eb5ae73)
  - **The list:** action cards per trigger (Submitted, Approved, Rejected, Verified), with order (drag and keyboard), an on/off switch and a rule chip; the last-run status is 10.2f's. Each card says what the action does in one line, and its "…" duplicates it, moves it to another trigger and deletes it with Undo (IA 5.3). Without `manageActions` the view is read-only and says which permission changes it.
  - **Verification** is no action of its own: the Verified trigger's list says that it needs the form's email verification setting (10.1) and links to it; Rejected says so of manual approval.
  - **Adding:** an "add action" modal with presets: the action's sidebar, its first setting choosing what happens among the types and their presets (IA 5.3), then the trigger and the type's settings, drawn by the inspector's setting editors. The editors no type has yet are 10.2b's and 10.2c's.
  - **The action's rule** in the rules builder, whose effect is `run` (§4.3.3), as 10.1b's outcome rule is `use`.
  - **The form check's Fix it** (ADR-0181, added in 9.5f): a problem in an action, which 9.5f leads to this view, selects the action and opens the setting it names, with the focus on it.
  - Done when:
    - Playwright adds an action from a preset, moves it by dragging and by the keyboard, switches it off, gives it a rule, and deletes it and brings it back with Undo;
    - Playwright opens the view as a user without `manageActions` and finds it read-only;
    - Playwright follows a link of 9.2's copy-actions result, which opens the target's Actions view with the copied action selected (`?action=`, ADR-0166);
    - Playwright follows the form check's Fix it of an action's setting to it.
- [x] **10.2b The actions view: the email editor and its preview** (10.2 split in four, then 10.2b in two, see WORKLOG) (2026-11-06, 818d1c3)
  - **The email editor:** a recipient builder, the merge-tag picker (its paths from 9.4c's endpoint, for mail), and a live preview with a sample entry.
  - The form check's Fix it lands on the email editor's own settings too.
  - Done when:
    - Playwright configures an email action from a preset with the recipient builder (an address, an email field's answer and the addresses an answer chooses), and the draft keeps it as the server writes it;
    - Playwright edits the email's body and its preview shows the sample entry's answers in place of its merge tags;
    - Playwright follows the form check's Fix it of an email setting to its editor;
    - integration tests cover every new endpoint, with its row in the permission matrix and the OpenAPI snapshot.
- [x] **10.2c The actions view: Test** (10.2 split in four, then 10.2b in two, see WORKLOG) (2026-11-06, c6cc659)
  - **Test run:** on a card and in the sidebar, for the action as the sidebar holds it, saved or not, against the sample entry (ADR-0187).
  - Done when:
    - Playwright configures an email action from a preset, and a test-run email lands in the pickup folder;
    - Playwright test-runs an action from its card, and one from its sidebar before it is saved;
    - integration tests cover every new endpoint, with its row in the permission matrix and the OpenAPI snapshot.
- [x] **10.2d The actions view: the webhook, Slack and Teams editors** (10.2 split in four, then 10.2d in two, see WORKLOG) (2026-11-06, e7fe1c3)
  - **Webhook:** a mapping table, a signature help text and a test.
  - **Slack and Teams.**
  - The form check's Fix it lands on these editors' own settings too.
  - **Carried in from 10.2c** (ADR-0190): the webhook's test answers with a code the backoffice says in its words (`ActionTestCodes`), each with a test. Since 10.2c's review it answers with `sent` or its run's code, under an English sentence the toast shows until then.
  - Done when:
    - Playwright configures and test-runs a webhook, a Slack message and a Teams message against the stub on 5560;
    - integration tests cover every new endpoint, with its row in the permission matrix and the OpenAPI snapshot.
- [x] **10.2e The actions view: the content and Set state editors** (10.2 split in four, then 10.2d in two, see WORKLOG) (2026-11-06, 4224674)
  - **Content:** document type and property mapping; the site's document types come with the setting as its choices, as the option sources serve them (ADR-0173), since Umbraco lists them only to users of its Settings section.
  - **Set state.**
  - The form check's Fix it lands on these editors' own settings too.
  - **Carried in from 10.2c** (ADR-0190): the content action's and Set state's tests answer with a code the backoffice says in its words (`ActionTestCodes`), as dry runs whose lines say what a run would do in the backoffice's words, each with a test. Since 10.2c's review they answer as dry runs, their sentence an English preview the toast shows until then.
  - Done when:
    - Playwright configures and test-runs a content action and a Set state action;
    - integration tests cover every new endpoint, with its row in the permission matrix and the OpenAPI snapshot.
- [x] **10.2f The actions view: the last run and its details** (10.2 split in four, then 10.2d in two, see WORKLOG) (2026-11-06, 4442d2e)
  - **The last-run status** on each card ("Delivered 2 hours ago"); a failed or uncertain last run links to **Details**: what the server answered and the tries, never the entry, who can retry the run (ADR-0013, amendment), and Test (IA 5.3).
  - Done when:
    - Playwright shows an action's last run, and a failed one's Details with the server's answer and its tries (the webhook stub answering with an error);
    - integration tests cover the endpoint, with its row in the permission matrix and the OpenAPI snapshot, and show it naming no entry and no answer.
- [x] **10.3 Preview, publishing and history, and phase review** (10.3a, 10.3b, 10.3c, 10.3d, 10.3e, 10.3f and 10.4 in one, ADR-0197) (2026-11-07, ff38c58)
  - **10.3a History: the list, pins, comments and the comparison**:
    - **The list:** versions (draft saves and published), with pins and comments (IA 5.6): the published versions newest first, each with when, who and what changed against the one before, its comment and pin, the live one's "Published" chip; the "Show draft saves" switch; a row's "…" with Pin and Add comment….
    - **The diff:** a side-by-side comparison in plain language (fields added, removed and changed; settings, actions and rules): Compare for two ticked versions, and a clicked row's sidebar comparing that version with the current draft. The server sends paths and values (ADR-0045), and the backoffice says them in its own words, in English and German.
    - Done when: tests and Playwright cover the history: the list with its draft saves, the diff, pins and comments; Vitest says every kind of change the comparison sends in words.
  - **10.3b History: restoring as a new version, and how long draft saves are kept**:
    - **Restore** as a new version (IA 5.6): Restore as a new draft in the sidebar, Restore and publish…, a row's "…" restoring at once with Undo, and Restore an earlier version… in the form's "…".
    - **The version-cleanup settings:** how long a form's History keeps draft saves, read by the draft cleanup (`HistorySettings`), with an endpoint and their place in Settings › General (the rest of General is 12.4a's).
    - Done when: tests and Playwright cover restoring as a new version from the sidebar, from a row's "…" with Undo and from the form's "…"; an integration test shows the cleanup keeping draft saves as the changed setting says; every new endpoint has its row in the permission matrix and the OpenAPI snapshot.
  - **10.3c Conflicts, and the guard against leaving unsaved**:
    - **Conflicts:** when someone else saved first, show both versions and let the user choose; never overwrite silently (IA 5.7: the save conflict says who saved and when, and that the user's changes are still here; Review both versions takes each difference from either side). A Save after a refused one is refused again until the user has chosen (the survey for this split found the re-read after a failed save taking the newer version, so a second Save would overwrite).
    - **A guard** against leaving with unsaved changes: Umbraco's, for a change on the canvas and in a view as for the name, and for closing or reloading the browser's tab.
    - Done when: Playwright covers a two-browser concurrency conflict, choosing differences from both sides and saving; a test shows a second Save after a conflict refused; Playwright leaves a canvas change unsaved and is asked first.
  - **10.3d Publish: what changed since the live version**:
    - **Publish:** a summary of the changes against the live version, and draft and unpublished badges (the badges came with 9.3a): the publish dialog groups what changed (fields, settings, actions, rules), with unsaved changes included, or, on a first publish, says what the first version holds (IA 5.7).
    - Done when: Playwright covers publishing: a changed form's dialog lists its changes, grouped, and a first publish says what the first version holds; tests cover the grouping.
  - **10.3e Preview: the frame, its ticket and its choices**:
    - **Preview:** a modal with the draft rendered by the chosen theme in an iframe (from a preview endpoint), device widths, light and dark, and a culture switch (IA 5.7: Preview in the footer, the form's widths with "Narrowed to fit"). The frame is sandboxed without `allow-same-origin`; its requests reach the public API with the origin `null` and a short-lived signed preview ticket bound to the form (§4.10, ADR-0012), issued to who may design it.
    - Done when:
      - Playwright covers preview: its widths, light and dark, and another language;
      - the preview frame has an opaque origin (§4.10), and integration tests show the public API refusing a `null`-origin request without a valid preview ticket, with an expired one and with one for another form; the ticket's endpoint has its row in the permission matrix and the OpenAPI snapshot.
  - **10.3f Preview: test entries and sample answers**:
    - Test submissions are marked `IsTest`: the preview sends against the draft, and a test entry runs no actions unless "Run actions for test entries" is on; after sending, Run actions… runs them for that one test entry, asking first (IA 5.7). **Fill with sample answers** (added in 1.1) fills every field with valid values in one click, so editors can try rules and outcomes quickly (Start again, after sending).
    - Done when: Playwright covers sample answers that pass validation for every field type and a test entry, which runs no action; tests cover a test entry's actions with and without the setting and Run actions….
  - **10.4 Phase review**:
    - UX review as in 9.6, plus the handover note.
    - The option editor's rows get a way to move without dragging, as an outcome's Place in the list (WCAG 2.5.7,
      ADR-0185; found in 10.1b's review).
    - Done when: `docs/ux/review-10.md` is committed and pushed, with the phase-review gate, and a test moves an option
      to another place with single pointer presses, no drag.
  - Done when: every part's "Done when" above passes, as each part says.

### Phase 11: Entries, exports and insights

- [x] **11.1 Entries: the list, the entry panel, the inbox and Spam** (11.1, 11.2 and 11.3 in one, ADR-0197) (2026-11-07, a387009)
  - **11.1 The entries list**:
    - **The table:** a collection table per form, a column chooser (saved per user in `jfbUserSetting`), server-side paging, "new" markers and mark-as-seen, and an action status ("3/3 delivered"). Each entry is named by its entry title (§4.3.4), or by its number when the form has none.
    - **Finding entries:** filters as chips (date presets and ranges, state, typed field filters), search and sort. A set of filters can be saved under a name (per user). A search shows its count exactly up to 1,000 and as "more than 1,000" beyond (ADR-0003).
    - **Working on many:** bulk actions (approve, reject, spam, not spam, delete, export selected, re-run actions) and the keyboard shortcuts of §4.13. Export selected… opens 11.4's export dialog and comes with it, its check there (ADR-0207).
    - **Drafts stay out** (ADR-0163): an empty state filter means every state to the repository, so the list, its counts, search and exports name the states they show and leave `Draft` out, with a test.
    - Done when: Playwright passes with 1,000 seeded entries, including a saved filter set reopened in a second browser and a bulk approval undone from its toast inside the window (`page.clock`; every entry back in its state, no approved action run, and no Undo offered once the window has passed, ADR-0023), and the 100,000-entry budget of §4.12 is measured and met.
  - **11.2 The entry panel**:
    - **Values:** grouped by page and group, with fields from older versions marked as such, plus files and the consent proof (the exact text), and a signature typed instead of drawn with its name as text beside its image, read from its PNG (ADR-0103, added in 6.2k). The panel's endpoint reads them through `EntryAnswerStore.ReadAsync` (named in 5.5j by the Phase 5 wiring sweep, ADR-0070), each value as its type read it.
    - **Metadata:** time, page, culture, member link, IP if stored, duration, spam reasons.
    - **The timeline:** events, action runs with retry, and emails.
    - **Notes:** add and edit your own.
    - **State actions:** moving an entry back into a state that holds places takes them again, and asks before overbooking an option that is full by then (§4.4, ADR-0090). Moving an entry back whose unique answer another entry holds by then asks first too (ADR-0091).
    - **Entry events** (registered in 8.4, ADR-0161): a user deleting an entry, one at a time or in bulk, publishes `EntryDeletedNotification` through `EntryEvents`, tested; a form's removal and a stored-nothing form letting go of its entry publish none.
    - **Edit mode:** validation per type, audited; a changed choice moves its place, and asks before overbooking an option that is full (ADR-0090); a changed answer that must be unique and that another entry holds asks first too (ADR-0091).
    - **Sensitive values:** masked, with an audited "reveal".
    - **Forward** (added in 1.1): send the entry to a backoffice user or to one of the team addresses of the email settings (§4.8), through the outbox. Sensitive values go along only when the user may see them and chooses to. Audited, and shown on the timeline.
    - **The entry's link** (added in 5.3e, ADR-0067): the entry's workspace (`BackofficeLinks.EntryEntityType`) answers `/umbraco/section/jfb-forms/workspace/jfb-entry/edit/<key>`, the link `entry.url` gives team mail, and opens the entry's panel without its list; a user who may not view the entry sees the backoffice's "not found" answer, as for an entry that is gone.
    - Done when: Playwright passes, and the audit entries are asserted, including Approve in the panel followed by Undo from its toast (ADR-0023), and a rejected entry moved back onto an option that is full by then, which asks first and overbooks only once confirmed (ADR-0090), and one moved back whose unique answer another entry holds by then, which asks first too (ADR-0091). The panel's endpoint's integration test stores an entry of every type and gets each value back as `EntryAnswerStore.ReadAsync` reads it, and a typed signature with its name (added in 6.2k). A forwarded entry arrives in the pickup folder, and an address outside the allowed recipients is refused by the API. Playwright opens the `entry.url` link of a mail in the pickup folder and lands on that entry's panel, and the same link as a user without access to its form shows no value of it.
  - **11.3 The all-entries inbox and spam**:
    - **The inbox:** a cross-form list with a form column and a form filter, unseen entries first. It holds the entries of the forms the user may view entries of, through the access filter of 4.6a in its query (ADR-0048).
    - **Spam:** the spam folder with "not spam", which asks first, as moving an entry back does (11.2), when an option is full or a unique answer is held by another entry by then (ADR-0090, ADR-0091).
    - **Badges:** unseen counts on the menu items, from a light count endpoint. The poll pauses while the user is idle and never renews an expired session (§4.10).
    - **The digest** (added in 1.1): each user can ask for a daily or weekly email ("12 new entries in 3 forms") instead of watching the inbox. It holds counts and links only, never entry values, covers only the forms the user may view, and goes out in the site's time zone through the outbox.
    - Done when: Playwright passes, including an idle user whose session still ends on time (`page.clock`), an inbox as the viewer that holds no entry of the restricted folder's forms, and "not spam" on an entry whose unique answer another entry holds by then, which asks first. Tests with fake time show one digest per user and period, nothing for a user who has not asked for one, no form the user may not view, and no entry value in the mail.
  - Done when: every part's "Done when" above passes, as each part says.
- [x] **11.4 Exports, insights and answer summaries, and phase review** (11.4, 11.5, 11.5b and 11.6 in one, ADR-0197) (2026-11-08, 914b66a)
  - **11.4 Exports**:
    - **Formats:**
      - **CSV:** delimiter, BOM, formula guard, headers as labels or aliases;
      - **Excel:** per ADR, either a small own XLSX writer or an MIT library; repeaters on their own sheet;
      - **JSON**, and a **printable view** of one entry and of a list;
      - **ZIP** of the files: a folder per entry, safe names.
    - **Columns:** a grid gives one column per row, and a ranking one column per position.
    - **The export dialog** remembers the last format, fields and delimiter per user and form (`jfbUserSetting`).
    - **Large exports:** a background job, a notification, and a time-limited download. Every export reads in keyset pages of 250 entries, each its own gated read, so submissions wait behind one page at most (ADR-0003).
    - **Rules:** every export respects the filters, the permissions and the sensitive-data rules, and is audited.
    - **Export selected** (moved from 11.1, ADR-0207): the entries list's bulk bar opens the export dialog for the ticked entries.
    - Done when: tests open the produced files (the XLSX with a test-only reader, CSV round trip, ZIP listing), and the 10,000-entry budget is met; Playwright ticks entries in a form's list, chooses Export selected… and finds only those entries in the file.
  - **11.5 Insights**:
    - **Collection:**
      - the runtime sends `view`, `start` and `page` beacons (`navigator.sendBeacon`) to `events`, unless insights are off;
      - when validation stops a visitor (on submit or "next"), it sends `invalid` with only the field key and the kind of error; the server also counts its own refusals;
      - the server rolls them up into daily stats;
      - **What a stored entry counts** (added in 6.1e, ADR-0088): 6.1e counts each entry the pipeline stores as `submissions` in the unit that stores it; 11.5 adds its origin page (`origin:{contentKey}`), its hour (`hour:{0-23}`, in the site's time zone) and its time to complete (`completionSeconds`) to the same unit;
      - unique members are counted from entries at query time.
    - **What it shows:**
      - views, starts, submissions and conversion;
      - spam caught, from the daily `spam` metric the pipeline already counts (added in 7.4g);
      - drop-off per page (funnel), median time to complete;
      - the fields that fail validation most, with the kind of error ("Postcode: 31% of attempts, mostly the format");
      - top origin pages, hour of day, unique members;
      - a comparison period, grouped by day, week or month;
      - a site-wide overview table.
    - **Charts** are small, accessible SVG components with a table view (no chart library).
    - **Opt-out** globally and per form (the privacy section is global, ADR-0008). The form's switch is the first setting of its Settings view's Privacy group, which this step adds to the view with its summary line (moved from 10.1a: settings come with their feature).
    - Done when: unit tests cover the aggregation, Playwright shows the beacons arriving and the charts filling on the demo (screenshots reviewed in `docs/ux/review-11.5.md`), turns a form's insights off in its Privacy group and sees its beacons stop, and a test proves the stats hold no personal data (the `invalid` beacon included: a test posts a value with it and finds it nowhere). An entry the pipeline stores counts its origin page, its hour and its time to complete in the unit that stores it, and none of them when the unit is rolled back (ADR-0088).
  - **11.5b Answer summaries** (added in 1.1 from the feature study):
    - **What it shows,** in the form's Insights view, over a date range and the entry filters of 11.1:
      - counts and percentages per option for choice and grid fields, and the average position for ranking fields;
      - average, median, lowest and highest for numbers, ratings, scales and sliders;
      - for a 0-to-10 scale, the three groups (0 to 6, 7 and 8, 9 and 10) and the recommendation score (the share of the top group minus that of the bottom one);
      - free-text answers as a list that links to their entries, never summarised.
    - **Rules:**
      - test, spam and sensitive values are left out, and so are values kept only until the actions have run;
      - only users who may view the form's entries see it;
      - computed in the database, never by loading the entries;
      - values from older form versions count under their field, with a mark where the options changed.
    - **The charts** reuse the accessible SVG components of 11.5, each with its table view.
    - **The endpoint** under §4.7 Insights.
    - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before designing the view (ADR-0019, ADR-0020).
    - Done when: unit tests cover each kind of summary against seeded entries, including multi-value fields and two form versions; the summaries over 100,000 entries load in under 1 s on SQLite; Playwright shows them on the demo, and the screenshots are reviewed in `docs/ux/review-11.5b.md`.
  - **11.6 Phase review**:
    - UX review as in 9.6 for the entries screens, plus the handover note.
    - Done when: `docs/ux/review-11.md` is committed and pushed, with the phase-review gate.
  - Done when: every part's "Done when" above passes, as each part says.

### Phase 12: Library, global settings and deployment

- [x] **12.1 The library: option lists, themes, email layouts and templates** (12.1, 12.2 and 12.3 in one, ADR-0197) (2026-11-08, 9e62b12)
  - **12.1 Option lists**:
    - A list, an editor per kind with a live preview of the options, CSV/TXT upload, the cache setting, and "used by".
    - **From 9.4b** (added in 5.5j): the editor per kind is the generic settings renderer over the option kinds' schemas that 9.4b's endpoint serves, with the same texts; a code list picks its source from the endpoint's names, and a manual list's items use 9.4's option editor.
    - Done when: Playwright passes, creating a list of every kind through the UI (a URL list against a local stub host the test site allows), and axe is clean on the list editor.
  - **12.2 Themes and email layouts**:
    - **The editor:** a theme list, and a visual token editor:
      - the tokens a base theme declares (`IFormBuilderTheme.Tokens`), with their defaults, which nothing else reads yet (ADR-0105, added in 6.8d);
      - light and dark colours, the dark ones only for a base theme whose `IFormBuilderTheme.DarkScheme` is true (ADR-0105, added in 6.8d);
      - the font family (system stacks or "use the site's font");
      - type scale, spacing, corners, borders, the focus ring, button style.
    - **The preview:** a live kitchen-sink form, with WCAG contrast checks shown in place.
    - **Custom CSS**, scoped to the form root and sanitised.
    - **Exchange and use:** import and export as JSON, the theme picker per form, and the default theme.
    - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before building the editors (ADR-0019, ADR-0020).
    - **Email layouts:**
      - Logo (media picker), colours, header and footer per culture, preview with a sample entry in light and dark, and a test send.
    - Done when:
      - Playwright and contrast unit tests pass;
      - a test shows the editor listing a site's own base theme's `Tokens` with their defaults, and offering dark colours only where its `DarkScheme` is true;
      - Playwright covers the email layout editor, and its test email arrives in the pickup folder.
  - **12.3 Templates**:
    - **Built-in templates,** in en and de:
      - contact, callback;
      - newsletter (with verification);
      - event registration (multi-page, entry limit, sessions with place limits);
      - job application (uploads, approval);
      - feedback with NPS (a 0-to-10 scale and a grid);
      - quote request (calculations);
      - support request;
      - privacy request (added in 1.1): a visitor asks for a copy or the deletion of their data, and the entry goes to the people who may use the privacy tools (13.2).
    - "Save as template", and the gallery with previews.
    - **Saved groups** (added in 1.1): save a group of fields (an address block, a standard consent) as a template of kind `Group` (Save group as template… in the selected group's "…" on the canvas, ADR-0169), and insert it into any form from the designer's palette, as a copy with new keys, so every form stays self-contained for versions and exports.
    - Done when: a test creates and publishes a form from every template without form-check errors, and the gallery screenshots are reviewed in `docs/ux/review-12.3.md`. Playwright saves a group, inserts it into another form, and a test shows the copy's new keys and remapped rules.
  - Done when: every part's "Done when" above passes, as each part says.
- [x] **12.4 Global settings and access, deliveries and the audit log, forms as files, and phase review** (12.4a, 12.4b, 12.5 and 12.6 in one, ADR-0197) (2026-11-09, ad8b295)
  - **12.4a Global settings and access**:
    - **Settings:** the sections of §4.8 with host locks, for the features that exist by now. The privacy section's retention and IP settings come with 13.1 and 13.3 ("Settings come with their feature"). The captcha consent mode's Cookiebot category is a switch between necessary, the default, and marketing, and says that Cookiebot files a captcha's cookie as necessary (the owner, 2026-11-06, ADR-0199).
    - **Scopes and the time zone** (ADR-0043): a site scope must name a root content node, and a save for any other key is refused with nothing written. The time zone is picked from a list of zones (the browser's `Intl.supportedValuesOf('timeZone')`), so nobody types its spelling, and a zone the server refuses shows its message at the field.
    - **Access:** a permissions editor (a matrix of groups and permissions, with folder and form overrides).
    - Done when: Playwright passes per section, the permission matrix covers the new endpoints, and a test enumerates every setting and fails unless each has a screen (or is marked host-only) and is read by product code outside the settings module. A site save whose key names no root content is refused, and a time zone picked from the list saves (ADR-0043).
  - **12.4b Deliveries and the audit log**:
    - **Deliveries:** emails and webhook runs, with filters and retry. A retry of an `Uncertain` run asks first and says that it may deliver twice; a `Limited` mail offers no retry and says which limit held it back; while sending is paused, the screen says so once (§4.4).
    - **The audit log viewer,** with filters and export.
    - Done when: Playwright passes for both screens, including the confirmation before an `Uncertain` retry and the paused notice, and the permission matrix covers the new endpoints.
  - **12.5 Forms as files, import and export**:
    - **Import and export:** the JSON import UI, with "new copy" or "replace".
    - **Mirror mode (D25):** write on publish, import on start-up, and detect conflicts (both file and database changed: keep the database, warn in the health check, a probe of its own as 9.1's are, ADR-0165). Documented.
    - Done when: integration tests cover the mirror round trip and the conflict case.
  - **12.6 Phase review**:
    - UX review as in 9.6 for the library and settings screens, plus the handover note.
    - **The setup checklist end to end:** on a freshly reset demo, Playwright follows the dashboard checklist (9.1) through every item and sees each one turn green.
    - Done when: `docs/ux/review-12.md` is committed, the checklist run passes, and everything is pushed, with the phase-review gate.
  - Done when: every part's "Done when" above passes, as each part says.

### Phase 13: Privacy, retention and compliance

- [x] **13.1 Privacy: retention, data subject tools, sensitive data and IP addresses, and phase review** (13.1, 13.2, 13.3 and 13.4 in one, ADR-0197) (2026-11-10, 41d92b38)
  - **13.1 Retention**:
    - **Rules:** days per state per form, with global defaults. `PurgeAfterUtc` is computed on every state change.
    - **A form that keeps entries for a number of days** (added in 6.8i by the Phase 6 wiring sweep): `settings.submission.store: days` with `storeDays` (§4.3, ADR-0002) has only its save check so far. It caps what the rules above give an entry of that form, in every state.
    - **A legal hold:** an entry can be kept with a reason and an end date.
    - **The job** deletes in batches, files and emails included, and offers a dry-run preview (counts per form). Everything is audited.
      - **What a dead process leaves in the file store** (ADR-0041): the job also deletes `.partial` files, `.deleted-` files and stored files that no `jfbEntryFile` row names, once they are a day old.
    - **The screens:** the retention settings (global and per form) with the dry run, now that the engine exists. The form's are its days per state in its Settings view's Privacy group and the choice "For a number of days" of Store entries in its Submission group (moved from 10.1a: settings come with their feature).
    - Done when: tests with fake time pass, including holds and a form's `storeDays` shorter and longer than its states' days, and Playwright changes a retention setting and sees the dry run follow it. Playwright sets a form's days per state in its Privacy group and Store entries "For a number of days", and each says its validation message for a number out of range. A test leaves a partial file, a deleted file's bytes and a stored file without a row, each older than a day, next to younger ones, and the job deletes exactly the three old ones.
  - **13.2 Data subject tools**:
    - **Find:** by email (email-type fields and member email) or by member, across all forms.
    - **Export:** everything as a ZIP (JSON per entry, files, consent texts).
    - **Erase:** delete, or anonymise (values replaced, files removed, stats kept), with a preview first. Audited.
      - **Every copy is in scope:**
        - entry values and files;
        - `SearchText`, `IpAddress`, `Context` and consent links;
        - the entry's events (old and new values) and notes;
        - queued and sent emails (addresses and bodies);
        - action-run error messages;
        - drafts.
    - Done when: tests and Playwright pass, including a test that searches **every Jiron Form Builder table** for the person's email address after an erase and finds nothing, and a test that an anonymised entry gives its places back (ADR-0090).
  - **13.3 Sensitive data and IP addresses**:
    - **Masking** in the UI, emails, exports and logs; the "view sensitive data" permission; audited reveals.
    - **IP modes:** none, anonymised (IPv4 /24, IPv6 /48) and full (only when the host allows it), with their settings screen: the site's, and the form's in its Settings view's Privacy group (moved from 10.1a: settings come with their feature). A form that stores `ip: full` on a host that does not allow it gets the form check's warning that the host replaces a value the form stores (`lock_replaces_value`, ADR-0008, ADR-0180; added in 9.5e).
    - **Values kept only until the actions have run** (§4.4, built in 6.1 and 8.1): audited end to end. The entry panel, exports and the privacy export show such a field as "not kept", and the designer explains the setting next to "sensitive".
    - Done when: tests pass, including captured logs, and a test searches every Jiron Form Builder table for a value kept only until the actions have run, once no trigger can still fire for its entry and every run is final, and finds nothing. A unit test finds the form check's `lock_replaces_value` warning on a form that stores `ip: full` on a host that does not allow it. Playwright sets a form's IP handling in its Privacy group, and an entry it then takes keeps the address that way.
  - **13.4 Privacy documentation and phase review**:
    - Write `docs/privacy/`:
      - **a facts sheet:** what Jiron Form Builder stores, where and for how long; that it sets no cookies; the browser storage key used only by "save and resume"; third parties only when a captcha provider is chosen;
      - a checklist for site owners;
      - privacy-policy section templates in en and de. **Each starts with "TEMPLATE, not legal advice; adapt with your data protection adviser".**
    - **Phase review:**
      - A subagent acts as a privacy reviewer (GDPR, TDDDG, revDSG, UWG §7 double opt-in, the European Accessibility Act) against the running demo and the docs. Fix what it finds, and record open legal judgements in OWNER-TODO.
      - Write the handover note.
    - Done when:
      - a test finds that no workspace of the section still shows the stand-in of 9.1 (ADR-0165);
      - committed, and a test compares the storage keys and requests the runtime makes with the facts sheet;
      - pushed, with the phase-review gate.
  - Done when: every part's "Done when" above passes, as each part says.

### Phase 14: Languages, members and multi-site

- [x] **14.1 Languages, members and multi-site, and phase review** (14.1, 14.2, 14.3 and 14.4 in one, ADR-0197) (2026-11-11, 1d2acdcf)
  - **14.1 Culture variants and backoffice localisation**:
    - **Texts:** every `LocalizedText` in the designer, settings, emails and outcomes, with the culture switcher, "missing translation" marks, `#dictionary` keys (with a picker) and fallback.
    - **Entries** keep their culture, and exports format dates and numbers by it.
    - **Backoffice localisation:**
      - Audit that every backoffice string in en and de goes through localisation keys (each UI step added its keys, see "How to use").
      - **Every key is used** (carried in from 9.6b's wiring sweep): the dictionaries' check holds each area to the keys its code uses, as the section's, library's, designer's and form check's areas are held already; the inspector's, the rules' and the merge tags' areas join them, with their keys built from a prefix (a setting's, a message's, an operator's, a merge tag path's) taken from the lists that name them.
    - Done when:
      - Playwright runs a form end to end in en and de, including the emails;
      - the dictionaries' check fails for a key no code uses, in every area of the backoffice's dictionaries;
      - a lint rule that fails on hard-coded UI strings passes, and the German screenshots are reviewed in `docs/ux/review-14.1.md`.
  - **14.2 Visitor texts in seven languages**:
    - Add French, Italian, Dutch, Spanish and Danish to the runtime and server messages, the words merge tags show yes and no as (ADR-0066), and the option presets (English and German came with 6.2g, 6.4d, 5.3d and 5.5). All seven are overridable per site and per form.
    - Done when:
      - a test fails when any key is missing in any language, and both runtimes pass `tests/vectors/messages.json` in all seven (added in 6.2g's split);
      - a review subagent per language reads the texts in context (screenshots) and its fixes are applied.
  - **14.3 Members**:
    - **Access:** members-only forms and member groups, one entry per member, whose switch sits in the Settings view's Availability group (moved from 10.1a: the form check reads it, nothing at submission does yet); the `options` endpoint answers a members-only form's options only to a member the form admits (added in 6.1c, ADR-0086).
    - **Prefill** (show and tamper-proof hide), and the member link on entries.
    - **An anonymous form** (added in 13.4 by the privacy review, ADR-0226): a switch in the Settings view's Privacy group that keeps no member on the form's entries, refused by the form check together with members only or one entry per member, which need the link; the facts sheet and the checklist then say so.
    - **Member deletion:** a handler deletes or anonymises the deleted member's entries, per a setting.
    - **Entries on the member:**
      - A workspace view on the member editor lists that member's entries, with permissions applied.
    - Done when:
      - tests and Playwright pass with the seeded members, including the entries view on the member, and Playwright turns on "One entry per member" in a form's Availability group and a member's second entry is refused;
      - a member signed in sends an anonymous form and its entry keeps no member, and the form check refuses an anonymous form with one entry per member (added in 13.4, ADR-0226);
      - the `options` endpoint gives a members-only form's options to a member the form admits and none to an anonymous request or a member it does not admit (added in 6.1c, ADR-0086);
      - a members-only post with another member's challenge, without a challenge, with a challenge fetched without the member's cookie, or from a page on another origin is refused on both paths; a members-only page refuses to be framed by another origin; a repeated no-JS post leads to the first entry's outcome without a second entry, and an expired challenge renders the form again with its answers; a cross-origin or `Origin: null` post with a bad challenge, over the per-address limit, or with validation errors is refused without its answers; a server-key submission to a members-only form answers `member_required`; a no-JS post faster than the minimum fill time is accepted; no Jiron Form Builder response sets a cookie.
  - **14.4 Multi-site and phase review**:
    - **Per-site settings:** sender, base URL, defaults and theme, resolved by the request's root or host. Links in emails follow the site. A root deleted for good takes its settings rows with it (ADR-0043).
    - **Protection stays out of a request's hands** (7.4a, ADR-0147): a client chooses its `Host` as it chooses a page, so a host's site protects a form only where a page of it holds the form, and otherwise the strictest of the sites does.
    - **Phase review:** write the handover note.
    - Done when:
      - integration and Playwright tests pass with two roots on `site-a.localhost` and `site-b.localhost` (loopback by themselves in Edge and Chromium, with Umbraco domains on each root; never edit the system hosts file). Playwright's API request context resolves `*.localhost` to IPv6 and reaches the default host instead, so API calls in these tests send the `Host` header explicitly (§3.3);
      - a root deleted for good leaves no `jfbSetting` rows, and the other root's rows stay (ADR-0043);
      - pushed, with the phase-review gate.
  - Done when: every part's "Done when" above passes, as each part says.

### Phase 15: Extensibility, samples and the AI add-on

- [x] **15.1 Extensibility, samples and the AI add-on, and phase review** (15.1, 15.2 and 15.3 in one, ADR-0197) (2026-11-11, 2398af89)
  - **15.1 The public C# surface and backoffice extension points**:
    - Complete the services and notifications of §4.9, including `IFormBuilderEntryReader`. Write `docs/extending.md`.
    - **Backoffice extension points:**
      - **Manifests:** `jfbSettingEditor` (a custom editor for one setting) and `jfbFieldPreview` (the designer preview of a custom field type).
      - **The headless element:** a site's own field type registers the control `<jfb-form>` writes for it, from the type's part of the element endpoint's answer (ADR-0128).
      - **Slots:** extra workspace views on the form workspace.
      - **Types:** TypeScript declarations for extension authors.
    - Done when:
      - a public-API snapshot test lists every public type and member and fails on unreviewed changes;
      - a sample extension registers a custom setting editor and a preview, and Playwright shows both working.
  - **15.2 Samples**:
    - `samples/Jiron.Umbraco.FormBuilder.Samples`:
      - a custom field type (a colour choice with swatches), rendered on a Razor page and through `<jfb-form>`;
      - a custom action (a CRM stub);
      - an option source (a fake product API);
      - an export format (iCalendar for event registrations);
      - a captcha provider (a simple arithmetic check);
      - a spam check (link density);
      - a merge-tag source (values from a site-settings node);
      - a file scanner (a stub that quarantines files named "eicar");
      - a file store for blob storage on Umbraco's official Azure Blob provider, per ADR-0006 (a private container, `ContainerRootPath` set, blocks committed only when complete);
      - a notification handler that adds a field error.

      Each is compiled and tested in CI and documented in `docs/extending.md`.
    - Done when: the samples build and their tests pass; the blob file store passes the store's contract tests against a local storage emulator (a dev-only tool).
  - **15.3 The AI add-on and phase review**:
    - **First, a spike on Umbraco.AI:**
      - Confirm the Umbraco.AI 17.x chat API (`IAIChatService`) and structured output.
      - Port Tandem's fake provider (`tests/…Testing/FakeAI`) for tests. **No real key, ever (D22).**
      - Write ADR-0018 "The AI add-on".
    - **The package** `Jiron.Umbraco.FormBuilder.AI`:
      - **Draft with AI:** a description, or the pasted text of an existing questionnaire, becomes a draft definition, validated by the structural validator, never published automatically, and marked "drafted by AI, please review".
      - **Translate:** a form's texts into another culture, with a preview and apply.
      - **Suggest:** help texts and accessibility improvements.
    - **Plumbing:** a profile picker and a health check. Editors only.
    - **No entry data ever reaches the AI;** a test asserts the prompts contain no entry values.
    - Docs. OWNER-TODO: try it with a real provider.
    - **Phase review:** write the handover note.
    - Done when:
      - the ADR is committed and a spike test drafts a form definition through the fake provider;
      - tests with the fake provider pass, and Playwright shows the three features in the designer;
      - pushed, with the phase-review gate.
  - Done when: every part's "Done when" above passes, as each part says.

### Phase 16: Hardening

- [x] **16.1 Hardening: security, performance and load, accessibility and resilience** (16.1, 16.2, 16.3 and 16.4 in one, ADR-0197) (2026-11-12, 34cfee0e)
  - **16.1 Security review**:
    - A STRIDE pass over the architecture, and an OWASP ASVS-style checklist on the code:
      - XSS, injection, authorisation on every endpoint, CSRF posture;
      - SSRF in webhooks and option lists, open redirects in outcomes;
      - uploads, DoS and limits, secrets.
    - Run `dotnet list package --vulnerable --include-transitive` and `npm audit --omit=dev`.
    - Fix the findings, with a test for each. An advisory in an upstream package that Jiron Form Builder cannot fix gets an ADR and a reviewed suppression in `NuGetAudit.props` (the Tandem ADR-0014 pattern), never a silent ignore.
    - Done when: `docs/security-review.md` exists and no high or critical findings are open.
  - **16.2 Performance and load**:
    - A Node load script (no copyleft tools) covers:
      - 200 submissions per minute with uploads;
      - lists, filters and exports over 100,000 entries;
      - render throughput;
      - the designer with 300 fields.
    - Check the query plans.
    - Done when: `docs/performance.md` has the numbers and §4.12 is met (or an ADR explains why not).
  - **16.3 Accessibility audit**:
    - **Tools:** axe on every theme × every field type, and on every backoffice screen.
    - **Automated checks** for what a person would otherwise check by hand:
      - Playwright accessibility snapshots for names, roles and states;
      - keyboard-only journeys;
      - `emulateMedia({ forcedColors: 'active' })` for Windows forced-colours mode;
      - 640 and 320 CSS-pixel viewports for reflow at 200% and 400% zoom;
      - reduced motion.
    - **For site owners:** an accessibility-statement template, labelled as a template.
    - **For buyers** (added in 1.1): `docs/accessibility.md` has a table of every WCAG 2.2 A and AA success criterion for the default theme and the backoffice, saying for each whether it applies, how it was tested (which suite, or "by hand", or "not tested") and any known gap. It states what was tested and never claims conformance (§3.7).
    - Done when: there are zero serious or critical findings, and `docs/accessibility.md` exists with the table, and a test fails when a criterion of WCAG 2.2 A or AA is missing from it. A real screen-reader test (NVDA, VoiceOver) goes to OWNER-TODO.
  - **16.4 Resilience**:
    - Cover:
      - SMTP down (no retries burnt), webhook timeouts;
      - transient database errors (retry the outermost unit of work, Tandem ADR-0065);
      - a host restart in the middle of an action run (`Uncertain`, visible and retryable);
      - a late cache refresher, double processing under load balancing;
      - the network dropping during a submission (the receipt check);
      - blocked browser storage;
      - one broken form on a page not breaking the others.
    - Done when: each case has a test.
  - Done when: every part's "Done when" above passes, as each part says.
- [x] **16.5 Upgrade tests, the UX polish pass and code quality, and phase review** (16.5, 16.6 and 16.7 in one, ADR-0197) (2026-11-13, 463a9639)
  - **16.5 Upgrade and migration tests** (this step builds the install harness that 18.3 reuses):
    - **`scripts/install-test.ps1`:**
      - creates a fresh Umbraco 17 site (SQLite, unattended, generated credentials, the private template hive) under `%TEMP%\jfb-install-<timestamp>` on port 5530;
      - takes Jiron Form Builder packages only from the local feed (package source mapping), with a fresh package cache in `%TEMP%\jfb-nuget-<timestamp>`;
      - installs the given Jiron Form Builder version;
      - runs the site with `Umbraco:CMS:Global:UseHttps=false` (plain http) and, for upgrades, `Umbraco:CMS:Unattended:UpgradeUnattended=true`;
      - reads the expected migration state from the plan in the code, never a hard-coded value;
      - runs its builds and packs with the throwaway-build settings of CLAUDE.md rule 6, and removes worktrees with `fs.rmSync` plus `git worktree prune` (MAX_PATH);
      - writes its site's PID to a file under `.local/` and stops the whole process tree by that PID (`taskkill /T /F /PID`), also when a step fails. Tandem's stopped only the one process, and had no build-server settings.
    - **The upgrade path:**
      1. With `git worktree add`, check out `baseline/schema-v1` (4.8) into `%TEMP%\jfb-baseline-<timestamp>`, and pack it as `17.0.0-alpha.0.baseline`.
      2. Install it, and create the data the baseline supports: forms with versions, folders, settings, and entries with values and files. Seed it through a small console built against the baseline's Core services (the baseline has no submission pipeline yet), or as SQL through Node's built-in `node:sqlite`.
      3. Upgrade to the current build packed as `17.0.0-dev.<yyyyMMddHHmm>`, start it again, and run smoke checks.
    - **Fixtures:** a fixture per released schema version, stored as JSON rows rather than a database file so it runs on SQLite and SQL Server (Tandem ADR-0067), plus definition-upgrader fixtures.
    - Done when: the upgrade test passes, the temp folders and worktree are removed, no process of the run is left (checked by its PID file), and the process is documented in `docs/upgrading.md`.
  - **16.6 UX polish pass** (D18, §4.13):
    - **Walk the full journeys** with screenshots at 375, 768 and 1440 px, light and dark, en and de:
      - an editor builds and publishes a multi-page form with rules and actions;
      - an entry handler works through the inbox;
      - an admin sets permissions and retention;
      - a visitor fills in a form on a phone;
      - a privacy officer erases a person's data.
    - **Carried in from 14.1's German walk** (docs/ux/review-14.1.md): at 375 px, Availability's two date boxes and the retention days side by side, an action card's controls beside its text, the rule builder's subject select and „wenn“ on one row, and a German dialog button on two lines; and the walk sets the user's language on the server too, so the texts the server writes (the Overview's alert, the digest's preview, the email layout's sample) show in German.
    - **Carried in from 15.3's look** (docs/ux/review-15.3.md): the Design view's outer scroll area overflows by about a thousand pixels at 1440 px, before any selection, so a scroll into view can shift the palette, the canvas and the inspector together; find what overflows it and keep the view to its own height, with a test that measures the area's overflow.
    - **Critique and simplify:** rerun all three design skills (ADR-0019, ADR-0020): `design-taste-frontend`, an `impeccable` critique and audit, and an `emil-design-eng` review; a subagent with the §4.13 checklist may add a fourth view. Fix every finding that is not purely taste. Count the clicks for the top five tasks per role and remove the unnecessary ones.
    - Done when: `docs/ux/review-16.6.md` lists the findings and fixes (a section per skill) with before and after screenshots, and the E2E suites are still green.
  - **16.7 Code quality and phase review**:
    - **Code quality pass:**
      - Analyzers clean, no dead code. Everything not meant for extension is `internal`. XML docs on every public type. Consistent naming.
    - Write the handover note.
    - Done when:
      - `dotnet format --verify-no-changes` and the builds are clean, and the public API snapshot is reviewed;
      - pushed, with the phase-review gate.
  - Done when: every part's "Done when" above passes, as each part says.

### Phase 17: Branding, documentation, screenshots

- [x] **17.1 Logo and icon, documentation and screenshots, and phase review** (17.1, 17.2a, 17.2b and 17.3 in one, ADR-0197) (2026-11-13, 45c03bb7)
  - **17.1 Logo and icon**:
    - **Design `assets/logo.svg`:**
      - **Concept:** a minimal form card (stacked field lines) whose submit button is a small check mark, drawn to sit next to the "Jiron" wordmark and to work alone as a 32 px section icon.
      - **Shapes and colours:** simple geometric shapes, legible at 32 px, with two brand colours and a neutral defined in `assets/brand.md`.
      - **No Umbraco logo, no "U" mark, and no resemblance to any other product's mark.**
    - Also design a monochrome variant and the section icon (registered through the `icons` manifest).
    - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before the logo work starts (ADR-0019, ADR-0020).
    - `scripts/icons.mjs` renders PNGs with Playwright (no new dependency): 512, 256, 128, 64.
    - Done when: the PNGs exist, the 128 px icon is checked visually (show the screenshot), and the brand doc is committed.
  - **17.2a Documentation for editors and site owners, and the READMEs**:
    - Under `docs/`:
      - **Start:** a `README` index, `getting-started.md` (install to first entry in 10 minutes), `installation.md`.
      - **For editors:** `editor-guide.md`, `field-types.md`, `rules-and-validation.md`, `merge-tags.md`, `actions.md`, `entries-and-exports.md`, `insights.md`, `themes.md` (the theme editor).
      - **For site owners:** `spam-protection.md`, `files.md`, `email.md`, `privacy.md`, `permissions.md`, `multi-language.md`, `members.md`, `multi-site.md`.
      - **Licence:** `license-faq.md`, plain words, clearly marked as not legal advice and pointing to `LICENSE.md` (OWNER-TODO: lawyer review). It answers at least: using it on client sites, changing it for your own projects, hosting, add-ons, contributing through forks and pull requests, and why publishing your own version (free or paid, open source or not) is not allowed.
    - Each page is checked against the running demo.
    - **"Learn more" in the backoffice (§4.13, `docs/ux/copy.md`; carried in from 9.6a):** each setting and notice whose help needs more than two sentences links to its page here, with link text that makes sense on its own, from one map of page addresses in the client.
    - **README and NuGet readme:**
      - **`README.md` (GitHub):**
        - **About:** the pitch, the features, screenshots.
        - **Getting started:** a quick start, the requirements (Umbraco 17.6+), a link to the docs.
        - **Licence:** "free to use and change, including commercially; no selling, no published versions of your own (free or paid, open source or not), no competing products; source-available" with a link to the licence FAQ, never called "open source".
        - **Notices:** the trademark notice and how to contribute.
        - **Credits:** "Made by Manassés Zähnler · Ligata", linking jiron.dev and ligata.ch.
      - **`docs/README.nuget.md`:** shorter, with no relative links.
    - Done when:
      - a link-check script passes, and a subagent reads these pages as a new editor and its questions are answered;
      - a test fails when a backoffice "Learn more" link names a page or heading that does not exist;
      - both are committed and render correctly, checked in Edge through a local GitHub-flavoured Markdown renderer (dev-only, MIT), because the private repository can't be viewed from here. The owner checks them on GitHub once public (OWNER-TODO).
  - **17.2b Documentation for developers**:
    - Under `docs/`:
      - **Placement:** `rendering.md` (Razor, tag helpers, blocks, overriding theme views, CSS custom properties).
      - **Headless:** `headless.md` (API, element, types, the Delivery API).
      - **Integration:** webhook signature verification in C#, Node and PHP (in `actions.md`), and `deployment.md` (the mirror).
      - **Reference:** `configuration.md`, generated from the settings types, with a test that fails when the docs are stale.
      - **Internals:** `extending.md`, `upgrading.md`, `troubleshooting.md`, `faq.md`, `security.md`, `database.md`, `architecture.md` (updated).
    - Done when: the link check and the configuration reference test pass, and a subagent reads these pages as a new developer and its questions are answered.
  - **17.3 Screenshots and phase review**:
    - `scripts/screenshots.mjs` (Playwright on the demo, with seeded data) takes desktop and phone shots:
      - **the backoffice:**
        - the setup dashboard, the designer with the inspector, the rules builder;
        - actions with the email preview, preview, the history diff;
        - the entries list and panel, the inbox, insights;
        - the theme editor, settings, privacy tools;
      - **the website:** forms in each theme, on a phone, the error summary, the success state.
    - Output goes to `docs/screenshots/` as optimised PNGs (16:9 for the Marketplace).
    - **Phase review:** write the handover note.
    - Done when:
      - the images are committed and referenced in the docs and the README;
      - pushed, with the phase-review gate.
  - Done when: every part's "Done when" above passes, as each part says.

### Phase 18: Packaging, CI/CD, Marketplace readiness

- [x] **18.1 Packaging, the release workflow, the install test and the first prerelease, and phase review** (18.1, 18.2, 18.3 and 18.4 in one, ADR-0197) (2026-11-13, 73a35637)
  - **18.1 NuGet metadata and `umbraco-marketplace.json`**:
    - **On every package:**
      - `PackageId`, `Title`, `Description`, `Authors`, `Copyright`, `PackageProjectUrl`, `RepositoryUrl`;
      - `PackageLicenseFile LICENSE.md` (non-OSI licences cannot use an expression, §3.5), `PackageRequireLicenseAcceptance true`;
      - `PackageReadmeFile`, `PackageIcon` (icon-128.png);
      - `PackageTags`: the meta package and the AI add-on (both installed by sites) get `umbraco umbraco-marketplace forms form-builder contact-form`; Core and Web get `umbraco` only;
      - the meta package references `Umbraco.Cms.Web.Website` `[17.6.0, 18.0.0)` directly (§3.5);
      - SourceLink, `snupkg`, `EnablePackageValidation`, `ContinuousIntegrationBuild` in CI.
    - The appsettings JSON schema ships as ADR-0008 decides: the Core package's `buildTransitive` props file adds `appsettings-schema.Jiron.Umbraco.FormBuilder.json` to `UmbracoJsonSchemaFiles`. Umbraco.Cms.Targets then copies it into the site's project folder and adds a reference to it to the site's `appsettings-schema.json` before every build (its `CopyUmbracoJsonSchemaFiles` and `AddUmbracoJsonSchemaReferences` targets, confirmed in 17.6.2, 17.7.1 and 18.2.1 in 1.8).
    - **`umbraco-marketplace.json`:**
      - **Fill it in from the schema:**
        - `Title` "Jiron Form Builder", `Description`;
        - `Category` "Editor Tools" and `AlternateCategory` "Campaign & Marketing" (there is no forms category; §3.5);
        - `AuthorDetails` (Name "Jiron"; the description names Manassés Zähnler and Ligata, with links to jiron.dev and ligata.ch);
        - `DocumentationUrl` (docs.jiron.dev/form-builder once the owner publishes it, until then the repository's `docs/`), `IssueTrackerUrl`;
        - `Screenshots` with raw GitHub URLs (they work once public), `Tags`;
        - `PackageType` "Package", `LicenseTypes` `["Free"]`;
        - `RelatedPackages` (the AI add-on).
      - Validate it against the downloaded schema in a script (copy in `.local/`).
    - Done when:
      - `dotnet pack` produces packages whose unzip listings are correct, and package validation passes;
      - validation passes, and the owner steps (make the repo public, run marketplace.umbraco.com/validate) are in OWNER-TODO.
  - **18.2 Release workflow**:
    - `.github/workflows/release.yml`, on tags `v*.*.*` and `workflow_dispatch`, in Tandem's final shape (Tandem ADR-0077):
      - a `prepare` job checks the version against the branch's Umbraco major and its `CHANGELOG.md` section, so a missing section fails in a minute;
      - build and test by calling `ci.yml` through `workflow_call` (copied CI steps drift), then the npm build. `ci.yml` gains the `workflow_call` trigger and drops its own `v*` tag trigger, which 3.5 set until this workflow exists, so a tag runs CI once (ADR-0032);
      - `dotnet pack -p:Version=${tag#v}` through the one `scripts/pack.mjs` that the gate and CI use too;
      - attach the nupkgs and snupkgs to a **draft** GitHub Release with changelog notes;
      - a `publish` job with `if: vars.JFB_NUGET_PUBLISH == 'true'`, `permissions: id-token: write`, `NuGet/login@v1` and `dotnet nuget push --skip-duplicate`.
    - The publish job stays disabled; no nuget.org policy exists.
    - Document the owner's steps to turn it on in `docs/releasing.md`.
    - Done when: the workflow check passes, and a dry run of the pack steps in `scripts/ci-local.ps1` produces the same files.
  - **18.3 Install test from packages**:
    - With `scripts/install-test.ps1` (16.5), create a fresh Umbraco 17.7.1 site on 5530 that installs `Jiron.Umbraco.FormBuilder` (Jiron Form Builder packages only from the local feed).
    - **Smoke:** the section is visible to the admin; a form is created through the management API and placed on a page; it renders, submits and emails (pickup); the entry shows in the backoffice; after the build, the site's `appsettings-schema.json` references `appsettings-schema.Jiron.Umbraco.FormBuilder.json` and the file is next to it (18.1).
    - Run the same smoke after `dotnet publish` of that site (static assets, §3.2).
    - **The supported floor:** run the same smoke on a fresh **Umbraco 17.6.0** site (its own template hive), because D3 promises `[17.6.0, 18.0.0)`.
    - **An upgrade on the floor:** on that 17.6.0 site, update the packages to a second build with a higher version, restart, and check in a signed-in browser that keeps its cache that the backoffice runs the new code without a hard refresh. 17.6 busts package files by the manifest's stamped `version` (§3.4), and Umbraco sends them without caching headers, so this checks both.
    - Done when: the smoke runs pass on 17.7.1 (run and published) and on 17.6.0 against the installed packages, and the 17.6.0 upgrade check passes.
  - **18.4 First prerelease and phase review**:
    - Set the version to `17.0.0-alpha.1`. Update CHANGELOG and `docs/upgrading.md`.
    - Tag `v17.0.0-alpha.1` and push the tag. The release workflow builds a draft release; nothing is published.
    - **Phase review:** write the handover note.
    - Done when:
      - the tag is pushed and the local pack of the same version passes 18.3. You can't watch Actions, so add "check the draft release" to OWNER-TODO;
      - pushed, with the phase-review gate.
  - Done when: every part's "Done when" above passes, as each part says.

### Phase 19: The Umbraco 18 line (forward compatibility)

- [x] **19.1 The Umbraco 18 line: branch, build and everything on 18, and phase review** (19.1 and 19.2 in one, ADR-0197) (2026-11-14, e54d894c)
  - **19.1 Branch strategy and an 18 build**:
    - As decided in D16: `main` stays the 17 line and holds every ROADMAP and WORKLOG change. The 18 code lives only on `v18/main`, branched from `main`.
    - Tick Phase 19 steps on `main` only, after pushing `v18/main`, with the `v18/main` commit hash.
    - Work on `v18/main` through `git worktree` in `%TEMP%\jfb-v18`, so `main`'s working folder never switches branches mid-run.
    - **One-way merges** (Tandem ADR-0078): `main` is merged into `v18/main` after every later change to `main`, and never the other way. The two lines share ports, so only one line's gate runs at a time.
    - On `v18/main`, set CMS `[18.1.0, 19.0.0)` (18.1.0 is the first 18.x whose runtime cache clears reliably, ADR-0074), `@umbraco-cms/backoffice` 18.x, Umbraco.AI 18.x for the add-on, and the version `18.0.0-alpha.0`.
    - **The OpenAPI port:**
      - move the registration from Swashbuckle to Microsoft.AspNetCore.OpenApi (`/umbraco/openapi`);
      - add the schema transformer that keeps the client's types as on 17 (§3.4);
      - regenerate the client and the snapshots per line;
      - replace the removed APIs.
    - Done when: the solution builds on CMS 18 with zero warnings.
  - **19.2 Run everything on 18, and phase review**:
    - Create an 18 sandbox on 5540 and an 18 demo on 5541, with Umbraco.Templates 18.x in its own template hive. Run the unit, integration and E2E suites. Fix what breaks behind the adapters. Document the differences in `docs/upgrading.md`.
    - **A real 17 → 18 upgrade:** the install test (16.5) learns the line. It installs the 17 packages on an Umbraco 17 site with data, then raises Umbraco and Jiron Form Builder to 18, and checks the data and Umbraco's own upgrade state.
    - **Phase review:**
      - Write the handover note on `main`.
    - Done when:
      - all suites pass on 18, the 17 → 18 upgrade passes, and `main` is untouched by 18 code and still green;
      - `v18/main` and `main` are both pushed, and `git ls-remote origin` shows both.
  - Done when: every part's "Done when" above passes, as each part says.

### Phase 20: Final verification and handover

- [x] **20.1 Clean-clone verification and the status report** (20.1 and 20.2 in one, ADR-0197) (2026-11-14, 800520b1)
  - **20.1 Clean-clone verification**:
    - Clone `origin/main` into `%TEMP%\jfb-verify-<timestamp>`, outside the repo, so no parent `NuGet.config` or `node_modules` can leak in. Use a fresh `NUGET_PACKAGES` cache in `%TEMP%\jfb-nuget-<timestamp>`, and the throwaway-build settings of CLAUDE.md rule 6, so the folders can be deleted afterwards. The hang-marker rerun applies here too.
    - Run, in order:
      - `scripts/ci-local.ps1`: restore, build, all unit and integration tests, vectors in both runtimes, and the client lint, tests and build;
      - the full Playwright suite;
      - a pack with a unique `-dev.<timestamp>` version;
      - the install test (18.3);
      - **an upgrade** from the `v17.0.0-alpha.1` pack (18.4) to this build, with data, through the 16.5 harness;
      - `node scripts/secret-scan.mjs --history`.
    - Run `node scripts/term-guard.mjs --history` **from the working repo**, because the clone has no `.private/`.
    - Show the summary output. If this session did not run 20.1 itself (for example after a restart), run it again before claiming the goal.
    - Done when: everything passes from the clean clone, the term guard passes on the full history, and the temp folders are removed.
  - **20.2 Status report**:
    - Write `docs/STATUS.md`:
      - what was built (features against §1);
      - test counts and coverage;
      - budgets measured;
      - known limitations;
      - the final OWNER-TODO list, with clear instructions for each item.
    - Update CHANGELOG. Every roadmap step is `[x]`, or `[~]` with its reason in OWNER-TODO.
    - If the owner's private reference exists, update the private mapping in `.private/` one last time (never committed).
    - Done when: committed and pushed, and the final output of `node scripts/roadmap-status.mjs` shows `open=0 blocked_missing_in_owner_todo=0 clean=yes` with `head` equal to `origin_main`.
  - Done when: every part's "Done when" above passes, as each part says.

### Phase 21: The owner's last pass

- [x] **21.1 A last pass: every function looked at again, calmer screens, and phase review** (the owner, 2026-11-14) (2026-11-15, 81193183)
  - The owner, after 20.1: "go through all functionalities roughly again to double check your work one last time and eliminate last findings", with screens that are "plain and simple", not "too much text too much info", "without missing out on features for no reason".
  - **Look again:**
    - walk every screen in the twelve looks, and the five journeys (§4.13), on the build as it stands;
    - independent readers go through each area: the form library and the designer; a form's settings, actions, preview and history; entries and insights; the library; the settings, the privacy tools and Activity; the visitor's side in Razor, the runtime and the element; the server's pipeline, actions, storage and upgrades; the docs against the screens. The code of 8.6b comes first, as it was built after the last phase review;
    - every finding is checked before anything changes, then fixed with a test that fails without the fix, or kept with its reason.
  - **Calmer screens:**
    - the walk counts the words each screen shows, and the pass goes through the screens from the wordiest down;
    - what is said twice goes, and so does what the control beside it already says; what a person needs only once moves behind "Learn more" (the copy guide's rule). No feature, setting or state goes, and nothing a person needs to decide is hidden;
    - the three design skills run again (ADR-0019, ADR-0020): `docs/ux/review-21.1.md` has a section per skill, each changed screen before and after with its words counted, and what was kept as it is. An ADR decides what changed.
  - **Keep it true:** the copy guide, the docs, the screenshots of a changed screen, CHANGELOG and `docs/STATUS.md` follow the changes, and `v18/main` gets them by merge.
  - Done when:
    - the walk and the journeys reach every screen on the final build with nothing of the section's own from axe, and no changed screen shows more words than before;
    - every finding of the readers is in the review document, fixed with its test or kept with its reason;
    - the phase-review gate passes as the clean-clone verification of 20.1 (`node scripts/verify-clean.mjs`) on the final work commit, with Core coverage at 85% or more and the name scan at 0 hits; `docs/STATUS.md` names that run;
    - `v18/main` has the pass merged in, and its unit and client tests and the integration and browser tests of what the pass touched pass there;
    - the handover note is written, both branches are pushed, and `node scripts/roadmap-status.mjs` shows `open=0` and `clean=yes` with `head` equal to `origin_main`.

### Phase 22: A simpler backoffice

The owner tried the demo after 21.1 (2026-11-16): "I love that there's so much functionality, but it just has to be more intuitive than it currently is, more simple". What was seen, why each screen is as it is and a direction for each are in [docs/ux/simpler-backoffice.md](docs/ux/simpler-backoffice.md), whose letters (A1, D1) the steps name. The owner's five decisions are [ADR-0254](docs/decisions/0254-a-simpler-backoffice.md); the fifth is that this phase is done before the repository goes public.

- **Six rules** join §4.13 for these steps, and every wireframe and review checks them:
  1. one way to do a thing, in the place where the thing is; a second way stays only for the keyboard;
  2. pick first, details after;
  3. in the designer the form is the widest thing on the screen, and no column stands empty;
  4. what is fixed in number is never pushed out of sight by what grows;
  5. one level of folding on a screen, never a section that opens inside one;
  6. Umbraco's pattern before an own one.
- **No function goes.** Things move behind a pick. Dragging to move fields, groups and pages, every keyboard path and shortcut, WCAG 2.2 AA and the §4.12 budgets stay.
- **Every step starts on paper:** the wireframes of the screens it changes (`docs/ux/wireframes/`), then its ADR, which names each earlier decision it turns round and answers the reason that decision had. The three design skills load first (ADR-0019, ADR-0020).
- **Every step keeps true** what describes the screens it changed: `docs/ux/information-architecture.md`, the copy guide, the guides and their "Learn more" map, CHANGELOG, English and German. `main` is merged into `v18/main` after it, where the unit and client tests and the browser tests of what the step touched pass.
- **Every step is measured** against the last walk before it (`JFB_UX_BEFORE`) and against `docs/ux/top-tasks.md`, counted again: no screen shows more words than before, and no task takes more than one click more. The step's WORKLOG line names each task that grew, and why.
- **The phase is built in one stretch** (the owner, 2026-11-16, ADR-0255): each step is built on the quick checks of "How to use" and its own browser specs, and its screens are looked at before the next step starts. Every long check, in whichever step it stands (the walk, the journeys, the whole browser suite, the checks on `v18/main`, the gate), runs once on the phase's final build, and the four steps are ticked then, in order. One agent on Opus 5.5 writes the code; the main session judges it and sends it back until it holds. **The owner paused the phase after 22.1** (2026-11-17, to spare usage): 22.1 runs every check of its own and is ticked, and 22.2 to 22.4, the stretch that is left, wait for the owner's word.

- [x] **22.1 The designer: a picker to add with, pages at their tabs, three columns** (the owner, 2026-11-16, ADR-0254) (2026-11-17, 93941fea)
  - The owner: "I don't want to be able to do something in multiple ways. I just want one specific button and at a specific place"; "when I click between two things, then it should open up a pop-up and not just tell me to pick from the left again"; "I have created a page and I don't see anywhere where I could delete that page".
  - **Adding (A1 to A3):**
    - **A picker** opens from every page's "Add field" and from every insert point, at every width, as the palette does over a narrow canvas today: its search with the focus, Group, the field types in their groups, the site's saved groups, and "Paste questions…". A pick puts the element where the click was and selects it; Esc closes the picker and gives the focus back to what opened it. It is a dialog as Umbraco's block picker is: named, with the focus kept inside. The wireframe decides whether "Recently used" stays, by the clicks it saves.
    - **The palette's column and its rail go** (the first decision). Dragging a type from a list onto the form goes with them; dragging a field, a group or a page to move it stays, with "Move to…".
    - **A page is added in one place:** "Add page" in the row of pages. "New page" leaves the list of types. "Start a new page after this" stays in an element's menu alone, under a name, in English and German, that says it moves what follows onto the new page.
  - **Pages (B1, B2):**
    - **The page's tab is its handle:** a click shows the page and selects it, and the tab's menu holds Rename, Duplicate and Delete. Delete is not offered for a form's only page, and a deleted page comes back with Undo, as now.
    - **"After sending" is edited where it is shown** (the third decision): the outcomes, their order, their rules and their editor with its preview move from the Settings view to the row's last step. The Settings view loses the group, and every "Fix it" and link that led there leads here.
  - **The layout (C1 to C3):**
    - **Three columns:** the form takes the room the palette had. The inspector's column stays, so the form never moves when an element is selected (ADR-0182), and it is never empty: with no field or group selected it shows the open page, and on "After sending" the outcomes.
    - **One row of controls above the form:** the pages at its start; the width switch, the language, the form check and "…" at its end. The wireframe decides what gives way first where the row has no room, never the pages, and whether "Desktop" is offered where the canvas cannot show it.
    - **"Change type"** is offered once, in the inspector's header beside the type's name.
  - Done when:
    - by mouse, and by keyboard alone, a field is added at a page's end and between two fields through the picker, lands where the click was and is selected, and Esc gives the focus back (browser tests of both paths, in a 1440 px window and in a 620 px workspace);
    - the Design view holds no list of types beside the form, one control that adds a page and one that offers "Change type" (a test reads the view's controls);
    - a page is renamed, duplicated and deleted at its tab; a form's only page offers no Delete; Undo brings a deleted page back with its fields;
    - an outcome is added, changed, moved and deleted in "After sending" on the Design view; the Settings view holds no such group; "Fix it" on an outcome's problem opens it there;
    - the inspector shows the open page while nothing is selected, and selecting and clearing a field leaves the form's left edge and its width as they were (a layout test);
    - layout tests: in a 1440 px window the form's column has more than half the window's width under one row of controls, and in a 1920 px window "Desktop" shows the form at its full 960 px;
    - the walk reaches every screen with nothing of the section's own from axe, and no screen of the designer shows more words than in the walk before; the top tasks are counted again;
    - the focused checks of "How to use" pass, with `node scripts/size-check.mjs`; `v18/main` has the step merged in, and its checks pass there.

- [x] **22.2 The section's left side: the fixed items first, each one screen with tabs** (the owner, 2026-11-16, ADR-0254) (2026-11-18, b349e686)
  - The owner: "Everything is listed at the left hand section. So you have to scroll a lot"; "at the left you can only click on settings", and "once you're in that page, you can select the sub categories of these settings".
  - **The sidebar (D1, the second decision):** Inbox, with its count of unseen entries, Library, Settings, Privacy and Activity come first, without headings. The tree of forms follows under Umbraco's own header with its "+" and "…", the Recycle Bin last. An item shows to a user who may use any of its tabs.
  - **Each item is one screen with tabs along its top,** in Umbraco's own way (a workspace's views or a section's; the ADR decides which):
    - Inbox: Inbox · Spam;
    - Library: Option lists · Themes · Email layouts · Templates, each a list whose row opens the item's own workspace, as now, and whose back arrow returns to its tab;
    - Settings: General · Email · Spam protection · Files · Access · Integrations;
    - Privacy: People · Retention · Data collection;
    - Activity: Deliveries · Audit log.
  - **Tabs follow the permissions** as the items did (ADR-0013): a tab shows only to who may use it, the screen opens on the first tab a user may use, and an address a user may not open is refused as now.
  - **Saving:** the ADR decides what Save saves on a screen with several tabs, and the button says it. Leaving a tab, or the screen, with unsaved changes asks first, as every settings screen does today.
  - **Every address stays good:** each old screen's address opens its tab, since mails already sent and bookmarks hold them, and every link the product builds (`BackofficeLinks`, the Overview's checklist and health lines, "Fix it", the links of the Audit log and of Deliveries) names the new place.
  - **"Settings" (D2)** names Umbraco's own section, this item and a view of every form. The wireframe decides whether one of the section's two gets a name that says whose settings they are, and the copy guide records it.
  - Done when:
    - a layout test: in a window of 1440 by 900 px with fifty forms in the tree, all five items are on the screen without scrolling, and the Inbox shows its count;
    - each persona of ADR-0013 sees the items and tabs it may use and no other, lands on its first tab, and is refused at a tab's address it may not open (the permission matrix, in the browser and at the endpoints as before);
    - a test opens every old screen's address and lands on its tab, and one follows every link the product builds to a screen of the section;
    - every setting is changed and saved on its tab as before (the settings' tests pass, moved), and leaving unsaved changes asks;
    - a user without any permission still reads, in the tree's place, who can give access;
    - the walk reaches every screen with nothing of the section's own from axe, and no screen shows more words than before; the top tasks are counted again;
    - the focused checks pass, with the size check; `v18/main` has the step merged in, and its checks pass there.

- [x] **22.3 A form's views: its settings by group, an action's kind first, the form's buttons where they apply** (the owner, 2026-11-16, ADR-0254) (2026-11-18, b90da088)
  - The owner: "I don't want to be bombarded with everything at once. I just want to have to pick something, and then I go to that place, and then in that place I have the more detailed stuff".
  - **A form's Settings (E1):** the view shows its groups to pick from, each with the line a closed group has today (what is set, and its problems counted), and then the group picked, alone. Six groups since 22.1: Submission, Availability, Spam protection, Privacy, Appearance, Developer. "Fix it" opens the group with the focus on its setting.
  - **One level of folding (the fifth rule)** in a form's Settings, the inspector and an action's sheet, which draw their settings the same way: where a section that opens holds "More options" today, the wireframe decides which of the two stays, and §4.13's "at most seven settings" is kept by it.
  - **An action's kind first (E2):** "Add action" opens the kinds to pick from, each with a line, as the Create dialog does; the sheet that follows holds that kind's settings only. When it runs is one choice, with the explanation of the moment chosen.
  - **The form's buttons where they apply (C4, the fourth decision):** Preview, Save draft and Publish… show on Design, Settings and Actions, the views that change the form, and not on Entries, Insights and History. With an entry open, its own bar is the only one. Unsaved changes are kept across the views as now, and leaving the form with them asks first. The ADR answers the rule of `docs/ux/information-architecture.md` that a workspace's primary stays on every view.
  - **One door to an earlier version (E3):** the History view. "Restore an earlier version…" leaves the form's "…".
  - Done when:
    - a form's Settings show one group at a time, each one pick away by mouse and by keyboard, and "Fix it" lands on its setting in every group (a test per group);
    - an action of every kind is added by picking its kind first, and its sheet shows no other kind's settings; the actions' tests pass;
    - on Entries, Insights and History the form's three buttons are not there, and with an entry open the screen holds one primary button (a test counts them); a change made on Design is still there after a visit to Entries, and leaving the form then asks;
    - a version is restored from the History view, and the form's "…" offers no second way;
    - no screen of the section opens a section inside a section (the walk checks every screen);
    - the walk reaches every screen with nothing of the section's own from axe, and no screen shows more words than before; the top tasks are counted again;
    - the focused checks pass, with the size check; `v18/main` has the step merged in, and its checks pass there.

- [x] **22.4 The other screens, what describes the section, and phase review** (the owner, 2026-11-16, ADR-0254) (2026-11-18, 40da2811)
  - **The screens not yet looked at (F):** an entry's screen and panel, the entries' filters and exports, the option list editor, People, Retention, Deliveries, the Audit log and Insights, each asked four questions: is there a second way to do this; what could wait until something is picked; what is said twice; does the empty state say what to do next. The "Actions" column of the entries' lists speaks only where something did not go out. Every finding is in `docs/ux/review-22.md`, fixed with a test that fails without the fix or kept with its reason, with a section per design skill.
  - **What describes the section:** `docs/ux/information-architecture.md` describes the section as it now is, with its wireframes, the copy guide and the top tasks; so do the editor guide and every guide that names a screen's place, the screenshots of every changed screen (the Marketplace listings' included), CHANGELOG and `docs/STATUS.md`. ADR-0021, ADR-0165 and ADR-0170 name the records that changed them. `docs/ux/simpler-backoffice.md` says for each of its findings what became of it, and its table "How to tell that it got simpler" holds the numbers measured before and after.
  - **Phase review:** the handover note, with the wiring sweep over every screen, address and link the phase moved.
  - Done when:
    - every finding of the pass and of the draft (A1 to F) is in the review document or the draft, fixed with its test or kept with its reason, and the table's "after" column is measured, not estimated;
    - the walk and the five journeys reach every screen in the twelve looks on the final build with nothing of the section's own from axe, and no screen shows more words than in the last walk of Phase 21;
    - `node scripts/link-check.mjs` passes, and no guide names a place the section no longer has: the review document lists every place a guide names and where it is now;
    - the phase-review gate passes as the clean-clone verification of 20.1 (`node scripts/verify-clean.mjs`) on the final work commit, with Core coverage at 85% or more and the name scan at 0 hits; `docs/STATUS.md` names that run;
    - `v18/main` has the phase merged in, and its unit, client and integration tests and its whole browser suite pass there;
    - the handover note is written, both branches are pushed, and `node scripts/roadmap-status.mjs` shows `open=0` and `clean=yes` with `head` equal to `origin_main`.

### Phase 23: A field may be called anything

The owner, after 22.4 (2026-11-18), on what that step's worklog line had noted and not looked into: "you can do testing on it". A browser answers a property read on a form element with the form's control of that name, in front of the element's own properties and methods, and a field's control is named by its alias (ADR-0015). Tried on the demo that day, the contact form with one more text field: called `dispatchEvent`, the form cannot be sent with JavaScript; called `replaceWith`, its entry is stored and no outcome shows; called `dataset`, `elements`, `querySelector`, `addEventListener`, `removeEventListener`, `ownerDocument` or `hasAttribute`, the form falls back to the post without JavaScript; called `classList` or `contains`, it is sent and errors are thrown on the page. Without JavaScript every one of them is sent as it should be.

- [x] **23.1 The website's script reads a form's own properties, whatever its fields are called, and phase review** (the owner, 2026-11-18) (2026-11-19, 7ac169c7)
  - **Aliases stay free:** no name is reserved for this, and a control's name stays its alias. A list of the names to refuse would have to hold `name` and `title`, or be a guess at what scripts read; an ADR says why, and what else was tried.
  - **The script:** the runtime and the headless element never read a property of a form's root by plain access, where a control may answer; they ask the element's prototype, with the element as the receiver, through one small module. That holds for everything that can be the root: what is typed as a form, the root a fit or a notice is given, what `closest` finds, a control's `form`, and the target of a listener on the form.
  - **Kept so:** a lint rule of the client's own refuses a plain read of a form's property in the website's code.
  - **A plain object by alias:** where the website's code or the shared logic reads a plain object by an alias or another key a person chooses, a field called `constructor` or `toString` meets nothing of `Object.prototype`.
  - **Told:** the developer docs say what a site's own script meets on a form with a field called `id` or `action`, as on any HTML form; CHANGELOG and `docs/STATUS.md` follow, and `v18/main` gets it by merge.
  - Done when:
    - on a page, a form whose fields are called `id`, `dataset`, `dispatchEvent`, `replaceWith`, `querySelector`, `elements` and the others the look found, and which, through controls the test adds, has one of every name a form element's property has in that browser that could be an alias, shows its errors and is sent with JavaScript: the entry is posted with its answers by alias, the outcome shows, and nothing is thrown; the spec fails on the build before the fix;
    - the runtime's unit tests run its journeys on a form covered the same way, and fail without the fix;
    - the lint rule fails on a plain read of a form's property, with a test of its own;
    - a post without JavaScript stores the answers of fields called `id`, `action`, `submit`, `constructor` and `toString`;
    - the sizes stay inside section 4.12's budgets;
    - the phase-review gate passes as the clean-clone verification of 20.1 (`node scripts/verify-clean.mjs`) on the final work commit, with Core coverage at 85% or more and the name scan at 0 hits; `docs/STATUS.md` names that run;
    - `v18/main` has the step merged in, and its unit and client tests and the website's browser tests pass there;
    - the handover note is written, both branches are pushed, and `node scripts/roadmap-status.mjs` shows `open=0` and `clean=yes` with `head` equal to `origin_main`.

### Phase 24: The same checks in a third of the time

The owner, before the first release (2026-11-21): the tests take hours and may be more than is needed; go through them, remove or combine what is not needed, and narrow the time down, as long as a broken product is still flagged. Measured that day ([ADR-0261](docs/decisions/0261-where-the-test-time-goes.md)): the 15,500 unit and client tests take two and a half minutes; the gate's 90 minutes are the 2,313 integration tests, each starting an Umbraco, one after the other (37 minutes), and the 590 browser tests against one site (23 minutes, and twice when its database stalls). The workflow's test jobs would not have fitted their 45 minutes.

- [x] **24.1 The gate's long stages run side by side, nothing that flags a break is lost, and phase review** (the owner, 2026-11-21) (2026-11-22, f2c19a25)
  - **Integration tests in shares:** one script runs the integration assembly in several processes at once, each with a working folder and results of its own, shared out by class; the last share names no class and takes whatever the others do not name. `scripts/ci-local.ps1`, `scripts/coverage.mjs` and the workflow's SQLite and SQL Server jobs run them through it, the workflow as a matrix of jobs.
  - **Timed tests alone:** an integration test that holds the product to a time budget is marked and runs after the shares with nothing beside it.
  - **Browser tests against more than one site:** a run may start several sites, each with ports, database, mail folder, sign-in lock and credentials of its own, every spec file given to exactly one, three workers each where four each filled the cores (one site keeps four; ADR-0261); `budgets` runs once, alone, last. The hang marker and its one rerun stay. The gate records no trace, and every test host does Umbraco's start-up work at once, with the start-up wait as strict as before.
  - **Cheap stages side by side:** the gate's checks and builds that need nothing of each other run as lanes at once, every command kept and still failing the gate.
  - **What goes:** a test is removed or folded into another only where a named test still runs the same behaviour and fails when it breaks; ADR-0261 lists each with the one that stays. Unit and client tests are left alone.
  - **Told:** `tests/README.md`, `tests/e2e/README.md`, `CONTRIBUTING.md` and `docs/STATUS.md` say how the tests run now and what the gate takes; `v18/main` gets it by merge.
  - Done when:
    - the integration run fails when the tests that ran do not add up to the tests the assembly holds, and when any share fails; the script's own tests show that a class no share names runs in the last share, that no class is in two shares, and that a short count fails the run;
    - the browser run fails in the same two ways, with tests of its own for the split of the spec files;
    - the marked timed tests and the `budgets` project each run with no other test process beside them;
    - with one fault put into the product for each of four layers (the shared logic, a repository, a page a visitor gets, a backoffice screen), the gate fails in the stage that failed before the change, each fault taken out again;
    - the whole gate (`scripts/ci-local.ps1`) passes twice in a row on the development machine with no test failing in either run, and takes under 30 minutes; `docs/STATUS.md` names both times and the counts of tests before and after;
    - `node scripts/check-workflows.mjs` passes on the workflow's matrix, and no test job's share is planned to take more than half of its time limit;
    - the phase-review gate passes as the clean-clone verification of 20.1 (`node scripts/verify-clean.mjs`) on the final work commit, with Core coverage at 85% or more and the name scan at 0 hits; `docs/STATUS.md` names that run;
    - `v18/main` has the step merged in, and its gate passes there;
    - the handover note is written, both branches are pushed, and `node scripts/roadmap-status.mjs` shows `open=0` and `clean=yes` with `head` equal to `origin_main`.

### Phase 25: A group's access is found where it is looked for

The owner, before the first release (2026-11-22): on the first site they made a user group of their own for the person who will manage it, gave it the section, signed in as that user and found every place refused, naming a permission; on Umbraco's user group screen they found no switch for it. Checked on the demo that day (WORKLOG): the permissions are given in the section's own Settings › Access and work once given, but nothing leads an administrator there. The refusals say "Settings › Access" to a user whose section shows no Settings place, Umbraco's user group screen says nothing about Form Builder once the section is added, and Privacy › People, opened by its address without its permission, shows its search and answers Find with "The entries couldn't be found". The owner: "Yes, fix now".

- [x] **25.1 A refusal names where access is given, a user group's own screen shows what the group may do, and phase review** (the owner, 2026-11-22) (2026-11-22, cbcddb2f)
  - **The refusals:** every sentence that sends a user to an administrator for a permission names the place whole, Form Builder › Settings › Access, in English and German: the line of a user who sees no forms, a place or a screen that needs a permission, and what a library item or retention says.
  - **Umbraco's user group screen:** a Form Builder view beside Details, for a user who may manage permissions, on a group that exists. It says, without changing anything, what the group may do with every form, by the permissions' names, which folders and forms give it more or are its own, and that its users see an empty section while it has no permission; a group without the section is told to add it under Details first, and the administrators' group that it holds everything. One link opens Settings › Access. An ADR says why the view shows and links and does not edit.
  - **Privacy › People:** without the privacy tools permission the screen says which permission it needs, as the other screens do, and shows no search; a search the server refuses for the permission says the same.
  - **Told:** `docs/permissions.md`, the installation and getting-started guides, troubleshooting, the copy deck and the information architecture; CHANGELOG and `docs/STATUS.md` follow, and `v18/main` gets it by merge.
  - Done when:
    - in a browser, the persona with the section and no permission reads "Form Builder › Settings › Access" in the line under Forms and on a place opened by its address, in English and, as a German user, in German; no sentence of the backoffice's words in either language still sends a user to "Settings › Access" without the section's name, which a test of the words holds;
    - in a browser, an administrator opens Umbraco's user group screen of the group without grants, of the entry handlers and of the HR team, and its Form Builder view says no permission, the group's permissions by name, and the folder that is the HR team's own; its link lands on Settings › Access; a group without the section is told to add it; the view passes axe in light and dark and at a phone's width; a user with Umbraco's Users section and without the permission to manage permissions sees no such view;
    - the view's unit tests cover each of those states, a failed read with its retry, and a group that is new;
    - in a browser, the persona without the privacy tools permission opens Privacy › People by its address and reads which permission it needs, with no search field; the screen's unit tests cover that and a refused search;
    - the sizes stay inside section 4.12's budgets, and the backoffice's start bundle does not hold the view;
    - the phase-review gate passes as the clean-clone verification of 20.1 (`node scripts/verify-clean.mjs`) on the final work commit, with Core coverage at 85% or more and the name scan at 0 hits; `docs/STATUS.md` names that run;
    - `v18/main` has the step merged in, and its unit and client tests and the user group view's browser test pass there;
    - the handover note is written, both branches are pushed, and `node scripts/roadmap-status.mjs` shows `open=0` and `clean=yes` with `head` equal to `origin_main`.

### Phase 26: The workflow passes where it runs

The owner, after 17.0.0-beta.1 was tagged (2026-11-23): "Yes, fix CI". Seen that day in the owner's browser (WORKLOG): the release run of `v17.0.0-beta.1` failed in CI, as the run of `v17.0.0-alpha.1` had, so neither tag has a draft release. It was the first time the workflow's tests ran on Linux and on SQL Server; everything before ran on one Windows machine with SQLite. What failed: a script test of the lanes' PID file on Linux; four rendering fixtures whose culture data (a time pattern, the countries) Linux writes differently; a time zone the browser lists under an older name, which a Linux server refuses; the blob store's tests without their emulator; on SQL Server, queries past their budget that are not stopped as the tests expect, a date outside SQL Server's `datetime` in the answers of Insights, and the answer summaries of 100,000 entries at 3.95 s against 1 s; and the browser jobs' test host, whose fake AI is set up before the AI package has made its tables. The owner allowed Docker on the development machine for Linux runs and the download of the run's artifact.

- [x] **26.1 The workflow's tests pass on Linux and on SQL Server, a release run drafts its release, and phase review** (the owner, 2026-11-23) (2026-11-26, 58ffbe2c)
  - **Each failure at its cause:** what is a fault of the product (the time zone on a Linux server, the date on SQL Server, a budget that does not stop a query there, if that is what it is) is fixed in the product with a test that fails without the fix; what is a fault of a test or of the workflow is fixed there. No test is skipped on a system to make it pass.
  - **No test depends on the system's culture data:** a fixture or an expectation that holds what the operating system's ICU writes (date and time patterns, region names, a culture's sort order) is compared so that Windows and Linux agree, and a test of the product's own says what it does with each form the systems write.
  - **Linux on the development machine:** a script runs the workflow's Linux steps in a container (the build job's checks and tests, the integration tests on SQLite, the browser smoke tests), so a Linux failure is found before a run on GitHub; `tests/README.md` says how. It makes no SQL Server.
  - **The answer summaries' budget on SQL Server:** measured again on the workflow's runner once the rest passes; then the query is made to meet its budget, or an ADR says what the budget is on which hardware and why.
  - **Told:** CHANGELOG, `docs/STATUS.md`, `docs/OWNER-TODO.md` and the testing guides follow; `v18/main` gets it by merge.
  - Done when:
    - in a Linux container on the development machine, the build job's script tests, the client's lint, tests and build, the Release build, the unit and AI tests, every share of the integration tests on SQLite and the browser smoke tests pass, each with the command the workflow runs;
    - a Linux server saves a time zone the browser lists under an older name and gives it back as it was saved, with a test that fails without the fix on Linux;
    - the answers of Insights are read on SQL Server without a date it cannot hold, with a test of the query's parameters that runs on SQLite too;
    - a dry run of the release workflow on GitHub (Run workflow) passes every CI job, the SQL Server shares and load balancing among them, and packs; its run is read in the owner's browser and its job times are in `docs/STATUS.md`;
    - the tag of the next prerelease is pushed, its release run passes and drafts a release with seven files, read in the owner's browser;
    - the phase-review gate passes as the clean-clone verification of 20.1 (`node scripts/verify-clean.mjs`) on the final work commit, with Core coverage at 85% or more and the name scan at 0 hits; `docs/STATUS.md` names that run;
    - `v18/main` has the step merged in, and its unit and client tests pass there;
    - the handover note is written, both branches are pushed, and `node scripts/roadmap-status.mjs` shows `open=0` and `clean=yes` with `head` equal to `origin_main`.
  - **Unblocked on 2026-11-25:** the owner made the repository public, where the runs cost nothing. The tag `v17.0.0-beta.2` was pushed that evening; its release run passed all 26 jobs and drafted a release with seven files, read in the owner's browser. On the owner's word publishing was turned on and `v17.0.0-beta.3`, the same code, followed: its run passed all 27 jobs and published the four packages to nuget.org. The clean-clone gate passed on `58ffbe2c` (3,170 s), `main` names `17.0.0-beta.3.dev`, and `v18/main` has it merged. What the note said while the step waited:
  - **Blocked on 2026-11-23 by an owner-only item:** every check above has passed but the tag's. A dry run of the release workflow passed every job and packed (`de208db2`), the clean-clone gate passed on the last work commit (`75b6c921`, 2,837 s, Core coverage 97.6%, name scan 0 hits), and 17.0.0-beta.2 is ready on `main` with its install tests passed. Its tag starts a release run of about 5 h 20 min of runner time, and the account's Actions minutes for November are used up; nothing was charged, and nothing runs until 1 December unless the owner adds a budget (`docs/OWNER-TODO.md`, "Decide when `v17.0.0-beta.2` is tagged"). On the owner's word: read the billing page, push the tag, read its run and its draft release, move `main` on to `beta.2.dev`, merge it into `v18/main`, and tick.

### Phase 27: What the first site found

The owner, before the first stable version (2026-11-26): "Fix the findings". The first site outside the demo took the package on 2026-11-19 and found ten things in it (WORKLOG, that day's two entries), which the owner had left until after the release. The Umbraco Marketplace lists a package from its first stable version (WORKLOG, 2026-11-26), so they come before `17.0.0`. Two of them a visitor meets: a send with no answer at all says only that the form is empty, never which fields are required, and the first press of Send on an untouched form posts the page, because the form's script starts loading on that same press. The others are a site developer's: warnings while the package's migrations are pending, a dark scheme given to a theme that has none, a theme stylesheet without a fingerprint, a setting without a configuration key, a table's dates, a migration step that loses its place beside a second process, and three things the guides do not say.

- [x] **27.1 The first site's ten findings are fixed or told, and phase review** (the owner, 2026-11-26) (2026-11-26, dc169a0d)
  - **An empty send names what is required (1):** a send with no answer at all, of a form with shown required fields, is refused with each such field's own error, at the field and in the error summary, as a send with one answer is; the general message for an empty form stays for a form that requires nothing. The same on the page without JavaScript, in the browser and from the public API, held by the shared vectors; ADR-0112's order is amended by an ADR.
  - **The first press of Send (2):** a press of Send before the form's script is there is held, not posted: the script finishes loading and takes that same send, with the button the visitor pressed. Where the script cannot be loaded, the form posts as it does without JavaScript. No send is made twice.
  - **Pending migrations are quiet (3):** while the package's tables are not there yet, a change of a content type writes no warning and no stack trace; what the change would have recorded is there once the migrations have run.
  - **The ground check keeps to what a theme has (4):** a theme without a dark scheme never gets the dark scheme's class from the check, and a form the check fitted follows its ground when the site's own switch changes it, not only the system's.
  - **A theme's stylesheet may carry a query (5):** a site's fingerprint (`?v=…`) is accepted and written as given; what the path refused before it still refuses.
  - **The general settings from configuration (7):** a site's default time zone has a configuration key, used where none was saved, and the package's and the migration plan's names are public constants with XML docs; `docs/configuration.md` follows from the generator.
  - **A table's dates in words (9):** `{{ entry.table }}` writes a date with its month's name in the entry's culture, never a form that reads as another day elsewhere; no test of it depends on a system's culture data (ADR-0263).
  - **Migration steps write first (10):** a step that fills a table does it in one statement that reads nothing first, so beside a second process on the same SQLite file it waits its turn or fails at once; running it twice changes nothing.
  - **Told (6, 8, 10):** `docs/rendering.md` says what `<jfb-assets />` writes on a page without a form and how a site limits it; `docs/installation.md` says what an unattended install does with the package's migrations, as seen on a throwaway site, and that two instances must not share one SQLite file during an upgrade. CHANGELOG, `docs/upgrading.md` and `docs/STATUS.md` follow, and `v18/main` gets it by merge.
  - Done when:
    - a send with no answer at all gets the required fields' errors from the submission pipeline, the page's post handler and the browser's checks, and the empty message where nothing is required, each with a test that fails without the fix; the shared vectors hold both cases for C# and the client;
    - in a browser, with the form's script held back by the test, the first press of Send on an untouched form leaves the page where it is and shows the errors in place, a complete form is sent once (one entry), and with the script refused the form posts as without JavaScript;
    - an integration test changes a content type while the package's migrations are pending and finds no warning in the log, and after the migrations what the change concerns is found;
    - the ground check's unit tests cover a theme without a dark scheme on a dark ground (no class) and a ground that changes with a site's switch; in a browser a form of such a theme on a dark page keeps its light tokens;
    - a theme stylesheet with a query renders with it, and the paths refused before are refused still, by unit tests;
    - with the configuration key set and nothing saved, a site's time is read in that zone; a saved zone wins; a zone that does not exist is refused at start-up as other wrong settings are; the public API snapshot holds the constants;
    - `{{ entry.table }}` of an entry in en-US and in de-DE writes the month by name, by a test that passes on Windows and in the Linux container;
    - the step that fills the default settings is one statement, run twice leaves one row, and an upgrade from the first prerelease still passes (`node scripts/verify-clean.mjs` holds that);
    - `docs/installation.md` says what was seen on a throwaway unattended install, and the docs' link check passes;
    - the sizes stay inside section 4.12's budgets, and every rendering fixture is regenerated and read;
    - the phase-review gate passes as the clean-clone verification of 20.1 (`node scripts/verify-clean.mjs`) on the final work commit, with Core coverage at 85% or more and the name scan at 0 hits; `docs/STATUS.md` names that run;
    - `v18/main` has the step merged in, and its unit and client tests pass there;
    - the handover note is written, both branches are pushed, and `node scripts/roadmap-status.mjs` shows `open=0` and `clean=yes` with `head` equal to `origin_main`.

---

## 6. Definition of done for the first release

- Every feature in §1.1 and §1.3 works on the demo site and is covered by automated tests (unit, integration, vectors, E2E).
- Zero warnings, clean analyzers, ≥ 85% coverage on Core, and the budgets in §4.12 met.
- SQLite locally and SQL Server in CI. Load-balancing-sensitive code is tested with simulated second servers, and the CI load-balancing job exists.
- No secrets and no private terms in the history (both scans clean). The license is the root commit. No community packages are used. Jiron Form Builder is original work (CLAUDE.md rule 2).
- Every backoffice screen and every theme passes WCAG 2.2 AA and the §4.13 rules, checked with screenshots.
- Docs complete and checked against the running product. Screenshots, logo, NuGet metadata and `umbraco-marketplace.json` are ready. The release workflow is ready but disabled.
- `docs/STATUS.md` and `docs/OWNER-TODO.md` tell the owner exactly what is left for them.

## 7. Later (not in this roadmap)

- **Payments:** payment fields with providers' hosted checkouts (never card data in Jiron Form Builder), and an "on payment" trigger.
- **Conversational mode:** one question at a time, keyboard-first.
- **Quizzes and polls:** scoring, correct answers, a results page, public poll results, a time limit, questions and options in random order, later questions that offer only the options picked earlier, and comparing answers between groups.
- **Entry collaboration:** assignees, custom statuses beyond approve and reject, tags (or first a single follow-up flag), a board view.
- **More data in and out:**
  - entry import from CSV/Excel;
  - scheduled exports (a nightly CSV by email or SFTP);
  - PDF generation of entries.
- **Front-end editing** of one's own entry through a signed link, and member registration and profile forms.
- **Field-level encryption at rest** for sensitive fields (needs careful key management, D13).
- **Partial-entry capture** for abandoned forms. It needs a privacy design of its own, so insights cover drop-off anonymously for now.
- **More captcha providers** (enterprise variants) and more backoffice languages (community translations).
- **Importers** from other systems, as separate optional packages, only after the owner decides (OWNER-TODO).
- **Ideas from the feature study** (step 1.1; each one is in `docs/research/feature-catalogue.md`):
  - **For visitors:** shared-password forms; a form that opens in a dialog from a button; a self-delete link in the confirmation (with front-end editing); collapsible groups and a contents list for long forms; a searchable choice for very long lists; country formats for postcodes and references, without keystroke masks; audio or video answers; reading a code with the camera.
  - **For editors:** delayed actions and reminders (N hours after submitting, or N days before a date answer); a calendar file attached to event confirmations; options taken from another form's approved entries; a waiting list for full options; saved groups whose copies stay in sync; comments and review on a draft form; "read-only when" rules and an "end the form here" shortcut; showing approved entries on the website without code (directories, guestbooks); a scannable code for a form's page, for posters.
  - **For entry handlers:** a short trash for deleted entries (it would change "deleting is permanent" in §4.4, so it needs an ADR, and erasure must always bypass it); the other entries of the same person in the entry panel.
  - **Spam and checks:** keyword rules combined with and, or and not; a local score for random-letter text; proof of work only for borderline scores; an opt-in check that an email domain can receive mail; test submissions that check a live form end to end.
  - **Insights:** plain-language hints from the numbers, and a phone or desktop split by screen width.
  - **AI:** an interface through which outside AI assistants build forms (editors only, never entries).

---

## Appendix A: README.md for the first commit

```markdown
# Jiron Form Builder (Jiron.Umbraco.FormBuilder)

Calm, capable forms for Umbraco: a form builder for Umbraco 17 with a drag-and-drop
designer, rules, multi-page forms, actions after every submission, an entries inbox with
exports and insights, and privacy built in.

> **Status:** in development, not released.
>
> **License:** [PolyForm Perimeter License 1.0.1](LICENSE.md). In short: free to use,
> including commercially on your own sites and on sites you build or host for clients,
> and free to change for your own projects. Not allowed: selling it, or publishing your own
> version of it (free or paid, open source or not) or any other product or service that
> competes with it. Contributions are welcome instead. This summary is not legal advice; the
> license text governs.
> Jiron Form Builder is source-available, not "open source" in the OSI sense.

Made by Manassés Zähnler ([jiron.dev](https://jiron.dev)) · [Ligata](https://ligata.ch)

Umbraco is a trademark of Umbraco A/S. Jiron Form Builder is an independent community project and
is not affiliated with or endorsed by Umbraco A/S.
```

## Appendix B: ADR template

```markdown
# NNNN. Title

- Status: accepted | superseded by NNNN
- Date: YYYY-MM-DD
- Roadmap step: X.Y

## Context
What problem, which constraints (link §1/§2 decisions, research docs).

## Options
1. Option A: pros, cons (measured where possible)
2. Option B: …

## Decision
What we do, in one paragraph.

## Consequences
What becomes easier or harder; follow-up steps; what would make us revisit it.
```

## Appendix C: Worklog format

`docs/WORKLOG.md` is append-only and newest last. One line per finished step:

```
- 2026-10-20 · 3.3 Demo site · a1b2c3d · demo on :5500, seeded en/de, Playwright login OK (8 tests)
```

The phase review adds a handover block:

```
### Handover after Phase N (2026-10-21)
- State: what works, what is half-done (should be nothing)
- Next: first step of Phase N+1 and anything it needs to know
- Gotchas: things learned the hard way
- Open owner items added: …
```
