# Jiron Form Builder roadmap: Jiron.Umbraco.FormBuilder

**Calm, capable forms for Umbraco.** Jiron Form Builder is a form builder for Umbraco 17. Editors design forms in a **Forms** section of the backoffice: pages, groups, fields, rules and actions, with drafts, history and a live preview. Visitors fill them in on the website, server-rendered and accessible, or through a headless API. The people who handle the results work with the entries: they review, approve, export and answer privacy requests. It is free to use, including commercially, and maintained in the open.

Written 2026-10-19 from the owner's brief, with research into Umbraco 17 packaging, the Marketplace, licensing, privacy, accessibility and email law, plus an inventory of the owner's earlier work (Ligata.Forms and Tandem). Revised 2026-10-22 with the lessons of Tandem's finished build: its process, its test harness and about sixty gotchas found on this machine (§3.3 and `docs/research/tandem-lessons.md`). The steps were consolidated from 137 to 107 on the same day. The rules for working on it are in [CLAUDE.md](CLAUDE.md), and they are binding.

## How to use this roadmap

- Work through Phase 0 to Phase 20 **in order, one step at a time**. A step can only start once the steps before it are done. The one exception: when a step is blocked, continue with the next step that doesn't depend on it, and come back later.
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
- **The bootstrap exception:** step 0.1 makes no commit (the owner already made the license root commit). Its tick and WORKLOG line are written in 0.2.
- **Done when** lists the checks that prove a step is finished. Run them in the current session and show their output. Passing checks are the only evidence that counts.
- **Every phase ends with a phase review.** It reviews the phase's work, fixes what it finds, and writes a handover note to `docs/WORKLOG.md`, so that a context compaction at any moment loses nothing. Where a phase needs a review of its own (security, privacy, UX, the schema freeze), that is a step of its own. Otherwise the review is the last part of the phase's last step, whose title says "and phase review".
- You may improve this roadmap as you learn. Split steps that turn out too big, fix facts that turn out wrong, or add steps that are clearly needed. Record every such change in `docs/WORKLOG.md` with the reason.
  - **Never weaken or delete** a requirement from §1 or §2, a step, or a "Done when" check.
  - If a check is impossible as written, replace it with an equal or stronger check, explained in an ADR.
  - If a requirement turns out impossible, explain why in an ADR and list it in `docs/OWNER-TODO.md`.
- Facts marked *(verify)* came from research but were not confirmed on this machine. Confirm them in Phase 1 before relying on them.
- **Splitting a step:** give the parts letters (`9.3a`, `9.3b`), so `roadmap-status.mjs` counts them. Some steps are split already.
- **`[~]` is only for things outside your control:** an owner-only item (CLAUDE.md rule 8) or an outage of an outside service. An ordering problem (a check that needs something a later step builds) is fixed by moving or splitting work, with the reason in WORKLOG, never by marking a step blocked.
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
  - its §4.7 endpoints, with integration tests including the `403` matrix (4.7);
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

A NuGet package for Umbraco 17 sites. Install it, open the **Forms** section, and build a form in minutes. Put it on a page with a picker, a block, a tag helper or the headless API. The site gets:

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
    - email the team, or confirm to the submitter (responsive HTML, per language, attachments optional; a plain-text part only if ADR-0007 finds a way through Umbraco's mail sender, §3.4);
    - call a webhook (signed, retried, SSRF-safe), or post to Slack or Microsoft Teams;
    - create or update Umbraco content;
    - change the entry's state;
    - email verification (double opt-in).
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
    - load-balanced hosting with no extra infrastructure;
    - SQLite and SQL Server;
    - file storage through Umbraco's file system providers.
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
| D1 | **Name:** NuGet id and namespace root `Jiron.Umbraco.FormBuilder`; display name **Jiron Form Builder**; backoffice section label **Forms**; repo `github.com/Jiron/Jiron.Umbraco.FormBuilder` (private for now). Short identifiers use `jfb` (tables, CSS, URLs, events) and `JironFormBuilder` (settings section, folders, JS global). The owner reserves the NuGet prefix `Jiron.` (OWNER-TODO). | Owner's choice after name research (2026-10-20). A descriptive product name under the owner's own name leaves nothing to clash with, and the reserved prefix marks the packages as his. "Umbraco" appears only as the platform qualifier inside the package id, never in the display name. The product is always a "Form Builder"; no package id ends in `.Forms`. Editors look for "Forms", so that is the section label. |
| D2 | **First commit = license.** `LICENSE.md` is the PolyForm Perimeter License 1.0.1 with Jiron Form Builder's header and `Required Notice:` line. The owner placed it and made the root commit `781401e` on 2026-10-20, replacing GitHub's README-only commit `06f126f`; step 0.1 checks its SHA-256. No force-push remains approved. | The owner wants everyone to be able to use Jiron Form Builder free of charge, including companies on their own sites and agencies on client sites, and to change it for their own projects, but nobody to sell it, to publish their own version of it (free or paid, open source or not), or to offer any other competing product or service built from it. Contributions go to this repository instead. Perimeter says exactly that in a short, lawyer-drafted text (§3.6). It is **not** OSI "open source", so Jiron Form Builder calls itself "free and source-available", never "open source". A lawyer review is an OWNER-TODO item. |
| D3 | **Target Umbraco 17 LTS first:** CMS `[17.5.0, 18.0.0)`, developed against the latest 17.x (17.7.1 on 2026-10-19), .NET 10. An Umbraco 18 line comes at the end (Phase 19). **17 is the most important version** (the owner, 2026-10-22): the 18 line never costs `main` a feature, a fix, a delay or a compromise, and nothing is designed for 18 at 17's expense. | 17 is the LTS that most sites run, supported until 2028-11-27. 17.5 brought the async distributed background jobs Jiron Form Builder relies on. Confirmed in 1.2b: the whole spike builds against 17.5.0, and only the `tree` workspace-view kind (17.6.0) and `AlignToClock` (17.6.2) are newer; Jiron Form Builder uses neither. |
| D4 | **Original work.** Every name, screen, syntax, data model and line of code is Jiron Form Builder's own (CLAUDE.md rule 2). Vocabulary: form, page, group, field, option list, rule, action, entry, theme, merge tag, template. | Legal safety and a coherent product. The owner's private reference material stays in `.private/` and never enters the repository. |
| D5 | **Form definitions are versioned JSON documents; entries are relational.** A form has one draft and many published versions, each a schema-versioned JSON document upgraded by code. Entries are rows: entry, typed values per field, files, a timeline and notes. Each entry points at the version it was submitted with. | Drafts, history, diff, restore, export and deployment as files fall out naturally. Old entries never change meaning when a form changes. Entries stay filterable and sortable in SQL. Proven in Ligata.Forms (draft and published documents), but entries there were JSON blobs that could not be queried, which is fixed here. |
| D6 | **Submissions are transactional and idempotent.** One transaction stores the entry, its values and one queued **action run** per action that applies (an outbox). A client-generated submission id plus a digest makes retries safe. Actions run in the background with leases, retries and a visible log. | No entry is lost when email or a webhook is down, and no double entries come from double clicks or flaky networks. Proven in Ligata.Forms. |
| D7 | **Persistence:** NPoco DTOs with Umbraco's `PackageMigrationPlan` and `AsyncMigrationBase` steps that are idempotent and use frozen schema snapshots. It works on SQLite and SQL Server. All Jiron Form Builder database access goes through one async gate, and writes start with a write. | This is Umbraco core's own path. `MigrationBase` is obsolete in 17 and gone in 18. The gate is Tandem's proven fix for SQLite's shared-cache locking (§3.3). |
| D8 | **Background work** runs as `IDistributedBackgroundJob`s: one server in the cluster runs each, coordinated by the database. Jobs cover the action and email outbox, retention, stats roll-ups, abandoned-upload cleanup and file-mirror sync. After a commit, the server also wakes its own processor at once, so emails go out within seconds; the job itself is only the safety net. | Load-balanced safe, with no extra infrastructure and no per-node state that matters. Umbraco's runner starts only one due job per 5-second tick, and its own jobs come first, so a package's job can wait a long time for its turn. Speed therefore never depends on the job's period (Tandem ADR-0076). |
| D9 | **Rendering:** server-side Razor first, enhanced by a small framework-free TypeScript runtime: no jQuery, ≤ 16 KB gzip, no third-party requests. Without JavaScript, forms still submit and validate on the server. A **headless** path (JSON definition, submission API, web component renderer) uses the same markup contract and the same runtime. | SEO, speed, accessibility and robustness. Headless sites are a large part of Umbraco 14+ projects. |
| D10 | **One logic, two runtimes.** Rules, validation and merge tags are implemented in C#, which is authoritative, and in TypeScript for instant feedback. Both must pass **one shared set of JSON test vectors** (`tests/vectors/`). | The browser and the server can never disagree about a hidden field or a validation message. This was a lesson from Ligata.Forms, where a condition value was handled differently than intended. |
| D11 | **Public endpoints** live at `/jfb/api/v1` (configurable), never under `/umbraco`. Razor forms without JavaScript post back to their page through Umbraco's surface routing. | Many sites block `/umbraco` publicly, and no-JS posts must re-render the page with errors. |
| D12 | **The backoffice** is a top-level section **Forms** (alias `Jiron.FormBuilder.Section`), built with Lit, TypeScript and UUI, using Umbraco's own patterns: trees, workspaces with views, the save bar, modals and sidebars, the sorter, entity actions and toasts. It talks to the server through a generated OpenAPI client. | It must feel native to Umbraco. No foreign design system. |
| D13 | **Privacy by default:** <ul><li>no cookies, and IP addresses are not stored;</li><li>retention defaults are on;</li><li>sensitive fields are masked;</li><li>consent fields store the exact wording shown;</li><li>logs never contain field values;</li><li>nothing loads from third-party hosts unless an editor chooses a third-party captcha.</li></ul> | GDPR, TDDDG and good practice (§3.7). Site owners get compliance help without configuring anything. |
| D14 | **Spam protection needs no third party by default:** a honeypot, a minimum fill time, a signed challenge, rate limits and content rules feed a spam score. A proof-of-work challenge is optional. reCAPTCHA v2/v3 and hCaptcha are optional providers that load only per §4.10. Suspicious entries go to **Spam**, where nothing is lost and no action runs, instead of being dropped. | Stops bots without privacy costs, and never silently loses a real lead. |
| D15 | **Files:** <ul><li>stored outside the public web root, through a Jiron Form Builder `IFileSystem`, which also works with Umbraco's official storage providers;</li><li>names are random, and downloads go only through authorised endpoints;</li><li>extension allowlist, content sniffing, size and count limits;</li><li>a scanner hook (`IFormBuilderFileScanner`), and deletion with the entry.</li></ul> | Uploaded files are personal data and an attack vector. |
| D16 | **Versioning and branches:** the package major follows the Umbraco major. The first line is `17.0.0-alpha.N`, then `-beta.N`, then `17.0.0`, for CMS 17. `main` is the 17 line and holds every ROADMAP and WORKLOG change; CMS 18 code lives on `v18/main` (Phase 19). Changes flow one way: `main` is merged into `v18/main`, never the reverse. `Directory.Build.props` holds `VersionPrefix` 17.0.0 and `VersionSuffix` alpha.0; release builds pass the tag's version; local test packs use `-dev.<yyyyMMddHHmm>`. | Compatibility is obvious at a glance, and stale NuGet caches can never serve old bits. |
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
| "The form changed while you were filling it in": fetch the new markup, keep the answers that still fit, reset changed consent texts, refuse when an answered field was removed | `JS` (render refresh) | **Port**, simplified for live sites (6.4). |
| CSV export with BOM and the formula-injection guard (`=`, `+`, `-`, `@`, tab, CR) | `FormsManagementController.Export` | **Port** into every export (11.4). |
| Several forms on one page: deterministic, per-request instance ids | `src/Rendering/LigataFormViewComponent.cs` | **Port** (6.2). |
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
- **Good form UX ideas** from the ligata.ch redesign mock (`C:\Code\ligata\redesign`): an error summary with links plus inline errors, a character counter, well-styled native checkboxes and radios. Adopt them in the default theme (6.3).
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
| Serving client assets precompressed (Brotli and gzip) with a `?v=` fingerprint and caching headers | the asset middleware (Tandem 6.8) | **Port** (3.2, 6.2). |
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

### 3.4 Umbraco 17 package facts (research 2026-10-19; confirm in Phase 1)

- **What the sandbox showed:** [docs/research/umbraco-backoffice.md](docs/research/umbraco-backoffice.md) has the confirmed shapes, signatures and traps from 1.2a and 1.2b. Its last section lists what they mean for Jiron Form Builder; every backoffice step reads it before it starts.
- **Templates:** `dotnet new install Umbraco.Templates@17.7.1`.
  - **Always in a private template hive:** pass `--debug:custom-hive %TEMP%\jfb-templates-<version>` to every `dotnet new install` and every `dotnet new` call. The user-wide template install is shared with the Tandem session and must never change (CLAUDE.md rule 6).
  - Pin 17.7.1 for the whole run. Only 18.3 (the 17.5 floor) and Phase 19 use other versions, each in its own hive.
  - `dotnet new umbraco-extension -n <Name> -ex -pm Central` creates an RCL with `Client/` (Vite library mode, Lit, TypeScript, `@umbraco-cms/backoffice`, `@hey-api/openapi-ts`), `StaticWebAssetBasePath=/`, and output to `wwwroot/App_Plugins/<Name>`. It does not run npm during `dotnet build`; run it in scripts and CI. Its `@hey-api/openapi-ts ^0.85.2` fails `npm install` against 17.7.1: pin 0.99.x, which also satisfies 17.5 (1.2a, 1.2b).
  - Test sites: `dotnet new umbraco -n Site --development-database-type SQLite`, then the admin through the unattended-install settings (`Umbraco__CMS__Unattended__InstallUnattended` and the `UnattendedUser*` values) in the start-up environment, so the password never appears on a command line (`scripts/sandbox.mjs`, 1.2a).
- **`umbraco-package.json`:** in `wwwroot/App_Plugins/JironFormBuilder/`, with `id` = NuGet id. Since 17.6, `version` drives cache busting, so stamp it at build time. 17.5 has no cache busting for package files, so 18.3 checks an upgrade on the 17.5 floor. Register the manifests with a `bundle` extension.
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
- **Property editors:** a `propertyEditorSchema`, which is a server `DataEditor`, plus a `propertyEditorUi`. Use a `PropertyValueConverterBase` for Razor, and an `IDeliveryApiPropertyValueConverter` for the Content Delivery API, which must be enabled with `Umbraco:CMS:DeliveryApi:Enabled`.
- **"Used on" tracking:** `IDataValueReferenceFactory` / `IDataValueReference` report references from property values, and Umbraco's walk covers blocks. Umbraco stores references only between `umbracoNode` rows and drops a form's, so Jiron Form Builder keeps its own index, refreshed on content save, and shows it with Umbraco's `entityReferences` info app (1.2b, ADR-0017).
- **No-JavaScript posts** (confirmed in 1.4a, ADR-0004): the form posts back to its own page with Umbraco's encrypted route token `ufprt`, which routes the post to a `SurfaceController` action. `CurrentUmbracoPage()` renders the page again in the same request, and state reaches the view through `HttpContext.Items`. `RedirectToCurrentUmbracoPage(QueryString)` answers `302` to the page with the query but cannot add a fragment, so Jiron Form Builder builds its own redirect. `BeginUmbracoForm(…, antiforgery: false)` writes no token, but posts to the bare current path, always adds a multipart encoding and a random `id`; `Html.SurfaceControllerHiddenInput<T>()` gives the same routing inside our own `<form>`. `[IgnoreAntiforgeryToken]` on the action overrides the controller's `[AutoValidateAntiforgeryToken]`. `CurrentPage` throws when the action is reached through the auto-route `/umbraco/surface/…`, so the action checks the `UmbracoRouteValues` feature first. A tampered `ufprt` never reaches the action: Umbraco renders the page.
- **Content creation** (the "create content" action): the async `IContentEditingService` / `IContentPublishingService` of 14+ rather than the legacy `IContentService` (confirmed in 1.2a).
- **Members:** `IMemberManager` for the current member, `IMemberService` and `IMemberGroupService`.
- **Files:** `IFileSystem`, `PhysicalFileSystem`, and `IHostEnvironment` paths. Umbraco's official Azure Blob storage provider targets the media and image cache file systems; how a package registers its own blob-backed file system is decided in 1.4b *(verify in 1.4b)*.
- **Health checks:** classes with `[HealthCheck]` appear on the Settings › Health Check dashboard, grouped by `Group` (confirmed in 1.2b). A status's action runs at once, without a confirmation, so only safe, repeatable actions go there. Messages are server strings that Umbraco shows as written.
- **Webhooks** (confirmed in 1.2b): an event class derives from `WebhookEventBase<TNotification>` with `[WebhookEvent(name, Constants.WebhookEvents.Types.Other)]` and is registered with `builder.AddWebhookEvent<T>()`; publishing the notification queues the request. A distributed job sends it (every 10 s by default) with the header `Umb-Webhook-Event: <alias>` and our payload as the body.
- **Localization** (confirmed in 1.2b): one `localization` manifest per culture (`en`, `de`); a regional culture such as `de-de` falls back to the language file. The global search tab label and server strings (health checks, webhook event names) are shown as written.
- **Database:**
  - `PackageMigrationPlan` (it auto-runs unattended; `Umbraco:CMS:Unattended:PackageMigrationsUnattended`) with `AsyncMigrationBase` steps;
  - DTO attributes `[TableName]`, `[PrimaryKey]`, `[ExplicitColumns]`, `[Column]`, `[NullSetting]`, `[Length]`, `[SpecialDbType(NVARCHARMAX)]`, `[Index]`, `[ForeignKey]`;
  - store dates in UTC;
  - use `ICoreScopeProvider` and `IScopeAccessor`, and call `Complete()`.
- **Jobs:**
  - `IDistributedBackgroundJob` (`Name`, `Period`, `ExecuteAsync(CancellationToken)` from 17.5), registered with `AddSingleton<IDistributedBackgroundJob, T>()`. Settings live in `Umbraco:CMS:DistributedJobs`. Confirmed on 17.5.0 and 17.7.1 in 1.2b: the parameterless `ExecuteAsync()` is still abstract, so implement both; `AlignToClock` (17.6.2) is not used.
  - The runner starts one due job per 5-second tick, in table order, with Umbraco's own jobs first, so a package job may wait (D8, Tandem ADR-0076).
  - `IRecurringBackgroundJob` runs on every server.
- **Email:** `IEmailSender.SendAsync(EmailMessage, emailType, …)`. Check `CanSendRequiredEmail()` first: without SMTP settings Umbraco drops mail silently. Settings live in `Umbraco:CMS:Global:Smtp`, and `DeliveryMethod=SpecifiedPickupDirectory` suits tests. `EmailMessage` carries one body (HTML or text), so a multipart message with both is not possible through it; 1.6 decides how Jiron Form Builder handles that, and sites that replace `IEmailSender` (for example with a mail service) must keep working.
- **Integration tests:** `Umbraco.Cms.Tests.Integration` 17.x (NUnit ≥3.14 <4), `UmbracoIntegrationTest`, `UmbracoTestServerTestBase`, `[UmbracoTest(Database = NewSchemaPerTest)]`.
- **Release schedule:**
  - 17 LTS: until 2028-11-27;
  - 18 STS: 2026-06-25 to 2027-06-25;
  - 19 STS: planned 2026-12-10;
  - 21: the next LTS, planned 2027-12.
- **What 18 breaks:** `MigrationBase`, `UmbracoApiController`, `ILocalizationService`, `IFileService` and some sync overloads are removed. Swagger moves to `/umbraco/openapi` (OpenAPI 3.1). From Tandem's 18 line (Tandem ADR-0078):
  - documents register through `AddBackOfficeOpenApiDocument` and are served at `/umbraco/openapi/{name}.json`; tests read them through `IOpenApiDocumentProvider`, keyed by name;
  - the new generator writes enums as numbers, integers as "integer or string" and every record constructor parameter as required, documents form files and validation problems differently, and needs a schema transformer to give the TypeScript client the same types as on 17;
  - the snapshots in `docs/api/` and the generated client therefore differ per line.
- **Static files:** Umbraco serves package files through `UseStaticFiles`, with no compression and no caching headers, so the website runtime needs its own serving to meet §4.12 (6.2).
- **Output caching:** sites may put pages behind output caching or a CDN. Jiron Form Builder markup therefore contains nothing per visitor: the challenge is fetched when the visitor first interacts, and there is no antiforgery token on anonymous forms (§4.10). Umbraco's own website output cache (confirmed in 1.4a; not in 17.3.0, present in 17.5.0, 17.7.1 and 18.2.1):
  - opt-in with `Umbraco:CMS:Website:OutputCache:Enabled` (`ContentDuration` defaults to 10 s);
  - **the cache key ignores the query string**, so anything rendered from the query needs an `IWebsiteOutputCacheVaryByProvider` (a collection: a package adds rules without removing the site's);
  - `IWebsiteOutputCacheRequestFilter` is a single service, so a package must never register one;
  - only `Set-Cookie` or `no-store` block storage: the policy checks neither the method nor the status code (posts to a surface action were still never stored in 1.4a);
  - `IWebsiteOutputCacheTagProvider` (a collection) tags pages, and `IWebsiteOutputCacheManager.EvictByTagAsync` evicts them.

  ADR-0004 sets Jiron Form Builder's rules on top of it.

### 3.5 Umbraco Marketplace and NuGet facts (research 2026-10-19)

- **Listing requirements:** the installable package needs the NuGet tag `umbraco-marketplace` (only on the meta package) and must depend on `Umbraco.Cms.*`. Jiron Form Builder's meta package therefore references `Umbraco.Cms.Web.Website` `[17.5.0, 18.0.0)` directly, not only through Web *(verify whether a transitive dependency is enough)*. The AI add-on, which is installed on its own, gets the tag too. Supported versions come from the dependency range (`[17.5.0,18.0.0)` = 17 only). Non-OSI and commercial licences are accepted; the listing is about the package, not its licence type.
- **Sync:** new packages are picked up daily at 04:00 UTC, and known ones every 2 h.
- **`umbraco-marketplace.json`:**
  - It is read from the root that `PackageProjectUrl` points to, so **it cannot be read while the repo is private**.
  - Schema: `https://marketplace.umbraco.com/umbraco-marketplace-schema.json`; validator: `https://marketplace.umbraco.com/validate`.
  - Fields:
    - `Title`, `Description`;
    - `Category` and `AlternateCategory`. **There is no "Forms" category**: use "Editor Tools", plus "Campaign & Marketing" *(verify the current list)*;
    - `AuthorDetails`, `DocumentationUrl`, `IssueTrackerUrl`, `DiscussionForumUrl`, `VideoUrl`, `Screenshots`, `Tags`;
    - `PackageType` (`Package`), `LicenseTypes` (`Free` | `Purchase` | `Subscription`; this is about price, so `["Free"]`);
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
├─ LICENSE.md  README.md  CLAUDE.md  ROADMAP.md  GOAL.md  CHANGELOG.md  SECURITY.md  SUPPORT.md
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
Jiron.Umbraco.FormBuilder (meta)
  └─ FormBuilder.Web ──► FormBuilder.Core ──► Umbraco.Cms.Core / Infrastructure [17.5,18)
         └──────────► Umbraco.Cms.Web.Common, Web.Website, Api.Common, Api.Management, Api.Delivery [17.5,18)
Jiron.Umbraco.FormBuilder.AI (optional) ──► FormBuilder.Core, Umbraco.AI.Core [17.x]
```

- **Core** holds every rule of the product and knows nothing about HTTP. Services take and return domain types. Every public service has an interface.
- **Web** hosts the HTTP surfaces, rendering and static assets. All Umbraco-major-specific glue (OpenAPI registration, auth policies, manifest helpers) lives in clearly named classes, so the 18 port touches as few files as possible.
- **Client packages:**
  - `logic` has no DOM and no dependencies, and is tested against `tests/vectors/`;
  - `runtime` and `element` share `logic` and the markup contract (§4.3);
  - `backoffice` uses `logic` for the designer's live rule preview and the form check.

### 4.3 The form definition model (schema v1)

A form definition is one JSON document, validated against `schemas/jfb-form.v1.schema.json` and upgraded by code when `schemaVersion` changes. Phase 1 (1.7) fixes the exact shape in an ADR. The design:

```jsonc
{
  "schemaVersion": 1,
  "key": "guid", "alias": "contactUs", "name": "Contact us",
  "settings": { /* §4.3.4 */ },
  "pages": [{
    "key": "guid", "title": { "": "Your details", "de-DE": "Ihre Angaben" }, "description": null,
    "rule": null,                                   // show/hide (skip) the whole page
    "groups": [{
      "key": "guid", "title": null, "description": null, "columns": 2, "style": "plain|card",
      "rule": null,
      "fields": [{
        "key": "guid", "alias": "email", "type": "email", "span": 1,
        "label": { "": "Email" }, "hideLabel": false, "help": null, "placeholder": null,
        "required": true, "messages": { "required": null, "invalid": null },
        "defaultValue": null, "prefill": { "query": null, "member": null, "memberMode": "show|hide" },
        "sensitive": false, "includeInEmails": true, "keep": "always|untilActionsRun",
        "autocomplete": "email", "cssClass": null,
        "validation": [ { "kind": "pattern", "value": "...", "message": { "": "..." } } ],
        "rule": null,
        "settings": { /* type-specific, validated by the field type */ }
      }]
    }]
  }],
  "validationRules": [ /* form-level: rule + target field + message, for cross-field checks */ ],
  "actions":   [ { "key": "guid", "type": "email", "name": "Notify sales", "enabled": true,
                   "trigger": "submitted|approved|rejected|verified", "rule": null, "settings": {} } ],
  "outcomes":  [ { "rule": null, "kind": "message|redirect", "message": { "": "<p>Thanks!</p>" },
                   "redirect": { "contentKey": null, "url": null, "delaySeconds": 0 } } ]
}
```

#### 4.3.1 Conventions

- **Keys and aliases:**
  - Every element has a stable GUID `key`. Entries, rules and actions refer to fields by key, never by alias or position.
  - `alias` is unique within the form, matches `^[a-z][a-zA-Z0-9]{0,63}$`, and is not reserved: `form`, `entry`, `page`, `member`, `query`, `context`, `site`, `date` and the honeypot names.
  - An alias is generated from the label (camelCase). It can be edited until the first published version has entries; after that, changing it asks for confirmation and keeps the old alias as a merge-tag alias.
- **Localised text:** every editor-written text is a `LocalizedText`, a map from culture (`""` = default) to text.
  - The visible text resolves as the request culture → its neutral culture → default.
  - A value of exactly `#key` resolves through the Umbraco dictionary.
- **Limits** (host-configurable): 30 pages, 100 groups, 300 fields, 1,000 options per field, 50 actions and 200 rules per form. A definition of at most 2 MB.
- **The public projection** (what the website and the headless API get) never contains:
  - actions, outcomes' internal settings or recipients;
  - option-source configuration;
  - prefill-from-member mappings beyond the field;
  - anything marked internal.

  The **public revision** changes only when the public projection changes (§3.1).

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
| | Radio buttons (`radio`) | layout (vertical, horizontal, cards, cards with images), an "Other" option with free text, option source |
| | Checkboxes (`checkboxes`) | layout as for radio, min/max selections, "Other" option, option source |
| | Grid (`grid`) | rows (statements) rated on shared columns (a scale or an option list), one or several choices per row, all rows or chosen rows required. Each row is its own fieldset, so it works without JavaScript and reads well with a screen reader; on phones the rows stack, one question each. |
| | Ranking (`ranking`) | options to put in order, all of them or the top N. "Move up" and "move down" buttons with live announcements, never drag only; without JavaScript, a position number per option, checked for unique positions on the server. |
| | Checkbox (`checkbox`) | text beside the box (rich text with links); required means it must be ticked |
| | Yes/No (`yesno`) | labels, style (two buttons or a switch) |
| | Consent (`consent`) | consent text (rich text with links to the privacy page), required by default. **The exact text shown is stored** (§4.5 `jfbConsentText`). |
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
  - A rule is `{ "effect": "show|hide|require", "enabled": true, "match": "all|any", "conditions": [ … ] }`. A condition is one of:
    - `{ "field": key, "op": …, "value": … }`, where the value can also be `{ "field": key }` to compare two answers ("departure after arrival");
    - `{ "context": "culture|memberSignedIn|memberGroup|query.<name>|today", "op": …, "value": … }`, for the page's language, the member, an allowed query value and the date in the site's time zone;
    - a nested group.
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
  - **What rules apply to:** fields, groups, pages, actions, outcomes and form-level validation rules.
  - **Hidden means gone:** a hidden field is never required, never validated and never stored, and the server strips it even if posted. A rule that depends on a hidden field sees it as empty.
  - **Saving refuses** cycles and rules that point at missing fields.
- **Validation kinds:**
  - `required`, `minLength`/`maxLength`, `min`/`max`, `pattern`, `format`;
  - `dateWindow`, `matchesField`, `uniquePerForm`, `fileTypes`/`fileSize`/`fileCount`, `selections`;
  - plus form-level validation rules ("show this message on field X when the rule matches").
  - Patterns run with `RegexOptions.NonBacktracking` and a timeout on the server, and saving refuses patterns that the non-backtracking engine cannot run.
- **Merge tags:** `{{ source.path | filter: arg }}`.
  - **Sources:**
    - `field.<alias>`, `field.<alias>.label`;
    - `entry.number|reference|date|url|state`, `form.name|alias`;
    - `page.name|url|<propertyAlias>` (with `page.up.<alias>` to search the ancestors);
    - `member.name|email|<propertyAlias>`;
    - `query.<name>` (only where the form allows query values), `context.<key>` (values the developer passes when rendering or submitting);
    - `site.name|url`, `date.now`, `dictionary.<key>`.
  - **Filters:** `default`, `upper`, `lower`, `truncate`, `date`, `number`, `currency`, `join`, `html`, `url`, `json`.
  - **Escaping:** output is HTML-escaped in HTML contexts, URL-encoded in URL contexts and JSON-escaped in JSON contexts. Unknown tags render empty and raise a form-check warning.
  - **Answer recall:** visitor texts (labels, help texts, text blocks, page titles and outcome messages) may use `field.<alias>` of a field above them or on an earlier page ("Thanks, Anna. Where should we send the quote?"). The runtime updates them as the visitor types, the server renders the same text for no-JS posts and the summary page, and a hidden field resolves empty. A text that names a later field is a form-check error.
  - Developers add sources and filters through `IFormBuilderMergeTagSource` and `IFormBuilderMergeTagFilter`.
- **Calculations:** a tiny expression language (numbers, field references, `+ - * / ( )`, `min`, `max`, `round`, `if`, and `daysBetween`, `yearsBetween` over date fields in the site's time zone), parsed into a tree. It has no evaluation of arbitrary code and no reflection. The server recomputes every calculation, and its result is stored.
- **Shared test vectors:** `tests/vectors/rules.json`, `validation.json`, `merge-tags.json` and `calculations.json` are run by NUnit against Core and by Vitest against `packages/logic`. A case that passes in one runtime and fails in the other fails CI.

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
- **After submitting:** ordered **outcomes**, each with an optional rule: a message (rich text with merge tags), a redirect (content page or URL with merge tags), or a message and then a redirect after N seconds. A message can also show **a copy of the answers** with a print button and a "fill in again" link (6.5).
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
  - `Unverified` (waiting for email verification) → `Submitted`;
  - `Submitted` → `Approved` or `Rejected` (manual approval; without it, entries are approved automatically and the `approved` trigger fires);
  - any state ↔ `Spam` (marking an entry "not spam" moves it to `Submitted` and runs its `submitted` actions then).

  Every change is a timeline event. Deleting is permanent: the entry, its values, files, notes, events, action runs and queued emails go together, and the audit log records that it happened. A test entry (`IsTest`, from preview) is kept out of exports, insights and actions unless the editor asks.
- **Entry numbers:** each form numbers its entries 1, 2, 3, … (`#1042`). The number appears in the backoffice and in emails (`{{ entry.number }}`), and it is unique per form.
- **The pipeline** for a submission (JavaScript, no-JS post or API):
  1. Resolve the form and its published version. If the posted public revision is older but compatible, accept it; otherwise answer `form_changed` (the runtime refreshes the form, §3.1).
  2. Check availability: dates, limits, members, uniqueness, and the places left on chosen options.
  3. Check protection: rate limits, challenge, honeypot, time, captcha, content rules. The result is a spam score with reasons. A failed challenge or captcha is rejected. A high score is **accepted silently into `Spam`**, so bots learn nothing.
  4. Parse and validate per field type, after applying rules. Hidden fields are stripped, and calculations recomputed.
  5. Idempotency: the same `submissionId` with the same digest returns the original receipt; with a different digest it returns `409 duplicate_submission`.
  6. **One transaction:** the entry, values, file links, consent texts, the timeline event, one `ActionRun` per action whose trigger and rule match (none for spam or test entries), the places taken, and the stats increment.
  7. After the commit, wake this server's action processor, then return the outcome.
- **Places on options:**
  - The submission takes a place on each chosen option with a limit through one atomic statement on `jfbOptionCapacity` (§4.5), so two servers can never overbook. When none is left, the field fails validation with its own message, and the visitor can choose another option.
  - Spam and test entries take no places. Rejecting, marking as spam, deleting and erasing an entry give its places back. Moving an entry back takes them again, and when an option is full by then, the backoffice asks before overbooking it.
- **Values kept only until the actions have run:** a field set to "only until the actions have run" is stored with the entry, used by its action runs, and deleted once every one of them is final (succeeded, failed for good or skipped). An entry event records it, and the entry shows the field as "not kept". When no action run applies, the value is never stored.
- **Actions:**
  - **Built-in types:**
    - email (to fixed addresses, a field value, the member, or the creator; CC/BCC; reply-to a field; attachments of uploaded files within size caps; HTML with a layout plus generated text; per-culture subject and body; or a Razor view chosen by a developer);
    - webhook (JSON, form-encoded or XML; field mapping or "everything"; standard metadata; HMAC-SHA256 signature header; secrets as `$JironFormBuilder:Secrets:<Name>` references; SSRF protection);
    - Slack and Microsoft Teams messages (webhook presets with formatted text);
    - create or update content (document type, parent from a picker or a dynamic rule, mapping fields and fixed values to properties, publish or save only);
    - set state (approve, reject or mark as spam, under a rule);
    - send verification (double opt-in).
  - **Running them:**
    - Actions run in the background in the order shown, each in its own `ActionRun`, with retries (5 attempts, exponential back-off from 1 minute) and an `Uncertain` state when a lease expires mid-send.
    - A failed action never blocks the others unless it is marked "stop the rest if this fails".
    - Every run shows on the entry's timeline and in the **deliveries** log, which can retry it.
    - **Retrying an `Uncertain` run asks first,** because its first attempt may already have been delivered: the screen says so, and the management API refuses the retry without an explicit confirmation.
    - **Sending can be paused:** with the host switch `JironFormBuilder:Actions:Paused` on, no action run and no email is sent, and none is lost. Entries are still stored, runs and emails stay queued, and they go out once the switch is off. It is meant for a copy of a live database restored onto a test server, and for incidents. The deliveries log, the dashboard and a health check say that sending is paused.
  - **Umbraco webhooks:** Jiron Form Builder also registers entry events (submitted, approved, rejected, deleted, action failed) with Umbraco's own webhook system, so sites can subscribe in Settings › Webhooks. Confirmed in 1.2b: our event appeared in Umbraco's event picker, and a webhook on it delivered our payload (§3.4, "Webhooks").

### 4.5 Database schema v1 (table prefix `jfb`; all times UTC; GUID keys for anything exposed)

| Table | Columns (main) | Indexes and notes |
| --- | --- | --- |
| `jfbFolder` | Id PK, Key (unique), ParentId FK null, Name, SortOrder, TrashedUtc null, CreatedUtc, UpdatedUtc, CreatedByUserKey, UpdatedByUserKey | (ParentId). Trash keeps the tree; restore returns to the parent or the root. |
| `jfbForm` | Id PK, Key (unique), FolderId FK null, Alias (unique), Name, DraftVersionId FK null, PublishedVersionId FK null, PublicRevision, ConcurrencyVersion, LastEntryNumber, ThemeKey null, TrashedUtc null, CreatedUtc, UpdatedUtc, CreatedByUserKey, UpdatedByUserKey | (FolderId), (TrashedUtc). `ConcurrencyVersion` makes stale saves fail with `409 concurrency_conflict`. |
| `jfbFormVersion` | Id PK, FormId FK (cascade), Number, Kind (Draft, Published), SchemaVersion, Definition (NVARCHARMAX), DefinitionHash, PublicHash, Comment, IsPinned, CreatedUtc, CreatedByUserKey, PublishedUtc null, PublishedByUserKey null | Unique (FormId, Number). Every save adds a row. Published versions are never pruned. Draft saves are pruned by the cleanup job (keep the last 50 and the last 90 days, never pinned ones). |
| `jfbFormField` | Id PK, FormId FK (cascade), FieldKey, Alias, Label (default culture), Type, StorageKind, IsSensitive, SortOrder, FirstVersionNumber, LastVersionNumber null (null = in the live version), ParentFieldKey null (repeater children and grid rows) | Unique (FormId, FieldKey). The **field catalogue**: every field ever published, for entry columns, exports and search. `jfbEntryValue` refers to a field by this `Id`, so a field gets its row before its first value is stored: on publish, or on the first preview submission for a field that exists only in the draft (ADR-0003). |
| `jfbEntry` | Id PK (bigint), Key (unique), FormId FK, FormVersionId FK, Number, State, IsTest, SubmissionId, Digest, Culture, ContentKey null, PageUrl (path only), MemberKey null, IpAddress null, SpamScore, SpamReasons, SearchText (NVARCHARMAX, normalised, no sensitive values), Context (JSON), DurationSeconds null, SubmittedUtc, UpdatedUtc, StateChangedUtc, StateChangedByUserKey null, FirstSeenUtc null, VerifiedUtc null, ResumeTokenHash null, ResumeExpiresUtc null, CurrentPageKey null, ChallengeId null, HoldUntilUtc null, HoldReason null, PurgeAfterUtc null | Unique (FormId, Number) and (FormId, SubmissionId). `SubmissionId` is always set; the server generates one for no-JS, server-key and programmatic entries. **Filtered** unique indexes `WHERE … IS NOT NULL` on (FormId, ChallengeId) and on ResumeTokenHash, created with custom SQL in the migration (SQLite and SQL Server both support them; a test inserts two NULL rows). Also (FormId, SubmittedUtc), (FormId, State, SubmittedUtc), (MemberKey), (PurgeAfterUtc) (ADR-0003). |
| `jfbEntryValue` | Id PK (bigint), EntryId FK (cascade), FieldId FK (`jfbFormField`, no cascade: SQL Server refuses a second cascade path, and entries are deleted before their form), RowIndex, ValueIndex, Text (NVARCHARMAX) null, TextKey (nvarchar 256, normalised in C# with the invariant culture, for sort and filter; SQLite's `LOWER` folds ASCII only) null, Number (a double: `REAL` on SQLite, `FLOAT` on SQL Server, exact for the at most 15 significant digits a number field accepts; `Text` keeps the submitted value; Umbraco maps `decimal` to `TEXT` on SQLite) null, Date null, Bool null, FileId FK null | (EntryId, FieldId), (FieldId, TextKey, EntryId), (FieldId, Number, EntryId), (FieldId, Date, EntryId): covering indexes for the query shapes of ADR-0003. An integer `FieldId` instead of the field's GUID key halves the values on disk (1.3). Multi-value and repeater rows use ValueIndex and RowIndex. |
| `jfbEntryFile` | Id PK, Key (unique), EntryId FK (cascade) null, FormId, FieldKey, OriginalName, StoredName, ContentType, SizeBytes, Sha256, ScanState, CreatedUtc | (EntryId), (CreatedUtc). Files without an entry after 24 h are orphans and get cleaned up. |
| `jfbConsentText` | Hash PK (SHA-256 of culture and text), Culture, Text (NVARCHARMAX), FirstSeenUtc | A consent value stores `true` plus this hash: proof of the exact wording, stored once. |
| `jfbEntryEvent` | Id PK (bigint), EntryId FK (cascade), OccurredUtc, Kind, UserKey null, Details (JSON) | (EntryId, OccurredUtc). Edits record old and new values only for non-sensitive fields, and they are deleted with the entry. |
| `jfbEntryNote` | Id PK, EntryId FK (cascade), UserKey, Body, CreatedUtc, UpdatedUtc | Internal notes, never sent anywhere. |
| `jfbActionRun` | Id PK (bigint), EntryId FK (cascade), FormVersionId, ActionKey, ActionType, Trigger, State (Pending, Running, Succeeded, Failed, Skipped, Uncertain), Attempts, NextAttemptUtc, LockedUntilUtc, LockedBy, LastErrorCode, LastErrorMessage (sanitised, ≤ 1000), CreatedUtc, StartedUtc, FinishedUtc | (State, NextAttemptUtc), (EntryId). |
| `jfbEmail` | Id PK (bigint), Kind (Action, Resume, Verification, Test, Report, Digest, Forward), EntryId FK null (set null), ActionRunId null, To, Cc, Bcc, ReplyTo, From, Subject, HtmlBody, TextBody, Attachments (JSON of file keys), Culture, RecipientKey null, State (Queued, Sending, Sent, Failed), Attempts, NextAttemptUtc, LockedUntilUtc, LastError, CreatedUtc, SentUtc, PurgeAfterUtc | (State, NextAttemptUtc), (PurgeAfterUtc), (RecipientKey, CreatedUtc). Sent mail is purged after N days (default 30). RecipientKey is the visitor-typed address, trimmed and case-folded in C# (SQLite's `LOWER` folds ASCII only), so the per-address limits count every spelling of one mailbox. |
| `jfbOptionList` | Id PK, Key (unique), Alias (unique), Name, Kind (Manual, Content, DataType, Url, File, Countries, Code), Config (JSON), Items (JSON), CacheSeconds, ConcurrencyVersion, CreatedUtc, UpdatedUtc | |
| `jfbTheme` | Id PK, Key (unique), Alias (unique), Name, BaseTheme, Tokens (JSON), CustomCss, IsDefault, ConcurrencyVersion, CreatedUtc, UpdatedUtc | |
| `jfbEmailLayout` | Id PK, Key (unique), Alias (unique), Name, Layout (JSON: logo, colours, header and footer per culture), IsDefault, CreatedUtc, UpdatedUtc | |
| `jfbFormTemplate` | Id PK, Key (unique), Kind (Form, Group), Name, Description, Category, Definition (NVARCHARMAX), CreatedUtc, CreatedByUserKey | Templates saved by users: whole forms, and saved groups of fields that the designer inserts as copies with new keys. The built-in ones ship as embedded JSON. |
| `jfbOptionCapacity` | FormId FK (cascade), FieldKey, OptionValue, Taken, UpdatedUtc | PK (FormId, FieldKey, OptionValue). The places taken on options with a limit; the limit itself lives in the definition. Taken with `UPDATE … SET Taken = Taken + 1 WHERE … AND Taken < @limit` after an `INSERT … WHERE NOT EXISTS`. |
| `jfbUserSetting` | UserKey, Name, Json (NVARCHARMAX), UpdatedUtc | PK (UserKey, Name). A backoffice user's own choices, the same on every device: column sets, saved filters, export choices, the digest. |
| `jfbPermission` | Id PK, UserGroupKey, TargetType (Global, Folder, Form), TargetKey (`Guid.Empty` for Global), Permissions (flags) | Unique (UserGroupKey, TargetType, TargetKey). |
| `jfbSetting` | Id PK, Scope (`global` or a site root key), Section, SchemaVersion, Version, Json (NVARCHARMAX), UpdatedUtc, UpdatedByUserKey | Unique (Scope, Section). |
| `jfbReference` | FormId, ContentKey, PropertyAlias, Culture, UpdatedUtc | PK (FormId, ContentKey, PropertyAlias, Culture). Needed: Umbraco's relations link only `umbracoNode` rows (1.2b, ADR-0017). |
| `jfbAuditEntry` | Id PK (bigint), OccurredUtc, UserKey, Action, EntityType, EntityKey, Details (JSON) | Never field values. (OccurredUtc), (EntityKey). |
| `jfbDailyStat` | Day, FormId, Metric, Dimension, Value | PK (Day, FormId, Metric, Dimension). Metrics: views, starts, pageReached:{pageKey}, submissions, spam, completionSeconds, origin:{contentKey}, hour:{0-23}, invalid:{fieldKey}:{errorKind}. Unique members are counted from entries at query time, so stats never hold personal data. New metrics need no migration. |
| `jfbKeyValue` | Key PK, Value, UpdatedUtc | One-time flags (section granted, setup done, file-mirror hashes). |

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
| `GET forms/{keyOrAlias}?culture=&contentKey=` | The public projection for headless rendering, when the form allows it. `ETag` from the public revision and culture. |
| `GET forms/{key}/render?instance=&page=&contentKey=&culture=` | The published form as HTML in the page's theme and culture, for a runtime that got `form_changed` on a page that may come from a cache (ADR-0004). `page` must be a local path. `no-store`. |
| `POST forms/{key}/challenge` | A signed challenge (form, time, and optionally a proof-of-work puzzle). Fetched on first interaction, so cached pages stay cacheable. The response also carries what cached markup cannot: the places left per option, and the member values that rules use (§4.3.3). |
| `POST forms/{key}/uploads` | Upload one file before submitting (progress per file). Returns a signed upload token. Limits per form, file and IP. |
| `POST forms/{key}/validate` | Validate one page's values on the server (multi-page, `422` with errors by alias). |
| `POST forms/{key}/entries` | Submit: `{submissionId, revision, culture, contentKey, values, uploads, context, challenge, captchaToken}` → `{receipt, outcome:{kind, html \| url, delaySeconds}}`. |
| `POST forms/{key}/receipt` | Check an uncertain submission by `{submissionId, receipt}`. |
| `POST forms/{key}/drafts` | Save and resume: `{values, pageKey, email}` → the resume link is emailed; the response never contains it. |
| `POST drafts/resume` | `{token}` → values and page. The token comes from the URL fragment (`#jfb-resume=…`), so it never reaches server logs. |
| `GET verify?t=` and `POST verify` | Email verification. The GET only shows a confirm button, because mail scanners follow links; the POST confirms. |
| `GET forms/{key}/options/{fieldKey}?parent=&culture=` | Cascading option lists. |
| `POST forms/{key}/events` | Insights beacon (`view`, `start`, `page`, and `invalid` with only the field key and the kind of error) with no identifiers, counted per day only. |

**Rules:**

- Tokens always travel in the body or the URL fragment, never in query strings that get logged (except the verify link, which is single-use and short-lived).
- Same-origin requests are always accepted; others only from `JironFormBuilder:PublicApi:AllowedOrigins`.
- Client IPs come only from `HttpContext.Connection.RemoteIpAddress`. Sites behind proxies configure ASP.NET Core `ForwardedHeaders` (documented).
- **Server-to-server headless submissions** may send `Authorization: Jfb-Key <key>`, with keys in host configuration compared by hash. They skip the browser challenge and captcha, never validation.
- **Stable error codes:** `form_not_found`, `form_closed`, `form_changed`, `validation_failed` (`422`, errors by alias), `challenge_required`, `captcha_failed`, `rate_limited`, `payload_too_large`, `unsupported_media_type`, `member_required`, `duplicate_submission`, `entry_limit_reached`, `already_submitted`.

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
- **Setup and health:** checklist status, health results, "create a form block type for me".

### 4.8 Settings model

**Host configuration** (`appsettings.json` → `JironFormBuilder`). It is for developers and for locks, and it wins over the backoffice:

```json
"JironFormBuilder": {
  "PublicApi": { "BasePath": "/jfb/api", "AllowedOrigins": [], "ServerKeys": [] },
  "Rendering": { "DefaultTheme": "default", "IncludeAssetsAutomatically": true },
  "Files": { "Root": "~/umbraco/Data/JironFormBuilder/Uploads", "MaxRequestBytes": 52428800, "BlockedExtensions": ["exe", "dll", "js", "bat", "cmd", "ps1", "sh", "msi", "html", "svg"] },
  "Privacy": { "AllowFullIp": false, "AllowRetentionOff": false, "ThirdPartyCaptchaConsent": "explicit" },
  "Captcha": { "RecaptchaV2": { "SiteKey": null, "SecretKey": null }, "RecaptchaV3": { "...": "..." }, "HCaptcha": { "...": "..." } },
  "Webhooks": { "AllowedHosts": [], "AllowHttp": false },
  "Actions": { "Paused": false },
  "Secrets": { "CrmToken": null },
  "Definitions": { "Mirror": false, "Folder": "~/umbraco/JironFormBuilder/forms" },
  "Limits": { "...": "hard caps the backoffice cannot exceed" },
  "Designer": { "SettingLocks": [ { "type": "email", "setting": "from", "value": "noreply@example.com", "hidden": true } ] },
  "LockedSections": []
}
```

**Backoffice settings** (typed sections in `jfbSetting`; `global` scope, plus per-site overrides where marked):

- **General:**
  - default theme, the required-marker style, default labels;
  - new-form defaults (consent field, notify-the-creator action);
  - per site: the base URL and the **time zone** (an IANA id, used for "today", opening dates and relative dates).
- **Email** (per site):
  - sender name and address;
  - default layout;
  - team recipients for system notices (failed deliveries);
  - team addresses that entries may be forwarded to (besides backoffice users);
  - test email.
- **Spam protection:**
  - default level;
  - content rules (blocked words, blocked email domains, maximum links);
  - blocked address ranges (CIDR; matched against the request, never stored);
  - spam retention;
  - provider keys status (keys themselves only in host configuration).
- **Files:** allowed type groups, maximum sizes, scanner status.
- **Privacy:**
  - default retention per state;
  - IP handling;
  - sensitive-field defaults;
  - third-party captcha consent mode (explicit, Cookiebot category, CMP through the JS API);
  - the insights switch.
- **Access:** which user groups get which Jiron Form Builder permissions, globally and per folder or form (when the host doesn't lock them).
- **Integrations:** webhook signing secret reference, Slack and Teams defaults, Umbraco webhook events on or off.

Every setting has:

- a default, validation (server and client), help text, a docs entry and a test;
- a "locked by configuration" display when the host locks it.

A settings change raises a notification and a cache refresher, so every server sees it at once.

### 4.9 Extension points (documented, with samples)

- **C# types** (each registered through `builder.JironFormBuilder()…`):
  - `IFormBuilderFieldType` (or the `FormBuilderFieldTypeBase<TSettings>` helper): a custom field with a settings schema, parsing, validation, storage kind, Razor partial and client behaviour name;
  - `IFormBuilderActionType` (`FormBuilderActionTypeBase<TSettings>`): a custom action with settings, a test run and retry semantics;
  - `IFormBuilderOptionSource`: options from code, with optional parent filtering for cascades;
  - `IFormBuilderExportFormat`: a custom export;
  - `IFormBuilderCaptchaProvider`: a captcha;
  - `IFormBuilderSpamCheck`: an extra spam signal with a score and a reason;
  - `IFormBuilderMergeTagSource` and `IFormBuilderMergeTagFilter`;
  - `IFormBuilderFileScanner`: malware scanning, for example ClamAV, in a sample;
  - `IFormBuilderFileStore`: other storage, for example blob storage.
- **Settings schemas:** C# attributes on a settings class (`[FormBuilderSetting(Label, Description, Editor, Order, Group, SupportsMergeTags)]`) give every custom type a backoffice editor without any JavaScript. A custom Lit element can replace the editor for a setting through a `jfbSettingEditor` manifest.
- **Notifications:**
  - forms: `FormSaving`/`Saved`, `FormPublishing`/`Published`, `FormTrashing`/`Deleted`;
  - entries: `EntrySubmitting` (cancelable, can add field errors), `EntrySubmitted`, `EntryStateChanged`, `EntryDeleting`;
  - actions: `ActionRunning`/`ActionRunCompleted`/`ActionRunFailed`;
  - `ExportCompleted`.
- **Services:**
  - `IFormBuilderFormService`, `IFormBuilderEntryService` (query, create programmatically, change state);
  - `IFormBuilderEntryReader` (read approved entries on the website, for example a guestbook);
  - `IFormBuilderRenderer`;
  - `IFormBuilderMergeTagService`.
- **Rendering:**
  - the `<jfb-render form="alias|key" theme="…" context-…="…">` tag helper, `@await Component.InvokeAsync("FormBuilderForm", …)` and `<jfb-assets>`. The custom element `<jfb-form>` belongs to the headless renderer;
  - the CSP nonce: `<jfb-assets nonce="…">`, or an `IFormBuilderCspNonceProvider` that a site (or its CSP package) registers to return the request's nonce, which `<jfb-assets>` also uses when it is included automatically (ADR-0004);
  - theme views in Jiron Form Builder's own layout, fixed in ADR-0015 without mirroring any other product's conventions. The proposal: `Views/JironFormBuilder/<theme>/` holds `Form.cshtml`, `Page.cshtml`, `Group.cshtml`, `Summary.cshtml`, `Outcome.cshtml` and `fields/<type>.cshtml`;
  - per-form overrides in `Views/JironFormBuilder/<theme>/_forms/<alias>/`, with the same file names.
- **Website JS API:**
  - `window.JironFormBuilder.init(root)` for forms injected later;
  - `get(formEl)` → `{ validate(), next(), back(), submit(), values() }`;
  - `consent.grant('captcha')`/`withdraw()`.
- **DOM events:** `jfb:ready`, `jfb:page`, `jfb:invalid`, `jfb:submitting`, `jfb:submitted`, `jfb:error`. They are for the site's own analytics; Jiron Form Builder tracks nothing itself.
- **Headless:** the TypeScript types and JSON schema of the public projection, and `<jfb-form>` with `api`, `form` and `culture` attributes.

### 4.10 Security and privacy by design

- **Escaping and rendering:**
  - Escape all visitor input everywhere: Razor, emails, the backoffice and exports.
  - Rich text from editors is sanitised server-side, before rendering and before emails, by an established allowlist sanitiser (HtmlSanitizer, MIT; licence and dependencies checked in 3.5), never a home-made one.
  - Theme custom CSS goes through the sanitiser's CSS parser, which removes `@import`, `url()`, `image-set()` and `@font-face` sources that point to other hosts, `expression()` and `behavior`.
  - No `innerHTML` with untrusted data, and no `unsafeHTML` in Lit, except for sanitised editor HTML through one audited helper.
  - Exports guard against formula injection.
- **Anonymous endpoints:**
  - signed challenges (Data Protection, form-bound, time-bound), honeypot, minimum fill time;
  - the ASP.NET Core rate limiter per IP, per form and site-wide, with bounded partitions that fail closed;
  - request size and time limits, and cancellation everywhere;
  - `Cache-Control: no-store`;
  - origin checks.
  - **Antiforgery:** anonymous forms carry no antiforgery token, so they work on cached pages; their endpoints are not authenticated, and SameSite plus origin checks cover them. **Members-only forms** use antiforgery, because they act for a signed-in member.
- **Third-party captchas** (reCAPTCHA, hCaptcha):
  - Their scripts load only on forms that use them, and only after consent.
  - **Consent modes:**
    - `explicit` (default): a short notice and a "Load the check" button, or the consent field, before anything loads;
    - `cookiebot` (a chosen category);
    - `cmp`: `JironFormBuilder.consent.grant('captcha')` from any consent manager;
    - `implicit`: only if the host allows it.
  - reCAPTCHA can use `recaptcha.net`. Verification checks hostname, action, score and token age (ported from Ligata.Forms).
- **Files:**
  - an extension allowlist per field plus a global blocklist, checked against the bytes' magic numbers;
  - random stored names, never served from a public path, sizes enforced while streaming;
  - downloads only through the management API, with permission checks, `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`;
  - images are never rendered inline from untrusted SVG;
  - the scanner hook can quarantine a file, which marks the entry.
- **Webhooks and URL option lists:**
  - `https` only (`http` only when the host allows it);
  - hosts only from `JironFormBuilder:Webhooks:AllowedHosts` (host configuration, so editors cannot add hosts);
  - private, loopback and link-local addresses blocked after DNS resolution (SSRF), with no redirects to other hosts;
  - timeouts and response caps;
  - secrets only as `$JironFormBuilder:Secrets:<Name>` references, never stored in the database or shown in the UI.
  - **Testing without weakening production:** an `IFormBuilderHttpTransport` lets only the `Testing` assembly reach the stub on `127.0.0.1:5560`. A test proves that production refuses loopback.
- **Backoffice:**
  - **no HTML from data in Umbraco's modals:** Umbraco's confirm modal renders its strings as HTML, and form names, entry values and file names are untrusted. Every confirm goes through one helper with a Lit template, and a source test fails on any direct use of the modal (Tandem 10.1);
  - **polls never keep a session alive:** badge counts and other polls pause while the user is idle and never renew an expired session (Tandem ADR-0051);
  - **the preview frame cannot reach the backoffice:** it is sandboxed without `allow-same-origin`, so a form's theme or scripts never get the backoffice session. Its requests therefore come from the opaque origin `null`, which the public API accepts only with a short-lived signed preview ticket bound to the form (a finding of Tandem's security review);
  - every management endpoint is authorised by Jiron Form Builder permissions, with tests that call each endpoint as a user without the permission and expect `403`;
  - sensitive values are masked unless the user has "view sensitive data", and exports leave them out unless chosen and permitted;
  - the audit log records views of entries with sensitive data, exports, deletions, state changes, settings and permission changes.
- **Logging:** logs never contain field values, emails, tokens or file names. Tests capture the logs and check.
  - MVC logs action arguments at Trace through their `ToString`, and records print every property there, so request models that carry values or tokens seal a `ToString` that prints only their type.
  - Job runs log exception types and codes, never messages, because database and mail server messages can quote personal data.
- **Email:**
  - headers are stripped of CR/LF, and the sender always comes from configuration, never from a visitor;
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
  - insights are counters only.
- **CSP-friendly website runtime:** no inline scripts or event handlers, no style attributes, a nonce option for the script tag, and no `eval`. On cached pages `script-src 'self'` is enough. A nonce works only on uncached pages: a cached page keeps an old nonce and the browser blocks the runtime (1.4a). Lazily imported chunks inherit the module script's nonce, with or without `'strict-dynamic'` (ADR-0004).

### 4.11 Ports, folders and local files

| Port | Use |
| --- | --- |
| 5500 | Demo site (`scripts/demo.ps1 start`) |
| 5501 | Second demo instance on the same database (load-balancing smoke test, SQL Server in CI only) |
| 5510 | Research sandbox (Phase 1) |
| 5520 | E2E test host (fresh database per run) |
| 5521 | Static page server for headless and cross-origin tests |
| 5522 to 5529 | Extra E2E hosts for suites that need their own (privacy on 5522, load on 5523, resilience with restarts on 5524), §3.3 |
| 5530 | Install-test site (packages from the local feed) |
| 5540 | Umbraco 18 sandbox (Phase 19) |
| 5541 | Umbraco 18 demo (Phase 19) |
| 5550 | Vite dev server for the client packages (`--port 5550 --strictPort`) |
| 5551 | Playwright HTML report server |
| 5560 | Local HTTP stub for webhooks, URL option lists and captcha verification (test hosts only; see §4.10) |

Servers that Jiron Form Builder starts use only these ports. Ports the OS assigns automatically are fine. Never touch 1210, 1211 or 5400 to 5499, and never connect to `MSSQL$UMBRACO` (CLAUDE.md rule 6).

`.local/` is git-ignored. It holds:

- `credentials.json`: generated users for every test role (admin, designer, entry handler, viewer, privacy officer, no access) and two members, per site;
- `sandbox/`, `install-test/`, `e2e/`;
- databases;
- `mail-pickup/`;
- Playwright traces and raw screenshots;
- `nuget-feed/`.

### 4.12 Quality budgets

- **Website runtime:** ≤ 16 KB gzip for the core, with features loaded on demand (uploads, signature, repeater, calculations, captchas): ≤ 40 KB gzip with everything. The default theme CSS is ≤ 10 KB gzip. Nothing loads from third parties (§4.10). The files are served compressed and cacheable (6.2), because Umbraco's static files are neither, and a page with a form scores the same in Lighthouse as the page without it (16.2).
- **Server speed (demo machine, SQLite):** each budget is measured at a stated concurrency. SQLite has a single writer that Umbraco itself also uses, so parallel writes queue there (Tandem ADR-0063). The load test (16.2) therefore also reports where SQLite stops keeping up, and the docs steer busy sites to SQL Server.
  - rendering a 50-field form takes ≤ 20 ms from the cached definition (1.4a: a median of 0.12 to 0.15 ms, ADR-0004);
  - a submission (store plus outbox) takes ≤ 150 ms at p95, with up to 10 submissions arriving at the same moment on SQLite (ADR-0003);
  - the first page of entries for a form with 100,000 entries loads in ≤ 300 ms, and a 10,000-entry CSV export streams in ≤ 5 s;
  - the first email leaves ≤ 10 s after a submission.
- **Backoffice:** a 300-field form keeps designer interactions under 100 ms, and the form tree with 1,000 forms opens in under 1 s.
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
- **Checked, not assumed.** Each UI phase ends with a review of every screen as every role, in light and dark, in English and German, with screenshots:
  - **Widths:** website screens at 375, 768 and 1440 px; backoffice screens at 1024, 1440 and 1920 px. Umbraco's own backoffice needs about 920 px (Tandem 7.9), so at narrower widths the review only checks that Jiron Form Builder's screens break no worse than Umbraco's.
  - **Making the pictures:** a UX-walk script (ported from Tandem's `ux` project) takes all of them in one run, so a review never skips a variant for time.
  - **Critique:** three design skills run in every review (the owner, 2026-10-22; ADR-0019, ADR-0020): `design-taste-frontend` for the design read and its pre-flight check, `impeccable` for critique, audit and polish, and `emil-design-eng` for motion and interaction feedback.

### 4.14 Upgrade and compatibility policy

- **Semantic Versioning** within each Umbraco major line. A patch never needs a migration, and a minor may add migrations but never breaks public C#, HTTP or JS APIs.
- **The public surface** is listed in `docs/extending.md`: public C# types, `/jfb/api/v1`, the definition schema, the JS API and DOM events, theme view names and CSS custom properties.
  - Breaking changes there need a new major or a new API version.
  - Deprecations carry `[Obsolete]` with the replacement and last for the whole major.
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
- [ ] **1.4a Spike: rendering and posting**
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
- [ ] **1.4b Spike: uploads and file storage**
  - **Uploads:** a streamed multipart upload with size limits enforced while reading, stored through an `IFileSystem` in a folder that is not web-accessible, with a magic-number check.
  - **Downloads:** a protected download through the management API.
  - **Blob storage:** read the docs and source of Umbraco's official Azure Blob storage provider and decide how an `IFormBuilderFileStore` can use it (a sample, not a dependency). Resolve the open files note of §3.4.
  - Write ADR-0006 "File storage and uploads".
  - Done when:
    - an integration test shows that a 60 MB upload to a 50 MB limit is cut off without buffering, that a renamed `.exe` is refused, and that the stored file cannot be fetched by URL;
    - the same test shows the download working for an authorised backoffice user and refused (`401` or `403`) for an anonymous caller and a user without access.
- [ ] **1.5 Spike: one logic, two runtimes**
  - Write a minimal rules evaluator in C# and TypeScript, with 40 vectors in one JSON file run by NUnit and by Vitest.
  - **Decide:**
    - the vector format;
    - wire formats (invariant culture, ISO dates, decimal strings);
    - time zones and "today" (the per-site `TimeZone` setting of §4.8, with injected time, and vectors across a daylight-saving change);
    - number semantics (C# `decimal` against JS numbers) and the rounding rules for calculations. Stored numbers keep at most 15 significant digits (ADR-0003).
  - Write ADR-0005 "Shared logic and test vectors".
  - Done when: `dotnet test` and `npx vitest run` in the sandbox run the same vector file, and a deliberately broken TypeScript operator makes the TypeScript suite fail (shown, then reverted).
- [ ] **1.6 Spike: background work and email**
  - Write a distributed job with leases, and an in-process "kick" after commit through a bounded channel.
  - Simulate two servers on one database: SQLite cannot be shared reliably, so simulate the second processor in-process, and note the real two-host check, which 8.1 adds to CI.
  - Send through `IEmailSender` to a pickup folder, gated by `CanSendRequiredEmail()`.
  - **One body:** `EmailMessage` carries HTML *or* text (§3.4). The ADR decides whether Jiron Form Builder mail is HTML only, as Tandem's is (Tandem ADR-0042), or whether a text part can be added. Either way it never bypasses `IEmailSender`, because sites replace it with their own mail services.
  - **The job tick:** show that the in-process kick, not the job's period, carries the latency, with Umbraco's own jobs registered (one job per 5-second tick, §3.4).
  - Measure the time from submission to email.
  - Write ADR-0007 "Background work, outbox and email".
  - Done when: the latency numbers are in the ADR, and no action ran twice in 1,000 simulated runs with two processors.
- [ ] **1.7 Architecture and the remaining ADRs**
  - Write `docs/architecture.md`, the contract for later phases:
    - modules, the dependency diagram, the definition model and its JSON schema outline, the data model with an ER diagram;
    - the public and management APIs, settings, extension points;
    - a threat model (STRIDE table), quality budgets and the glossary.
  - ADRs:
    - 0001 solution structure and packages;
    - 0002 the form definition model, versions and the public projection;
    - 0008 the settings model (typed sections, schema versions, scopes, locks);
    - 0009 privacy defaults;
    - 0010 versioning, branching and the upgrade policy (§4.14);
    - 0011 testing strategy;
    - 0012 anonymous endpoints, spam protection and challenges;
    - 0013 permissions;
    - 0014 the merge-tag language;
    - 0015 themes and the markup contract.
  - Done when: all of them are committed, and §4 of this roadmap is updated where the ADRs changed it.
- [ ] **1.8 Phase review**
  - A subagent reviews the ADRs for contradictions with §1, §2 and §4. Fix what it finds.
  - Check that no *(verify)* is left in §3 and §4.
  - Write the handover note.
  - Done when: pushed, with the phase-review gate.

### Phase 2: Experience design (no product code)

The owner's top UX requirement is calm, uncluttered screens (D18, §4.13). This phase designs every screen before any of it is built, so later phases implement a design instead of improvising one.

- [ ] **2.1 Information architecture and words**
  - Write `docs/ux/information-architecture.md`:
    - the Forms section's menus, trees, workspaces and views;
    - every screen with its one primary action and its secondary actions (visible or in "…");
    - a role matrix (designer, entry handler, viewer, administrator, privacy officer, no access): who sees what.
  - Write `docs/ux/copy.md`: the glossary (CLAUDE.md vocabulary), tone (plain, friendly, direct; sentence case; verbs on buttons), message patterns for errors, empty states and confirmations, with en and de examples.
  - List the top 15 tasks per role with their click paths.
  - Done when: committed, and a subagent playing a first-time editor finds every top task in ≤ 3 clicks from the section (each path written down).
- [ ] **2.2 Backoffice wireframes**
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
- [ ] **2.3 Website form design**
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
- [ ] **2.4 Design critique and phase review**
  - Run a design critique on 2.2 and 2.3: rerun all three design skills (ADR-0019, ADR-0020): `design-taste-frontend`, an `impeccable` critique and audit, and an `emil-design-eng` review. A subagent with the §4.13 checklist may add a fourth view. Fix every finding that is not purely taste, and re-render.
  - **Phase review:** write the handover note.
  - Done when:
    - `docs/ux/critique-2.md` lists the findings (a section per skill), the decisions and before/after images;
    - pushed, with the phase-review gate.

### Phase 3: Skeleton, tooling, demo site, CI

- [ ] **3.1 Solution and projects**
  - Create the `.slnx` and the projects of §4.1 (the AI add-on and samples come in Phase 15). Base `FormBuilder.Web` on `dotnet new umbraco-extension -ex -pm Central`, then restructure. Keep the packages solution filter in `src/` (§3.3).
  - Reference packages through central management, with Umbraco as `[17.5.0, 18.0.0)` (D3).
  - Add an empty composer per project. The meta package has no code.
  - Done when: `dotnet build -c Release` has zero warnings, and `dotnet pack` produces three nupkgs (Core, Web, meta) into `.local/nuget-feed`.
- [ ] **3.2 Client workspace**
  - Create `src/Jiron.Umbraco.FormBuilder.Web/Client` as an npm workspace with `packages/logic`, `runtime`, `element` and `backoffice`: TypeScript strict, Vite library builds, ESLint, Prettier and Vitest.
  - **The backoffice package:** Lit, `@umbraco-cms/backoffice` pinned to the minimum supported 17.x, and `@hey-api/openapi-ts` client generation.
  - **The runtime and element packages:** no runtime dependencies, output to `wwwroot/jfb/assets/`, and code split per feature. The build also writes Brotli and gzip copies of every file, which 6.2 serves (§3.4, "Static files").
  - **Checks:**
    - `scripts/size-check.mjs` measures gzip sizes against §4.12. It also reports the backoffice bundle's size, which Tandem never measured and a form designer will make much larger; 9.6 sets its budget;
    - an MSBuild target fails `dotnet pack` when the assets are missing, stale (an input hash), for another version, or contain source maps. Each pack builds the client for its own version (Tandem's `TANDEM001`);
    - `umbraco-package.json` gets its `version` stamped at build;
    - **the licence notice:** every emitted JS and CSS file keeps a `/*! Jiron Form Builder … Required Notice: … */` banner (the bundler's legal-comments setting), as PolyForm requires for every copy. The size check counts it, and the MSBuild target checks it is there.
  - **Ignored output:** the built assets are git-ignored (`.gitignore` already lists both output folders).
  - Done when: `npm ci && npm run lint && npm test && npm run build` passes, the size check prints the budgets, and the pack check fails on an asset without the notice (tested).
- [ ] **3.3 Demo site**
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
- [ ] **3.4 Test projects and harness**
  - **Unit:** NUnit 3 and NSubstitute.
  - Build the harness in the shape of the lessons file, part 2 (§3.3).
  - **Integration:** `Umbraco.Cms.Tests.Integration` 17.x, set up per Tandem's lessons (§3.3), with a sample test that boots Umbraco with Jiron Form Builder's composers.
  - **Testing project:**
    - a local HTTP stub on 5560 that records webhook calls and plays captcha verification answers;
    - fake time;
    - users and members factories;
    - a mail pickup reader;
    - the test-only endpoints (`/jfb-testing/…`), guarded to test hosts.
  - **Vectors:** a runner that turns `tests/vectors/*.json` into NUnit cases and Vitest cases.
  - **E2E:** Playwright Test, with Edge locally and Chromium in CI.
    - **Global setup:**
      - starts a fresh host on 5520 with its own database and mail folder, and waits for Umbraco's start-up work. The wait looks for a log marker that appears whether the Content Delivery API is on or off: Tandem's waited for "The Delivery API is not enabled", which 6.6 would make disappear;
      - creates the users admin, designer, entry handler, viewer, privacy officer and no-access, plus two members. Step 4.6 adds their Jiron Form Builder permissions;
      - writes the hang marker in its teardown.
    - **Fixtures:** `loginAs`, `apiAs`, `waitForMail`, `fillForm`, `submitForm`, and the sign-in lock.
    - **Projects:** the ones of the lessons file, each added to the config together with its first spec (`smoke` now).
  - Done when:
    - `dotnet test` and `npx playwright test --project=smoke` pass;
    - a test proves that a host outside Development has no `/jfb-testing/` endpoint;
    - a test of `support/host-health.ts` shows that a hung database writes the marker, and that the start-up wait ends with the Delivery API on and with it off.
- [ ] **3.5 CI, the local CI mirror and phase review**
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
    - `scripts/check-npm-licences.mjs`;
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

- [ ] **4.1 Schema v1 and migrations**
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
- [ ] **4.2 The definition model**
  - **The model:** C# records for §4.3, System.Text.Json with source generation, and `LocalizedText`.
  - **The schema:** `schemas/jfb-form.v1.schema.json`, with a test that validates serialised definitions against it (a JSON-schema library is allowed as a test-only dependency if MIT or Apache-2.0).
  - **Upgraders:** the definition upgrader framework, tested with a synthetic older version.
  - **Structural validation**, with stable error codes:
    - limits, alias rules and reserved aliases;
    - unique keys, existing references and cycles;
    - pattern safety.
  - **The public projection** and its hash.
  - Done when: unit tests cover every structural rule, round trips are lossless, and a property-style test over generated definitions shows that the public projection never contains action settings, recipients or option-source configuration.
- [ ] **4.3 Repositories, the file store, the audit log and stats**
  - One repository per aggregate (forms with versions and the field catalogue, folders, entries with values and files, events and notes, action runs, emails, option lists, themes, layouts, templates, settings, permissions, audit, stats, key-value).
  - All async, inside Umbraco scopes, through the database gate (D7). Paging, filtering and sorting happen in the database.
  - **The entries query builder** turns typed field filters into SQL against `jfbEntryValue`, using only the shapes of ADR-0003 (`IN` subqueries, keyset paging, the value-index walk for a sort by a value, one-pass search with a count up to 1,000) under the per-statement time budget.
  - **The file store:** the physical `IFormBuilderFileStore` per ADR-0006, in a folder that is not web-accessible. Uploads arrive in 6.1, hardening in 7.3.
  - **The audit log and daily stats:** port the pattern (§3.3).
  - Done when:
    - integration tests cover CRUD for each repository, concurrency conflicts on forms and settings, **unique entry numbers under 50 parallel inserts**, every filter kind (with the correctness cases of 1.3), and storing, reading and deleting files;
    - on SQLite, `EXPLAIN QUERY PLAN` tests show every shape of the query builder on its index, with and without planner statistics: no `SCAN` of `jfbEntryValue` and no temporary b-tree for a sort by a value (ADR-0003);
    - a statement past its time budget is interrupted and answered with `query_timeout`, and a submission made meanwhile still succeeds; a search with more than 1,000 matches reports "more than 1,000";
    - tests prove there are no field values in audit details, and that counters are correct under parallel increments.
- [ ] **4.4 The settings framework** (port the pattern, §3.3)
  - **Model:** typed sections (§4.8) with defaults, validators and JSON schema versions with migrations, plus global and per-site scope.
  - **Host options:** bound and validated at start (`ValidateOnStart`); locks win over the backoffice, including `Designer:SettingLocks`.
  - **Runtime:** a cached reader invalidated by a cache refresher, and a `SettingsSaved` notification. Ship a JSON schema for `appsettings` IntelliSense (with a `!` exception in `.gitignore`), and test that its `$ref`s resolve from the file itself (Tandem's first one resolved them from the options class, Tandem 11.1b).
  - Done when: unit tests cover every validator and default; migrating an old payload works; a lock overrides the backoffice value; an invalid host config fails start-up with a clear message.
- [ ] **4.5 The form service**
  - **Create:**
    - from blank, with the configured new-form defaults;
    - from a template: ship the built-in "Contact" template now, as embedded JSON (12.3 adds the others);
    - by import.
  - **Save and publish:**
    - save draft with a concurrency check;
    - publish: run the structural validation (4.2), update the field catalogue, and bump the public revision only when the public projection changed. Publishing is a pipeline that later steps extend: the form check joins in 9.5b, the mirror file in 12.5;
    - unpublish.
  - **Versions:** list, diff, restore (as a new version), pin and comment, plus the draft-version cleanup job.
  - **Copies and moves:**
    - duplicate, with or without actions, remapping every key and every rule, action and outcome reference;
    - copy actions to another form, remapping fields by alias and reporting the unmapped ones;
    - move.
  - **Trash and delete:** trash, restore, delete permanently (cascading entries, files, versions, permissions and references), empty trash.
  - **Folders**, and notifications for every lifecycle event.
  - Done when: unit and integration tests cover each operation, including:
    - "restore creates a new version";
    - "duplicate remaps rule references";
    - "delete removes the files from the file store";
    - "a stale save returns `concurrency_conflict`".
- [ ] **4.6 Permissions**
  - **The model** (ADR-0013):
    - permissions: design forms, manage actions, view entries, view sensitive data, edit entries, delete entries, export, manage library, manage settings, manage permissions, use privacy tools;
    - granted per user group globally, per folder or per form, combined as a union.
  - Authorization handlers for the management API, plus the "who may see this form" filter for trees, search and the inbox.
  - The E2E global setup (3.4) grants each test role its Jiron Form Builder permissions.
  - Done when: unit tests cover the evaluation, and the E2E users have their grants.
- [ ] **4.7 The management API foundation**
  - **The base:**
    - a base controller, `[MapToApi("jfb")]`, and the auth policies from 4.6;
    - ProblemDetails with stable codes, and a test that each refusal keeps its `code` in the body (Umbraco's error handling and validation answers can drop it, §3.3);
    - the OpenAPI document `jfb`, with a snapshot test that writes `docs/api/jfb.json`;
    - the TypeScript client in the backoffice package, generated from that committed snapshot rather than from a running site, so the client and the demo never wait on each other.
  - **The first endpoints** (§4.7): forms and folders (tree, search, CRUD, publish, versions, trash) over the 4.5 services.
  - **The permission matrix test:** it enumerates **every** management endpoint by reflection, asserts there is more than zero, asserts each one declares a Jiron Form Builder permission, and calls each as a user without that permission, expecting `403`. It fills route parameters by type, because an `int` route given a GUID answers `404` before the permission check. Every later step that adds endpoints is covered by it automatically.
  - Done when: the integration tests for the endpoints and the matrix test pass, and the client is generated and committed.
- [ ] **4.8 Phase review**
  - A subagent reviews the Core diff for SOLID, naming, async and load-balancing safety (no static mutable state). Fix what it finds.
  - Check the coverage report (≥ 85% on Core).
  - Tag the end of the phase as `baseline/schema-v1` and push the tag. Step 16.5 packs this tag as the "old version" for the upgrade test.
  - **The schema is frozen from here:** `CreateSchemaV1` is never edited again, and every change is a new migration step. A test pins the hash of the v1 snapshot DTOs.
  - Write the handover note.
  - Done when: pushed, with the phase-review gate.

### Phase 5: Field types, option lists and the logic engines

- [ ] **5.1 The field type framework**
  - **The registry:** a collection builder with `FormBuilderFieldTypeBase<TSettings>`, and settings schemas from attributes (exposed to the backoffice as JSON).
  - **Field values:**
    - mapping from storage kinds to `jfbEntryValue` columns;
    - the parse and validate contract;
    - an availability check ("not configured: …").
  - **Changing types:** a "change type" compatibility map, and `Designer:SettingLocks` applied.
  - Done when: unit tests pass, including a custom type registered by a test composer and a type removed from the collection.
- [ ] **5.2 Rules and validation engines** (C# and `packages/logic`)
  - Implement §4.3.3 in both runtimes, with form-level validation rules and the "hidden means gone" propagation (a field hidden because its group or page is hidden).
  - **Vectors (≥ 300 cases):**
    - every operator against every storage kind;
    - empty and unchecked values, and multi-values;
    - relative dates with an injected "today" in two time zones;
    - nested groups and hidden propagation;
    - the `require` effect, conditions that compare two answers, context conditions (culture, member, query value, today) and switched-off rules.
  - Done when: both runtimes pass every vector, and a scripted mutation check flips each TypeScript operator in turn and shows at least one failing vector for each.
- [ ] **5.3 Merge tags and calculations** (C# and `packages/logic`)
  - Implement the sources, filters and escaping contexts of §4.3.3, plus unknown-tag handling and the calculation parser and evaluator, with no `eval`, no reflection and documented rounding.
  - **Also:** answer recall (only fields above the text or on earlier pages, hidden fields empty), `entry.reference`, and the date functions `daysBetween` and `yearsBetween` (leap days and a daylight-saving change in the vectors).
  - Done when:
    - the vectors pass in both runtimes, including answer recall and the date functions;
    - a fuzz test of 10,000 random templates and formulas never throws an unhandled exception and never takes longer than 50 ms per input;
    - an injection test shows that merge tags cannot produce HTML, URL or JSON breakouts.
- [ ] **5.4 Field types (server side)** (server side)
  - Implement the types of §4.3.2 from Text to Country: parsing, validation, storage, "Other" options, consent text hashing into `jfbConsentText`, and country names from .NET culture data.
    - **Grid and ranking** (added in 1.1): each grid row has its own key and is stored like a child field (`ParentFieldKey` = the grid, §4.5), so it filters, sorts and exports like any field; a ranking stores one value per position (`ValueIndex`). With the required-rows rule and unique positions.
    - **Smaller settings:** word limits, excluded dates, the camera option on file fields, and place limits on options (the counting itself comes with the pipeline in 6.1).
    - **Numbers** accept at most 15 significant digits, so the stored double is exact (ADR-0003); a unit test refuses a 16-digit number with its own message.
  - **Advanced fields:**
    - **Files and signatures:** file (linking upload tokens) and signature (PNG checks on size, dimensions and magic number).
    - **Composite:** name, address, and repeater (rows, limits, per-row validation).
    - **Computed and hidden:** calculation (server recompute), hidden (server-only sources resolved on submit, never rendered).
    - **Content types** and captcha placement.
  - Done when:
    - unit tests per type cover valid, invalid and edge values, and an integration test stores every type and reads it back typed;
    - tests per type pass, including a grid with a missing required row, a ranking with two options in one position, and a hidden field whose member value never appears in the rendered markup.
- [ ] **5.5 Option lists and phase review**
  - **Kinds:**
    - manual;
    - content: root from a picker or "the current page", document type filter, value and label fields, descendants, order, published only, member-protected pages excluded;
    - data type;
    - URL JSON (allowed hosts, a JSON path mapping);
    - file (CSV or TXT, `value|label`);
    - countries;
    - code (`IFormBuilderOptionSource`);
    - **presets** (added in 1.1): months, weekdays, agreement and satisfaction scales, and "yes, no, not sure", in English and German (14.2 adds the other five), with month and day names from culture data.
  - **Behaviour:** per-culture labels, caching with invalidation on content publish, and cascading parents.
  - **Phase review:** write the handover note.
  - Done when:
    - tests per kind pass, including SSRF refusal for URL lists, cache invalidation on publish, and the presets in English and German;
    - pushed, with the phase-review gate.

### Phase 6: Rendering, the website runtime and headless

- [ ] **6.1 The public API and the submission pipeline**
  - Set up §4.6 at a configurable base path, with CORS, ProblemDetails codes, request limits and the OpenAPI document `jfb-public` (with a snapshot test).
  - **Endpoints built here:** the form definition, `validate`, `entries`, `receipt`, `options` and `uploads` (basic limits, using the 4.3 file store; hardening in 7.3). The others are built and tested by the steps that need them: `challenge` (7.1), `drafts` and `resume` (8.6), `verify` (8.5), `events` (11.5).
  - Implement the pipeline of §4.4 end to end (spam layers come in Phase 7): availability, validation, idempotency (the server generates a `submissionId` when none is posted), the single transaction, outcomes.
    - **Places on options** (§4.4): taken in the transaction, given back on reject, spam, delete and erase.
    - **Empty submissions are refused:** a submission with no answer at all fails validation with a form-level message.
    - **Values kept only until the actions have run** are stored only when an action run applies (§4.4; their deletion comes with the processor in 8.1).
  - Done when: integration tests cover each endpoint built here for success; validation errors by alias; a replayed submission returning the same receipt and a changed one returning `409`; `form_changed`, `form_closed` and the entry limit; CORS allowed and denied; oversize bodies. They also cover 50 parallel submissions for the last 10 places of an option (exactly 10 accepted), a rejected entry giving its place back, and an empty submission refused.
- [ ] **6.2 Razor rendering**
  - **Rendering:**
    - the view component and tag helpers (`<jfb-render>`, `<jfb-assets>`, included once per page automatically unless turned off), with the nonce option (`<jfb-assets nonce>` and `IFormBuilderCspNonceProvider`, ADR-0004);
    - **serving the assets** under `/jfb/assets/` itself:
      - the precompressed Brotli or gzip copy, chosen by `Accept-Encoding`;
      - `Cache-Control: public, max-age=31536000, immutable` under a `?v=` fingerprint;
      - `X-Content-Type-Options: nosniff`.

      Umbraco's static files send no compression and no caching headers (§3.4).
    - the form picker model `FormBuilderFormReference`;
    - the markup contract (`data-jfb-*` attributes, deterministic per-request instance ids);
    - member gating messages and culture.
  - **The no-JS post:** through the surface controller, with post/redirect/get and re-rendered errors, server-side paging for multi-page forms, and the outcomes (message, redirect, message then redirect). Our own `<form>` with `SurfaceControllerHiddenInput`, the redirect with the instance fragment, and the `UmbracoRouteValues` check, per ADR-0004.
  - **The output cache rules of ADR-0004:** `no-store` on every response of the surface action and on every render that is per visitor, not a `GET`, or carries `jfb-sent`; the `IWebsiteOutputCacheVaryByProvider` for `jfb-sent`; never an `IWebsiteOutputCacheRequestFilter`.
  - **The `render` endpoint** of §4.6 (for `form_changed`), through the same view component.
  - Done when: TestServer tests pass (including the compressed and cacheable asset responses), and Playwright with JavaScript **disabled** submits a valid form, an invalid one and a three-page one, with two forms on one page. With Umbraco's output cache on, tests also show: a cached page byte-identical for two visitors and holding nothing per visitor; `?jfb-sent` showing the outcome and never stored, for any value; no response to a post stored; the surface auto-route answering `400`; and `render` returning the published form with the given instance id.
- [ ] **6.3 Themes**
  - **`default`:**
    - CSS custom properties and an opt-in dark scheme;
    - the error summary plus inline errors, character counters;
    - styled native controls and choice cards;
    - the design from 2.3.
  - **`unstyled`:** semantic markup and classes only.
  - **`bootstrap5`:** Bootstrap 5 classes. Bootstrap itself is not shipped; the demo and tests load it (MIT, dev-only) to check the theme.
  - **Mechanics:**
    - a theme registry (`IFormBuilderTheme`);
    - view resolution in the order per form → theme → default, in the layout of ADR-0015;
    - versioned view-contract headers.
  - **Right to left** (added in 1.1): the themes use logical CSS properties only, take `dir` from the culture, and mirror directional icons. Building it into the first themes costs little; adding it later would mean touching every rule.
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before styling the themes (ADR-0019, ADR-0020).
  - Done when: Playwright screenshots of a kitchen-sink form (every field type) per theme at 375 and 1440 px, and once with `dir="rtl"`, are reviewed in `docs/ux/review-6.3.md`, which has a section per skill with its output and findings; axe is clean; the CSS is within budget; a lint check refuses physical `left`/`right` properties in the theme CSS.
- [ ] **6.4 The website runtime**
  - **Validation and rules:** progressive enhancement, plus client validation and rules from `packages/logic`.
    - **Messages:** English and German now; 14.2 adds the other five languages.
    - **Delivery:** one small file per language, loaded for the page's culture only, to keep the §4.12 budget.
  - **Live calculations and answer recall:** texts that use earlier answers update as the visitor types (§4.3.3).
  - **Submitting:**
    - fetch submission with an in-flight lock and a stable `submissionId`;
    - the error summary with focus management, and `aria-live` announcements;
    - `form_changed` handling with answer restore (§3.1), through the `render` endpoint (ADR-0004);
    - a submission id that does not depend on `crypto.randomUUID()`, which exists only in secure contexts, and chunk file names with a content hash (ADR-0004).
  - **The developer surface and constraints:** the JS API and DOM events (§4.9), the CSP nonce, no cookies.
  - **Field behaviours:**
    - Loaded on demand: uploads with progress (against `uploads`), the repeater, the signature pad (with the typed alternative), character counters, choice cards and the "Other" input.
    - The ranking field's move buttons with live announcements, and the grid's stacked layout on phones.
  - Done when:
    - Vitest covers the runtime;
    - Playwright on the demo covers validation, rules, calculations, answer recall, submission, `form_changed` and every field type's interaction, with `form_changed` on a page served stale from the output cache (the published form swapped in, the answers kept);
    - Playwright shows no CSP violation with `script-src 'self'` on a cached page and with a nonce on an uncached one, lazily loaded chunks included, and a submission from a plain-HTTP origin;
    - a **keyboard-only** run completes the kitchen-sink form;
    - axe is clean;
    - the size check passes with every chunk.
- [ ] **6.5 Multi-page, summary and prefill**
  - **Moving through pages:**
    - per-page server validation (`validate`), back without losing answers;
    - progress styles;
    - the summary page with "change" links.
  - **Keep a copy** (added in 1.1): the outcome option of §4.3.4 shows the submitted answers with a print button and a "fill in again" link, reusing the summary page's rendering:
    - only in the response to the submission: after a no-JS post, the redirect carries a short-lived, single-use signed token, never answers in the URL;
    - never cached (`no-store`), sensitive values masked, print styles only.
  - **Prefill:**
    - from members, with "show" and the tamper-proof "hide" mode;
    - from allowed query values;
    - from context values.
  - Save and resume needs the email outbox, so it comes in 8.6.
  - Done when: Playwright journeys pass for multi-page forms and the summary page, and a tamper test proves that a posted value for a hidden member-prefilled field is ignored. Playwright also shows the copy of the answers after a JS and a no-JS submission and in print media (`emulateMedia`), and a reused or expired token shows nothing; a three-page journey passes with `dir="rtl"`.
- [ ] **6.6 Headless**
  - **The definition:** the endpoint with `ETag`, and TypeScript types for the public projection (generated from C# or the JSON schema and tested for drift), shipped in the package and in `schemas/`.
  - **The renderer:** `<jfb-form>` (packages/element) renders the default theme's markup from JSON with the same runtime.
  - **The Content Delivery API:** the form picker returns `{key, alias, name}`, and with expansion, the projection.
  - **Server-key submissions.**
  - Done when: a static page on 5521 (cross-origin, allowlisted) renders and submits a multi-page form through the element; the Delivery API test passes; server-key tests pass.
- [ ] **6.7 The form picker, blocks and "used on"**
  - **The data type "Jiron Form Builder form picker":** single or multiple, allowed folders or forms, optional theme override and redirect override. Plus the value converters and the UFM block-label component.
  - **"Create a form block for me":** an idempotent service that creates a "Form" element type and data type, and never changes existing types.
  - **"Used on":** reference tracking per ADR-0017, with the references endpoint of §4.7, so that trash and delete can name the pages that use a form (9.2).
  - **Eviction:** an `IWebsiteOutputCacheTagProvider` tags the pages that use a form with `jfb-form-<key>` from the "used on" index, and publishing a form evicts that tag (ADR-0004).
  - Done when: integration tests pass (including "a page that picks the form is listed as a reference, and stops being listed after the pick is removed", and, with output caching on, "publishing the form re-renders a cached page that picks it at once"), and Playwright shows an editor picking a form in a block on the demo and the page rendering it.
- [ ] **6.8 Phase review**
  - An accessibility pass over the themes (axe, keyboard, screen-reader names), and a security pass over the public API.
  - Write the handover note.
  - Done when: pushed, with the phase-review gate.

### Phase 7: Spam protection and file security

- [ ] **7.1 Standard protection, content rules and the spam folder**
  - **Signals:**
    - a honeypot whose name varies per public revision;
    - a minimum fill time from the signed challenge;
    - the `challenge` endpoint (§4.6) and its use by the runtime for JS posts;
    - no time signal on no-JS posts, because the markup is cache-safe (§3.4). They are scored on the honeypot, rate limits and content rules (ADR-0012), and a test covers that case;
    - the challenge id stored on the entry (unique per form), so a solved challenge can't be replayed.
  - **What cached markup cannot carry** (§4.6): the challenge response also brings the places left per option and the member values for rules. The runtime then shows the places ("3 places left", "full") and evaluates member conditions with them; the server has refused full options since 6.1 either way.
  - **Limits:** the ASP.NET Core rate limiter per IP, form and site, with bounded partitions; origin checks; antiforgery on members-only forms; the blocked address ranges of the spam settings (§4.8).
  - **Scoring:** the spam score with reasons, and silent acceptance into `Spam` (§4.4).
  - **Content rules and the spam folder:**
    - Blocked words, blocked email domains, maximum links, per-form strictness, and `IFormBuilderSpamCheck`.
    - The `Spam` state. "Not spam" runs the submitted actions then.
  - Done when:
    - integration tests cover each signal, and a flood from 20,000 distinct IPs neither bypasses the site-wide limit nor grows memory without bound;
    - tests cover a blocked IPv4 and IPv6 range, and Playwright shows the places left changing on a cached page after another visitor's submission, and a member condition applied for a signed-in member;
    - tests cover each content rule, a custom `IFormBuilderSpamCheck`, and "not spam" running the submitted actions.
- [ ] **7.2 Proof of work and captcha providers**
  - **Proof of work:** a SHA-256 puzzle with difficulty per level, a signed and expiring nonce, solved in a Web Worker with a progress hint for slow devices.
  - **Third-party providers:** reCAPTCHA v2 and v3 (ported from Ligata.Forms) and hCaptcha. Providers are greyed out without keys. The consent modes of §4.10 apply.
  - Done when:
    - unit tests pass against the HTTP stub;
    - Playwright, with the provider hosts routed to a local stub (`page.route`, so no real third-party request ever happens, D22), shows **zero requests to them before consent** and the script loading after;
    - proof of work completes in under 2 s at the default level on the demo machine.
- [ ] **7.3 File security**
  - **Upload checks:**
    - streaming limits;
    - the allowlist, the global blocklist and magic-number checks;
    - SVG and HTML never served inline;
    - random names.
  - **Cleanup and scanning:** the orphan-cleanup job, and the scanner hook with quarantine.
  - **Downloads:** protected and permission-checked; ZIP exports safe against path traversal.
  - Done when: tests cover a polyglot file, a double extension, an oversize stream aborted early, a zip-slip name, and a quarantined file.
- [ ] **7.4 Phase review**
  - A security review of Phases 6 and 7 (STRIDE for the public surface). Fix what it finds, with a test per fix.
  - Write the handover note.
  - Done when: pushed, with the phase-review gate.

### Phase 8: Actions and email

- [ ] **8.1 The action framework**
  - **The model:** a registry with `FormBuilderActionTypeBase<TSettings>`, triggers, rules, order and "stop the rest if this fails".
  - **The processor:** a distributed job plus the in-process kick, with leases, back-off and the `Uncertain` state.
  - **The pause switch** (§4.4): while `JironFormBuilder:Actions:Paused` is on, the processor claims nothing, and the kick does nothing.
  - **Values kept only until the actions have run** (§4.4): deleted, with the entry event, once the entry's last action run is final.
  - **The retry API,** which refuses to retry an `Uncertain` run without an explicit confirmation (§4.4).
  - **Test runs:**
    - email goes to the current user only;
    - webhooks send to the configured URL with `X-Jfb-Test: true` after a confirmation;
    - content actions create nothing (a dry run that shows what would be created).
  - Fill in the CI load-balancing job.
  - Done when: tests cover ordering, retries with fake time, lease expiry turning into `Uncertain`, and **no double execution** with two processors on one database. The `loadbalancing` job definition is shown. Tests also show that nothing runs while sending is paused and every queued run goes out once it is not, that a retry of an `Uncertain` run without the confirmation is refused, and that a value kept only until the actions have run is still there while a run is retrying and gone after the last one is final.
- [ ] **8.2 Email infrastructure**
  - **Sending:** the outbox and sender job through `IEmailSender`, with `CanSendRequiredEmail()` gating so no retries are burnt while email is not configured (Umbraco would drop the mail silently). Queuing wakes the sender at once (D8). The sender also honours the pause switch (§4.4).
  - **Content:** responsive HTML layouts that work in dark mode, a text part as ADR-0007 decided, header sanitising, attachment caps, the per-site sender, a test email, and retention purge. The layouts follow the client rules of §3.3: inline styles on table cells, `<!--[if mso]>` tables for Outlook, and no styles on `<body>`.
  - **The limits for visitor-addressed mail** (§4.10): per recipient address (counted on `RecipientKey`, §4.5), per site, and the "limited" delivery state.
  - Done when:
    - tests pass with the pickup folder;
    - HTML snapshot tests pass;
    - every email is well-formed with inlined CSS (checked by a test);
    - a long unbroken word stays inside a 375 px wide rendering of every layout (Playwright on the `.eml` HTML);
    - tests show the per-address and per-site limits on SQLite and SQL Server, with two spellings of one mailbox that differ only in non-ASCII capitals counting as one, and the "limited" state in the delivery log;
    - a test shows that no email leaves while sending is paused, and that all of them leave once it is not.
- [ ] **8.3 The email action**
  - **Addressing:** the recipient kinds, CC/BCC, reply-to a field.
    - **A recipient map** (added in 1.1): the options of a choice field ("Sales", "Support") map to internal addresses, which stay out of the public projection (§4.3.1).
  - **Content:**
    - subject and body per culture with merge tags;
    - `{{ entry.table }}` (all answers as a table, optionally without the unanswered fields);
    - "include sensitive data" (off by default);
    - attachments;
    - a Razor view option (`Views/JironFormBuilder/Emails/*.cshtml`).
  - **Presets** ("Notify the team", "Confirm to the sender").
  - Done when: tests pass, and an E2E run submits the demo contact form and finds two emails in the pickup folder with the right content, escaping and language. An abuse test also passes: a submission that puts a stranger's address in the email field and a link in the message gets the confirmation through only within the limits, and the link is not clickable in it. A test shows the recipient map choosing the address and the mapped addresses absent from the public projection.
- [ ] **8.4 Webhook, Slack and Teams**
  - **The webhook:**
    - JSON, form-encoded or XML;
    - field mapping, metadata;
    - the HMAC signature (`X-Jfb-Signature: t=…, v1=…`, verification documented with code in C#, Node and PHP);
    - secret references, the SSRF guard, timeouts and retries.
  - **Slack and Teams:** presets with formatted text.
  - **Umbraco webhook events:** registration (§4.4).
  - Done when: tests pass against the stub on 5560, the SSRF refusal tests pass, and the signature verification samples are tested.
- [ ] **8.5 Content, state, verification and approval actions**
  - **Create or update content:**
    - document type, dynamic or picked parent, field-to-property mapping with type compatibility, publish or save;
    - uploaded files are not copied to media unless a mapping says so, and then into a chosen folder with the same file checks.
  - **Set state** under a rule.
  - **Email verification (double opt-in):**
    - the `Unverified` state and the email with its link;
    - the GET confirm page and the POST confirm (§4.6);
    - expiry and resend;
    - actions run after verification.
  - **Manual approval:**
    - The approved and rejected triggers, with bulk and single changes.
  - Done when:
    - tests pass, and E2E double opt-in works from the pickup email, including a simulated link-scanner GET that verifies nothing. A test shows that verification mail obeys the visitor-address limits (§4.10);
    - tests cover the approved and rejected triggers, for single and bulk changes.
- [ ] **8.6 Save and resume, and phase review**
  - **The flow:**
    - the `drafts` and `resume` endpoints (§4.6), with the `Draft` state and the resume email through the outbox;
    - the token in the URL fragment, never sent to the server in a query string;
    - expiry, and the page restored;
    - the runtime's "Save and continue later" control (the only use of browser storage, §4.10);
    - drafts purged when they expire.
  - **Phase review:** write the handover note.
  - Done when:
    - Playwright resumes a three-page form from the link in the pickup email, and tests cover expiry, a second resume, the submit that turns the draft into an entry, and the visitor-address limits for resume mail (§4.10);
    - pushed, with the phase-review gate.

### Phase 9: The backoffice I: section, form library and designer

Build from the Phase 2 designs. Every screen in this phase and the next must pass the measurable rules of §4.13 before its step is done.

- [ ] **9.1 Section, shell and setup**
  - **The section:** `Jiron.FormBuilder.Section` ("Forms") with its sidebar:
    - the **Forms** tree;
    - **Entries:** all entries, spam;
    - **Library:** option lists, themes, email layouts, templates;
    - **Settings:** general, email, spam, files, privacy, access, deliveries, audit log, privacy tools.

    Menu items appear only for users with the matching permission. Items whose screens come in later phases appear with those phases.
  - **The dashboard:**
    - the setup checklist of §4.13, each item with a detector on the server that knows when it is done;
    - recent entries;
    - health warnings.
  - **Health checks** (also on Umbraco's Health Check dashboard): email can be sent, the uploads folder is not web-accessible, the Data Protection key ring is persisted (needed for challenges across restarts and servers), jobs are running, deliveries are failing, sending is paused (§4.4), the mirror has conflicts.
  - **Plumbing:**
    - en/de localisation infrastructure, the generated API client, and the global search provider;
    - the safe confirm helper (§4.10), with the source test that bans direct use of Umbraco's confirm modal.
  - Done when:
    - Playwright shows that each role sees exactly its menus;
    - a confirm that names a form called `<img src=x onerror=alert(1)>` shows that text literally (Playwright);
    - each checklist detector has unit tests (the full green run on the demo is checked in 12.6, once every linked screen exists);
    - screenshots are reviewed in `docs/ux/review-9.1.md` against §4.13.
- [ ] **9.2 The form library**
  - **The tree:** folders, and create from blank or from the template gallery (a modal with previews; it shows the templates that exist, which are "Contact" from 4.5 until 12.3 adds the rest).
  - **Entity actions:**
    - rename, move;
    - duplicate (with or without actions), copy actions to another form;
    - import as a new copy, export JSON (12.5 adds "replace" and the mirror).
  - **Trash:** trash, restore, empty trash. The delete confirmation names the pages that use the form (6.7).
  - **Search:** forms by name and alias, and fields across all forms by label or alias (from the field catalogue, §4.5).
  - Done when: Playwright journeys for each action pass, including finding a form by one of its field labels, and the client stores have unit tests.
- [ ] **9.3a The designer: workspace and editor model**
  - **The workspace:** the form workspace with the views Design, Settings, Actions, Entries, Insights and History (views without content yet show a short "coming with …" empty state, replaced by their steps). The name and status are in the header; Save draft and Publish in the footer.
  - **The editor model** is pure TypeScript with unit tests (the Ligata.Forms lesson, §3.1):
    - selection;
    - insert, move, duplicate and remove;
    - undo/redo;
    - the rule-safety checks that refuse moves breaking a rule.
  - Done when: Vitest covers every editor-model operation, and the workspace loads and saves a form through the API.
- [ ] **9.3b The designer: canvas and palette**
  - **The canvas:** pages as steps, groups with columns, fields rendered by small Lit preview elements per type, insert points, and canvas widths for phone, tablet and desktop.
  - **The palette:** search, groups, recently used.
  - **Inline label editing.**
  - **Paste a list of questions** (added in 1.1): one line per question becomes a text field with that label, ready to change type.
  - Done when: Playwright builds a three-page form with the mouse and adds five fields from a pasted list, and screenshots are reviewed in `docs/ux/review-9.3.md`.
- [ ] **9.3c The designer: drag and drop and keyboard**
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
- [ ] **9.4 The inspector**
  - **A generic settings renderer** from type schemas, with these editors:
    - text, number, switch, select;
    - rich text (Tiptap), merge-tag-aware text with a tag picker;
    - content and media pickers;
    - an option editor (drag to reorder, paste many lines at once, images and descriptions for card layouts, a place limit per option), which also edits a grid's rows and columns;
    - a pattern editor with a live tester.
  - **Sections:** Essentials, Validation, Rules, Appearance, Advanced (§4.13).
  - **Per-culture texts** with a culture switcher and "missing translation" marks.
  - **Fields:** alias editing with its lock rules, and change field type (compatible types only, with a preview of what is lost).
  - **Locks:** host-locked settings are shown as locked.
  - Done when: Playwright configures every built-in type through the UI, and axe is clean on the inspector.
- [ ] **9.5a The rules builder**
  - **The builder:** rule rows in plain language ("Show this field when **Country** **is** **Germany**"), any/all groups, operators per field type, and value pickers (options, dates including relative ones, numbers).
    - "Required when", a value taken from another field ("is after **Arrival**"), and the context conditions (language, member, query value, today), all of §4.3.3.
    - A switch that turns a rule off without deleting it.
  - **Seeing rules:** chips on the canvas, and a **rules overview** listing every rule in the form with links.
  - **Form-level validation rules** in the same builder.
  - Done when: Playwright builds nested rules of every operator kind, a "required when" rule, a comparison of two fields and a context condition, and the designer's live rule preview agrees with the shared vectors (D10).
- [ ] **9.5b The form check**
  - **The check runs on the server and joins the publish pipeline:** errors block publishing, warnings don't. Every issue has a "fix it" link in the designer. The checks:
    - a required field that a rule can hide;
    - an email action with no email field to reply to;
    - missing labels or alt texts;
    - duplicate aliases, unknown merge tags;
    - unreachable pages, rules on removed fields;
    - personal data collected without a consent field (a hint);
    - a captcha provider that isn't configured;
    - an outcome redirecting to an unpublished page;
    - a file field without limits;
    - a text that recalls a later field's answer (an error, §4.3.3);
    - a redirect that puts answers into its URL (a warning: answers in URLs end up in logs and browser history);
    - labels or patterns that ask for card numbers or passwords (a warning, §1.4);
    - a required choice whose options can all become full;
    - and anything Phase 1 adds.
  - Done when: each check has unit tests, publishing with an error is refused through the API, and Playwright covers the badge, the list and the "fix it" links.
- [ ] **9.6 Phase review**
  - **UX review:** every screen of the phase at 375, 768 and 1440 px, in light and dark, in en and de, each checked against the measurable rules of §4.13, plus a pass of all three design skills (ADR-0019, ADR-0020): `design-taste-frontend`, an `impeccable` critique and audit, and an `emil-design-eng` review. Fix the findings.
  - Write the handover note.
  - **The backoffice budget:** set the backoffice bundle's gzip budget in the size check from the measured size plus a stated margin, and record it in §4.12.
  - Done when: `docs/ux/review-9.md` with screenshots, with a section per design skill, is committed, the size check enforces the backoffice budget, and everything is pushed, with the phase-review gate.

### Phase 10: The backoffice II: settings, actions, preview and publishing

- [ ] **10.1 The form settings view**
  - The groups of §4.3.4 with help text. The outcomes editor (an ordered list with rules), plus availability, spam, privacy, appearance and developer settings.
  - Done when: Playwright covers each group, including validation messages.
- [ ] **10.2 The actions view and its editors**
  - **The list:** action cards per trigger (Submitted, Approved, Rejected, Verified), with order (drag and keyboard), an on/off switch, a rule chip and the last-run status.
  - **Adding:** an "add action" modal with presets.
  - **The email editor:** a recipient builder, the merge-tag picker, and a live preview with a sample entry.
  - **Test run.**
  - **The other action editors:**
    - **Webhook:** a mapping table, a signature help text and a test.
    - **Slack and Teams.**
    - **Content:** document type and property mapping.
    - **Set state.**
    - **Verification.**
  - Done when:
    - Playwright configures an email action from a preset, and a test-run email lands in the pickup folder;
    - Playwright configures and test-runs each type (webhooks against the stub on 5560).
- [ ] **10.3 Preview, publishing and history**
  - **Preview:** a modal with the draft rendered by the chosen theme in an iframe (from a preview endpoint), device widths, light and dark, and a culture switch. Test submissions are marked `IsTest`. **Fill with sample answers** (added in 1.1) fills every field with valid values in one click, so editors can try rules and outcomes quickly.
  - **Publish:** a summary of the changes against the live version, and draft and unpublished badges.
  - **Conflicts:** when someone else saved first, show both versions and let the user choose; never overwrite silently.
  - **A guard** against leaving with unsaved changes.
  - **History:**
    - **The list:** versions (draft saves and published), with pins and comments.
    - **The diff:** a side-by-side comparison in plain language (fields added, removed and changed; settings, actions and rules).
    - **Restore** as a new version, and the version-cleanup settings.
  - Done when:
    - Playwright covers preview, sample answers that pass validation for every field type, a test entry, publishing, and a two-browser concurrency conflict;
    - the preview frame has an opaque origin (§4.10), and integration tests show the public API refusing a `null`-origin request without a valid preview ticket, with an expired one and with one for another form;
    - tests and Playwright cover the history: the diff, pins, comments and restoring as a new version.
- [ ] **10.4 Phase review**
  - UX review as in 9.6, plus the handover note.
  - Done when: `docs/ux/review-10.md` is committed and pushed, with the phase-review gate.

### Phase 11: Entries, exports and insights

- [ ] **11.1 The entries list**
  - **The table:** a collection table per form, a column chooser (saved per user in `jfbUserSetting`), server-side paging, "new" markers and mark-as-seen, and an action status ("3/3 delivered"). Each entry is named by its entry title (§4.3.4), or by its number when the form has none.
  - **Finding entries:** filters as chips (date presets and ranges, state, typed field filters), search and sort. A set of filters can be saved under a name (per user). A search shows its count exactly up to 1,000 and as "more than 1,000" beyond (ADR-0003).
  - **Working on many:** bulk actions (approve, reject, spam, not spam, delete, export selected, re-run actions) and the keyboard shortcuts of §4.13.
  - Done when: Playwright passes with 1,000 seeded entries, including a saved filter set reopened in a second browser, and the 100,000-entry budget of §4.12 is measured and met.
- [ ] **11.2 The entry panel**
  - **Values:** grouped by page and group, with fields from older versions marked as such, plus files and the consent proof (the exact text).
  - **Metadata:** time, page, culture, member link, IP if stored, duration, spam reasons.
  - **The timeline:** events, action runs with retry, and emails.
  - **Notes:** add and edit your own.
  - **State actions.**
  - **Edit mode:** validation per type, audited.
  - **Sensitive values:** masked, with an audited "reveal".
  - **Forward** (added in 1.1): send the entry to a backoffice user or to one of the team addresses of the email settings (§4.8), through the outbox. Sensitive values go along only when the user may see them and chooses to. Audited, and shown on the timeline.
  - Done when: Playwright passes, and the audit entries are asserted. A forwarded entry arrives in the pickup folder, and an address outside the allowed recipients is refused by the API.
- [ ] **11.3 The all-entries inbox and spam**
  - **The inbox:** a cross-form list with a form column and a form filter, unseen entries first.
  - **Spam:** the spam folder with "not spam".
  - **Badges:** unseen counts on the menu items, from a light count endpoint. The poll pauses while the user is idle and never renews an expired session (§4.10).
  - **The digest** (added in 1.1): each user can ask for a daily or weekly email ("12 new entries in 3 forms") instead of watching the inbox. It holds counts and links only, never entry values, covers only the forms the user may view, and goes out in the site's time zone through the outbox.
  - Done when: Playwright passes, including an idle user whose session still ends on time (`page.clock`). Tests with fake time show one digest per user and period, nothing for a user who has not asked for one, no form the user may not view, and no entry value in the mail.
- [ ] **11.4 Exports**
  - **Formats:**
    - **CSV:** delimiter, BOM, formula guard, headers as labels or aliases;
    - **Excel:** per ADR, either a small own XLSX writer or an MIT library; repeaters on their own sheet;
    - **JSON**, and a **printable view** of one entry and of a list;
    - **ZIP** of the files: a folder per entry, safe names.
  - **Columns:** a grid gives one column per row, and a ranking one column per position.
  - **The export dialog** remembers the last format, fields and delimiter per user and form (`jfbUserSetting`).
  - **Large exports:** a background job, a notification, and a time-limited download. Every export reads in keyset pages of 250 entries, each its own gated read, so submissions wait behind one page at most (ADR-0003).
  - **Rules:** every export respects the filters, the permissions and the sensitive-data rules, and is audited.
  - Done when: tests open the produced files (the XLSX with a test-only reader, CSV round trip, ZIP listing), and the 10,000-entry budget is met.
- [ ] **11.5 Insights**
  - **Collection:**
    - the runtime sends `view`, `start` and `page` beacons (`navigator.sendBeacon`) to `events`, unless insights are off;
    - when validation stops a visitor (on submit or "next"), it sends `invalid` with only the field key and the kind of error; the server also counts its own refusals;
    - the server rolls them up into daily stats;
    - unique members are counted from entries at query time.
  - **What it shows:**
    - views, starts, submissions and conversion;
    - drop-off per page (funnel), median time to complete;
    - the fields that fail validation most, with the kind of error ("Postcode: 31% of attempts, mostly the format");
    - top origin pages, hour of day, unique members;
    - a comparison period, grouped by day, week or month;
    - a site-wide overview table.
  - **Charts** are small, accessible SVG components with a table view (no chart library).
  - **Opt-out** per site and per form.
  - Done when: unit tests cover the aggregation, Playwright shows the beacons arriving and the charts filling on the demo (screenshots reviewed in `docs/ux/review-11.5.md`), and a test proves the stats hold no personal data (the `invalid` beacon included: a test posts a value with it and finds it nowhere).
- [ ] **11.5b Answer summaries** (added in 1.1 from the feature study)
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
- [ ] **11.6 Phase review**
  - UX review as in 9.6 for the entries screens, plus the handover note.
  - Done when: `docs/ux/review-11.md` is committed and pushed, with the phase-review gate.

### Phase 12: Library, global settings and deployment

- [ ] **12.1 Option lists**
  - A list, an editor per kind with a live preview of the options, CSV/TXT upload, the cache setting, and "used by".
  - Done when: Playwright passes.
- [ ] **12.2 Themes and email layouts**
  - **The editor:** a theme list, and a visual token editor:
    - light and dark colours;
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
    - Playwright covers the email layout editor, and its test email arrives in the pickup folder.
- [ ] **12.3 Templates**
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
  - **Saved groups** (added in 1.1): save a group of fields (an address block, a standard consent) as a template of kind `Group`, and insert it into any form from the designer's palette, as a copy with new keys, so every form stays self-contained for versions and exports.
  - Done when: a test creates and publishes a form from every template without form-check errors, and the gallery screenshots are reviewed in `docs/ux/review-12.3.md`. Playwright saves a group, inserts it into another form, and a test shows the copy's new keys and remapped rules.
- [ ] **12.4a Global settings and access**
  - **Settings:** the sections of §4.8 with host locks, for the features that exist by now. The privacy section's retention and IP settings come with 13.1 and 13.3 ("Settings come with their feature").
  - **Access:** a permissions editor (a matrix of groups and permissions, with folder and form overrides).
  - Done when: Playwright passes per section, the `403` matrix covers the new endpoints, and a test enumerates every setting and fails unless each has a screen (or is marked host-only) and is read by product code outside the settings module.
- [ ] **12.4b Deliveries and the audit log**
  - **Deliveries:** emails and webhook runs, with filters and retry. A retry of an `Uncertain` run asks first and says that it may deliver twice; while sending is paused, the screen says so once (§4.4).
  - **The audit log viewer,** with filters and export.
  - Done when: Playwright passes for both screens, including the confirmation before an `Uncertain` retry and the paused notice, and the `403` matrix covers the new endpoints.
- [ ] **12.5 Forms as files, import and export**
  - **Import and export:** the JSON import UI, with "new copy" or "replace".
  - **Mirror mode (D25):** write on publish, import on start-up, and detect conflicts (both file and database changed: keep the database, warn in the health check). Documented.
  - Done when: integration tests cover the mirror round trip and the conflict case.
- [ ] **12.6 Phase review**
  - UX review as in 9.6 for the library and settings screens, plus the handover note.
  - **The setup checklist end to end:** on a freshly reset demo, Playwright follows the dashboard checklist (9.1) through every item and sees each one turn green.
  - Done when: `docs/ux/review-12.md` is committed, the checklist run passes, and everything is pushed, with the phase-review gate.

### Phase 13: Privacy, retention and compliance

- [ ] **13.1 Retention**
  - **Rules:** days per state per form, with global defaults. `PurgeAfterUtc` is computed on every state change.
  - **A legal hold:** an entry can be kept with a reason and an end date.
  - **The job** deletes in batches, files and emails included, and offers a dry-run preview (counts per form). Everything is audited.
  - **The screens:** the retention settings (global and per form) with the dry run, now that the engine exists.
  - Done when: tests with fake time pass, including holds, and Playwright changes a retention setting and sees the dry run follow it.
- [ ] **13.2 Data subject tools**
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
  - Done when: tests and Playwright pass, including a test that searches **every Jiron Form Builder table** for the person's email address after an erase and finds nothing.
- [ ] **13.3 Sensitive data and IP addresses**
  - **Masking** in the UI, emails, exports and logs; the "view sensitive data" permission; audited reveals.
  - **IP modes:** none, anonymised (IPv4 /24, IPv6 /48) and full (only when the host allows it), with their settings screen.
  - **Values kept only until the actions have run** (§4.4, built in 6.1 and 8.1): audited end to end. The entry panel, exports and the privacy export show such a field as "not kept", and the designer explains the setting next to "sensitive".
  - Done when: tests pass, including captured logs, and a test searches every Jiron Form Builder table for a value kept only until the actions have run, after its last action run, and finds nothing.
- [ ] **13.4 Privacy documentation and phase review**
  - Write `docs/privacy/`:
    - **a facts sheet:** what Jiron Form Builder stores, where and for how long; that it sets no cookies; the browser storage key used only by "save and resume"; third parties only when a captcha provider is chosen;
    - a checklist for site owners;
    - privacy-policy section templates in en and de. **Each starts with "TEMPLATE, not legal advice; adapt with your data protection adviser".**
  - **Phase review:**
    - A subagent acts as a privacy reviewer (GDPR, TDDDG, revDSG, UWG §7 double opt-in, the European Accessibility Act) against the running demo and the docs. Fix what it finds, and record open legal judgements in OWNER-TODO.
    - Write the handover note.
  - Done when:
    - committed, and a test compares the storage keys and requests the runtime makes with the facts sheet;
    - pushed, with the phase-review gate.

### Phase 14: Languages, members and multi-site

- [ ] **14.1 Culture variants and backoffice localisation**
  - **Texts:** every `LocalizedText` in the designer, settings, emails and outcomes, with the culture switcher, "missing translation" marks, `#dictionary` keys (with a picker) and fallback.
  - **Entries** keep their culture, and exports format dates and numbers by it.
  - **Backoffice localisation:**
    - Audit that every backoffice string in en and de goes through localisation keys (each UI step added its keys, see "How to use").
  - Done when:
    - Playwright runs a form end to end in en and de, including the emails;
    - a lint rule that fails on hard-coded UI strings passes, and the German screenshots are reviewed in `docs/ux/review-14.1.md`.
- [ ] **14.2 Visitor texts in seven languages**
  - Add French, Italian, Dutch, Spanish and Danish to the runtime and server messages and the option presets (English and German came with 6.4 and 5.5). All seven are overridable per site and per form.
  - Done when:
    - a test fails when any key is missing in any language;
    - a review subagent per language reads the texts in context (screenshots) and its fixes are applied.
- [ ] **14.3 Members**
  - **Access:** members-only forms and member groups, one entry per member.
  - **Prefill** (show and tamper-proof hide), and the member link on entries.
  - **Member deletion:** a handler deletes or anonymises the deleted member's entries, per a setting.
  - **Entries on the member:**
    - A workspace view on the member editor lists that member's entries, with permissions applied.
  - Done when:
    - tests and Playwright pass with the seeded members, including the entries view on the member.
- [ ] **14.4 Multi-site and phase review**
  - **Per-site settings:** sender, base URL, defaults and theme, resolved by the request's root or host. Links in emails follow the site.
  - **Phase review:** write the handover note.
  - Done when:
    - integration and Playwright tests pass with two roots on `site-a.localhost` and `site-b.localhost` (loopback by themselves in Edge and Chromium, with Umbraco domains on each root; never edit the system hosts file). Playwright's API request context resolves `*.localhost` to IPv6 and reaches the default host instead, so API calls in these tests send the `Host` header explicitly (§3.3);
    - pushed, with the phase-review gate.

### Phase 15: Extensibility, samples and the AI add-on

- [ ] **15.1 The public C# surface and backoffice extension points**
  - Complete the services and notifications of §4.9, including `IFormBuilderEntryReader`. Write `docs/extending.md`.
  - **Backoffice extension points:**
    - **Manifests:** `jfbSettingEditor` (a custom editor for one setting) and `jfbFieldPreview` (the designer preview of a custom field type).
    - **Slots:** extra workspace views on the form workspace.
    - **Types:** TypeScript declarations for extension authors.
  - Done when:
    - a public-API snapshot test lists every public type and member and fails on unreviewed changes;
    - a sample extension registers a custom setting editor and a preview, and Playwright shows both working.
- [ ] **15.2 Samples**
  - `samples/Jiron.Umbraco.FormBuilder.Samples`:
    - a custom field type (a colour choice with swatches);
    - a custom action (a CRM stub);
    - an option source (a fake product API);
    - an export format (iCalendar for event registrations);
    - a captcha provider (a simple arithmetic check);
    - a spam check (link density);
    - a merge-tag source (values from a site-settings node);
    - a file scanner (a stub that quarantines files named "eicar");
    - a file store sketch for blob storage;
    - a notification handler that adds a field error.

    Each is compiled and tested in CI and documented in `docs/extending.md`.
  - Done when: the samples build and their tests pass.
- [ ] **15.3 The AI add-on and phase review**
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

### Phase 16: Hardening

- [ ] **16.1 Security review**
  - A STRIDE pass over the architecture, and an OWASP ASVS-style checklist on the code:
    - XSS, injection, authorisation on every endpoint, CSRF posture;
    - SSRF in webhooks and option lists, open redirects in outcomes;
    - uploads, DoS and limits, secrets.
  - Run `dotnet list package --vulnerable --include-transitive` and `npm audit --omit=dev`.
  - Fix the findings, with a test for each. An advisory in an upstream package that Jiron Form Builder cannot fix gets an ADR and a reviewed suppression in `NuGetAudit.props` (the Tandem ADR-0014 pattern), never a silent ignore.
  - Done when: `docs/security-review.md` exists and no high or critical findings are open.
- [ ] **16.2 Performance and load**
  - A Node load script (no copyleft tools) covers:
    - 200 submissions per minute with uploads;
    - lists, filters and exports over 100,000 entries;
    - render throughput;
    - the designer with 300 fields.
  - Check the query plans.
  - Done when: `docs/performance.md` has the numbers and §4.12 is met (or an ADR explains why not).
- [ ] **16.3 Accessibility audit**
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
- [ ] **16.4 Resilience**
  - Cover:
    - SMTP down (no retries burnt), webhook timeouts;
    - transient database errors (retry the outermost unit of work, Tandem ADR-0065);
    - a host restart in the middle of an action run (`Uncertain`, visible and retryable);
    - a late cache refresher, double processing under load balancing;
    - the network dropping during a submission (the receipt check);
    - blocked browser storage;
    - one broken form on a page not breaking the others.
  - Done when: each case has a test.
- [ ] **16.5 Upgrade and migration tests** (this step builds the install harness that 18.3 reuses)
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
- [ ] **16.6 UX polish pass** (D18, §4.13)
  - **Walk the full journeys** with screenshots at 375, 768 and 1440 px, light and dark, en and de:
    - an editor builds and publishes a multi-page form with rules and actions;
    - an entry handler works through the inbox;
    - an admin sets permissions and retention;
    - a visitor fills in a form on a phone;
    - a privacy officer erases a person's data.
  - **Critique and simplify:** rerun all three design skills (ADR-0019, ADR-0020): `design-taste-frontend`, an `impeccable` critique and audit, and an `emil-design-eng` review; a subagent with the §4.13 checklist may add a fourth view. Fix every finding that is not purely taste. Count the clicks for the top five tasks per role and remove the unnecessary ones.
  - Done when: `docs/ux/review-16.6.md` lists the findings and fixes (a section per skill) with before and after screenshots, and the E2E suites are still green.
- [ ] **16.7 Code quality and phase review**
  - **Code quality pass:**
    - Analyzers clean, no dead code. Everything not meant for extension is `internal`. XML docs on every public type. Consistent naming.
  - Write the handover note.
  - Done when:
    - `dotnet format --verify-no-changes` and the builds are clean, and the public API snapshot is reviewed;
    - pushed, with the phase-review gate.

### Phase 17: Branding, documentation, screenshots

- [ ] **17.1 Logo and icon**
  - **Design `assets/logo.svg`:**
    - **Concept:** a minimal form card (stacked field lines) whose submit button is a small check mark, drawn to sit next to the "Jiron" wordmark and to work alone as a 32 px section icon.
    - **Shapes and colours:** simple geometric shapes, legible at 32 px, with two brand colours and a neutral defined in `assets/brand.md`.
    - **No Umbraco logo, no "U" mark, and no resemblance to any other product's mark.**
  - Also design a monochrome variant and the section icon (registered through the `icons` manifest).
  - **Design skills:** load `design-taste-frontend`, `impeccable` and `emil-design-eng` before the logo work starts (ADR-0019, ADR-0020).
  - `scripts/icons.mjs` renders PNGs with Playwright (no new dependency): 512, 256, 128, 64.
  - Done when: the PNGs exist, the 128 px icon is checked visually (show the screenshot), and the brand doc is committed.
- [ ] **17.2a Documentation for editors and site owners, and the READMEs**
  - Under `docs/`:
    - **Start:** a `README` index, `getting-started.md` (install to first entry in 10 minutes), `installation.md`.
    - **For editors:** `editor-guide.md`, `field-types.md`, `rules-and-validation.md`, `merge-tags.md`, `actions.md`, `entries-and-exports.md`, `insights.md`, `themes.md` (the theme editor).
    - **For site owners:** `spam-protection.md`, `files.md`, `email.md`, `privacy.md`, `permissions.md`, `multi-language.md`, `members.md`, `multi-site.md`.
    - **Licence:** `license-faq.md`, plain words, clearly marked as not legal advice and pointing to `LICENSE.md` (OWNER-TODO: lawyer review). It answers at least: using it on client sites, changing it for your own projects, hosting, add-ons, contributing through forks and pull requests, and why publishing your own version (free or paid, open source or not) is not allowed.
  - Each page is checked against the running demo.
  - **README and NuGet readme:**
    - **`README.md` (GitHub):**
      - **About:** the pitch, the features, screenshots.
      - **Getting started:** a quick start, the requirements (Umbraco 17.5+), a link to the docs.
      - **Licence:** "free to use and change, including commercially; no selling, no published versions of your own (free or paid, open source or not), no competing products; source-available" with a link to the licence FAQ, never called "open source".
      - **Notices:** the trademark notice and how to contribute.
      - **Credits:** "Made by Manassés Zähnler · Ligata", linking jiron.dev and ligata.ch.
    - **`docs/README.nuget.md`:** shorter, with no relative links.
  - Done when:
    - a link-check script passes, and a subagent reads these pages as a new editor and its questions are answered;
    - both are committed and render correctly, checked in Edge through a local GitHub-flavoured Markdown renderer (dev-only, MIT), because the private repository can't be viewed from here. The owner checks them on GitHub once public (OWNER-TODO).
- [ ] **17.2b Documentation for developers**
  - Under `docs/`:
    - **Placement:** `rendering.md` (Razor, tag helpers, blocks, overriding theme views, CSS custom properties).
    - **Headless:** `headless.md` (API, element, types, the Delivery API).
    - **Integration:** webhook signature verification in C#, Node and PHP (in `actions.md`), and `deployment.md` (the mirror).
    - **Reference:** `configuration.md`, generated from the settings types, with a test that fails when the docs are stale.
    - **Internals:** `extending.md`, `upgrading.md`, `troubleshooting.md`, `faq.md`, `security.md`, `database.md`, `architecture.md` (updated).
  - Done when: the link check and the configuration reference test pass, and a subagent reads these pages as a new developer and its questions are answered.
- [ ] **17.3 Screenshots and phase review**
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

### Phase 18: Packaging, CI/CD, Marketplace readiness

- [ ] **18.1 NuGet metadata and `umbraco-marketplace.json`**
  - **On every package:**
    - `PackageId`, `Title`, `Description`, `Authors`, `Copyright`, `PackageProjectUrl`, `RepositoryUrl`;
    - `PackageLicenseFile LICENSE.md` (non-OSI licences cannot use an expression, §3.5), `PackageRequireLicenseAcceptance true`;
    - `PackageReadmeFile`, `PackageIcon` (icon-128.png);
    - `PackageTags`: the meta package and the AI add-on (both installed by sites) get `umbraco umbraco-marketplace forms form-builder contact-form`; Core and Web get `umbraco` only;
    - the meta package references `Umbraco.Cms.Web.Website` `[17.5.0, 18.0.0)` directly (§3.5);
    - SourceLink, `snupkg`, `EnablePackageValidation`, `ContinuousIntegrationBuild` in CI.
  - The appsettings JSON schema is shipped the way Umbraco merges package schemas *(verify)*.
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
- [ ] **18.2 Release workflow**
  - `.github/workflows/release.yml`, on tags `v*.*.*` and `workflow_dispatch`, in Tandem's final shape (Tandem ADR-0077):
    - a `prepare` job checks the version against the branch's Umbraco major and its `CHANGELOG.md` section, so a missing section fails in a minute;
    - build and test by calling `ci.yml` through `workflow_call` (copied CI steps drift), then the npm build;
    - `dotnet pack -p:Version=${tag#v}` through the one `scripts/pack.mjs` that the gate and CI use too;
    - attach the nupkgs and snupkgs to a **draft** GitHub Release with changelog notes;
    - a `publish` job with `if: vars.JFB_NUGET_PUBLISH == 'true'`, `permissions: id-token: write`, `NuGet/login@v1` and `dotnet nuget push --skip-duplicate`.
  - The publish job stays disabled; no nuget.org policy exists.
  - Document the owner's steps to turn it on in `docs/releasing.md`.
  - Done when: the workflow check passes, and a dry run of the pack steps in `scripts/ci-local.ps1` produces the same files.
- [ ] **18.3 Install test from packages**
  - With `scripts/install-test.ps1` (16.5), create a fresh Umbraco 17.7.1 site on 5530 that installs `Jiron.Umbraco.FormBuilder` (Jiron Form Builder packages only from the local feed).
  - **Smoke:** the section is visible to the admin; a form is created through the management API and placed on a page; it renders, submits and emails (pickup); the entry shows in the backoffice.
  - Run the same smoke after `dotnet publish` of that site (static assets, §3.2).
  - **The supported floor:** run the same smoke on a fresh **Umbraco 17.5.x** site (its own template hive), because D3 promises `[17.5.0, 18.0.0)`.
  - **An upgrade on the floor:** on that 17.5.x site, update the packages to a second build with a higher version, restart, and check in a signed-in browser that keeps its cache that the backoffice runs the new code without a hard refresh. 17.5 adds no cache busting to package files (1.2b), and §3.4 says Umbraco sends package files without caching headers.
  - Done when: the smoke runs pass on 17.7.1 (run and published) and on 17.5.x against the installed packages, and the 17.5.x upgrade check passes.
- [ ] **18.4 First prerelease and phase review**
  - Set the version to `17.0.0-alpha.1`. Update CHANGELOG and `docs/upgrading.md`.
  - Tag `v17.0.0-alpha.1` and push the tag. The release workflow builds a draft release; nothing is published.
  - **Phase review:** write the handover note.
  - Done when:
    - the tag is pushed and the local pack of the same version passes 18.3. You can't watch Actions, so add "check the draft release" to OWNER-TODO;
    - pushed, with the phase-review gate.

### Phase 19: The Umbraco 18 line (forward compatibility)

- [ ] **19.1 Branch strategy and an 18 build**
  - As decided in D16: `main` stays the 17 line and holds every ROADMAP and WORKLOG change. The 18 code lives only on `v18/main`, branched from `main`.
  - Tick Phase 19 steps on `main` only, after pushing `v18/main`, with the `v18/main` commit hash.
  - Work on `v18/main` through `git worktree` in `%TEMP%\jfb-v18`, so `main`'s working folder never switches branches mid-run.
  - **One-way merges** (Tandem ADR-0078): `main` is merged into `v18/main` after every later change to `main`, and never the other way. The two lines share ports, so only one line's gate runs at a time.
  - On `v18/main`, set CMS `[18.0.0, 19.0.0)`, `@umbraco-cms/backoffice` 18.x, Umbraco.AI 18.x for the add-on, and the version `18.0.0-alpha.0`.
  - **The OpenAPI port:**
    - move the registration from Swashbuckle to Microsoft.AspNetCore.OpenApi (`/umbraco/openapi`);
    - add the schema transformer that keeps the client's types as on 17 (§3.4);
    - regenerate the client and the snapshots per line;
    - replace the removed APIs.
  - Done when: the solution builds on CMS 18 with zero warnings.
- [ ] **19.2 Run everything on 18, and phase review**
  - Create an 18 sandbox on 5540 and an 18 demo on 5541, with Umbraco.Templates 18.x in its own template hive. Run the unit, integration and E2E suites. Fix what breaks behind the adapters. Document the differences in `docs/upgrading.md`.
  - **A real 17 → 18 upgrade:** the install test (16.5) learns the line. It installs the 17 packages on an Umbraco 17 site with data, then raises Umbraco and Jiron Form Builder to 18, and checks the data and Umbraco's own upgrade state.
  - **Phase review:**
    - Write the handover note on `main`.
  - Done when:
    - all suites pass on 18, the 17 → 18 upgrade passes, and `main` is untouched by 18 code and still green;
    - `v18/main` and `main` are both pushed, and `git ls-remote origin` shows both.

### Phase 20: Final verification and handover

- [ ] **20.1 Clean-clone verification**
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
- [ ] **20.2 Status report**
  - Write `docs/STATUS.md`:
    - what was built (features against §1);
    - test counts and coverage;
    - budgets measured;
    - known limitations;
    - the final OWNER-TODO list, with clear instructions for each item.
  - Update CHANGELOG. Every roadmap step is `[x]`, or `[~]` with its reason in OWNER-TODO.
  - If the owner's private reference exists, update the private mapping in `.private/` one last time (never committed).
  - Done when: committed and pushed, and the final output of `node scripts/roadmap-status.mjs` shows `open=0 blocked_missing_in_owner_todo=0 clean=yes` with `head` equal to `origin_main`.

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
