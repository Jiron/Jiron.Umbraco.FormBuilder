# Browser tests

Playwright Test against a fresh demo host for every run ([ADR-0011](../../docs/decisions/0011-testing-strategy.md),
[ADR-0031](../../docs/decisions/0031-browser-harness.md)).

```
cd tests/e2e
npm ci
npx playwright test --project=smoke
```

That is one site, as below. The gate runs every project on several sites side by side, each a whole copy of the run
([Several sites](#several-sites)): `node scripts/e2e-gate.mjs` from the repository root.

## The host

`global-setup.ts` builds the client assets (when stale) and the demo, then starts the demo's DLL on
`http://127.0.0.1:5520` with its own content root in `.local/e2e/host-5520/`: its SQLite database, the mail pickup
folder `mail/` and the log `host.log`. The teardown stops it with its whole process tree.

- **The Testing project** is on in this host only: the test-only endpoints under `/jfb-testing/`, the test clock, the
  stub on `http://127.0.0.1:5560`, and the start-up marker (ADR-0029, ADR-0031).
- **One server reads no cache instructions:** `hostEnvironment` sets Umbraco's times between two reads, and between
  two prunings, of the servers' cache instructions to a day. On SQLite's shared cache either of those jobs of
  Umbraco 17 and a request's own instruction can wait for each other, and every database call of the host waits with
  them (ADR-0253). A spec whose hosts share a database asks for `severalServers`, which keeps Umbraco's intervals.
- **The start-up wait:** Umbraco 17 registers its jobs and rebuilds its indexes about a minute after it starts, and
  the tests start only after that (`support/host-health.ts`). The wait ends on a log line that appears whether the
  Content Delivery API is on or off. If the work does not finish, the set-up fails with the reason and the end of
  the host's log.
- **The hang marker:** when the host's database hung, the run leaves `.local/e2e/host-5520/database-hung.txt`, and
  the gate runs that site's projects once more, on a fresh host. That is the only rerun ("Flaky means broken").
- **The static page** on `http://127.0.0.1:5521` (`support/static-site.ts`, ADR-0031): a site of its own whose page
  loads `<jfb-form>` from the host; the host's public API allows its origin, and `http://localhost:5521` is the same
  page on an origin the API does not allow. The headless spec starts it in its own process.
- **The AI add-on** (ADR-0018): the host offers Testing.AI's fake provider and chooses its chat profile in Settings ›
  AI at start-up (`JironFormBuilderTesting__FakeAI=Chosen`), and waits for its ready line, so the designer's Draft,
  Translate and Suggest answer without a real key (`specs/backoffice/ai.spec.ts`).
- **The samples** (ADR-0234): the host turns on the colour field type and the embed view, which the backoffice
  and website specs show; the protection host (`specs/website/protection-on-the-page.spec.ts`) also the arithmetic
  captcha, which would otherwise offer the captcha placement on every host.
- **The settings come from the harness only:** variables in your shell that would configure Umbraco or the demo are
  not passed on.

| Variable                  | Effect                                                                                  |
| ------------------------- | --------------------------------------------------------------------------------------- |
| `JFB_E2E_DELIVERY_API=on` | runs the host with the Content Delivery API on (default `off`)                          |
| `JFB_E2E_SKIP_BUILD=1`    | uses the demo and the client assets as they were last built                             |
| `JFB_E2E_CONFIGURATION`   | the demo's build configuration (default `Debug`)                                        |
| `JFB_E2E_SITES`           | how many sites the run has, 1 to 3 (default 1); `scripts/e2e-gate.mjs` sets it          |
| `JFB_E2E_SITE`            | which of them this run is, from 1 (default 1), or `budgets`; `scripts/e2e-gate.mjs` too |

## Several sites

`support/sites.ts` decides what a site is (ROADMAP 24.1, ADR-0261). One site, the default, is the run as it always
was. With `JFB_E2E_SITES` above 1, each site is a whole copy of the run, and nothing of it is shared with another:

- **Ports:** every server a spec starts has a port on each site (the `*_PORTS` lines of `support/sites.ts`): site 1
  keeps 5520 and the others above, site 2 takes 5562 to 5578 and site 3 5580 to 5596, in the same order (ROADMAP
  section 4.11). `support/paths.ts` gives each spec its own site's, under the names it always had.
- **State:** `.local/e2e/site-<n>/` holds the site's hosts (database, mail pickup folder, log, hang marker), its
  sign-in lock, results, report and `results.json`; the one site's is `.local/e2e/` itself.
- **People:** each site's global set-up generates its own passwords into `.local/credentials.json` under
  `e2e-site-<n>` (the one site's under `e2e`), one process at a time, so no site loses another's entry.
- **Spec files:** each spec file runs on exactly one site. `spec-times.json` holds the seconds of each spec file's
  tests; the sites share the files out, heaviest first to the lightest site, and the last site names no file: it takes
  every spec file the others do not name, so a new spec runs without anyone listing it. Inside a site the projects keep
  the config's order (`limits` after `website`, `site-wide` after the projects that read the settings).
- **`budgets`** runs on no site, but on the budgets site (`JFB_E2E_SITE=budgets`: site 1's ports, `.local/e2e/budgets/`)
  once every site has ended, so nothing else shares the machine while it measures (ADR-0169).

`scripts/e2e-gate.mjs` runs them, four workers each, as one site on SQLite carries (eight on one site failed tests).
Two sites is the default: on the development machine two already keep the processor busy all the time, and a third
only made every test slower (19.4 minutes with two, 18.9 with three, each with budgets):

```
node scripts/e2e-gate.mjs                  two sites side by side, then budgets alone
node scripts/e2e-gate.mjs --sites 3        three sites (1 is the one site of a default run, then budgets)
node scripts/e2e-gate.mjs --plan           what each site would run, checked, without running it
node scripts/e2e-gate.mjs --record         and rewrite spec-times.json from this run, when it passed
node scripts/e2e-gate.mjs --site 2/3       one site only, as the gate runs it
node scripts/e2e-gate.mjs --stop           stops a run it started, each process's whole tree by PID
node scripts/e2e-gate.mjs -- --grep inbox  arguments after -- go to every playwright test
```

Before anything starts, Playwright lists the projects' tests for the whole run and for each site, and the run stops
unless the sites' lists are the whole list, each test once. Unless `JFB_E2E_SKIP_BUILD=1`, the client assets and the
demo are built once for all the sites. Each site's console shows in the gate's log with its name in front, and in
`.local/e2e-gate/site-<n>-of-<count>.log`. A site whose host's database hung runs once more, alone of the sites; any
other failure fails the run. Once every site's process has ended and no port of theirs listens, the log says so, and
budgets starts; the summary names when it ran, and the run fails if its results began before a site's ended. At the
end the tests that ran are added up from each site's `results.json`, and the run fails unless they are the listed
tests, each once. The summary gives each site's tests, failures, skips, wall time, planned test seconds and runs,
budgets' run, the count against the projects and the whole time.

To look at one site's run again, set both variables: `JFB_E2E_SITES=3 JFB_E2E_SITE=2 npx playwright test` runs site
2's share of the spec files on site 2's ports and state.

## The people

Every run generates new passwords into `.local/credentials.json` under `e2e` (each of several sites under its own
entry); they are never printed.

| Persona          | User groups                            | Sections       | Jiron Form Builder permissions (ADR-0013)                       |
| ---------------- | -------------------------------------- | -------------- | --------------------------------------------------------------- |
| `admin`          | the unattended install's administrator | Umbraco's      | everything                                                      |
| `designer`       | `jfbDesigners`                         | Content, Forms | `design`, `manageActions`, `manageLibrary`                      |
| `layoutDesigner` | `jfbLayoutDesigners`                   | Forms          | `design`, so the Actions view only shows (10.2a, ADR-0188)      |
| `entryHandler`   | `jfbEntryHandlers`                     | Forms          | `viewEntries`, `editEntries`, `export`                          |
| `viewer`         | `jfbViewers`                           | Forms          | `viewEntries`                                                   |
| `privacyOfficer` | `jfbPrivacyOfficers`                   | Forms          | `privacyTools`, `viewEntries`, `viewSensitive`, `deleteEntries` |
| `noAccess`       | `jfbNoAccess`                          | Forms          | none                                                            |
| `hr`             | `jfbHrTeam`                            | Forms          | `viewEntries`, `editEntries` in the restricted folder `HR`      |
| `outsider`       | Umbraco's `writer`                     | Content        | none, without the Forms section                                 |
| `editor`         | Umbraco's `editor`                     | Content, Media | none, without the Forms section; places forms on pages (6.7h)   |

The designers' group may also pick from the whole media library, as a card's image is chosen (9.4b). The host seeds
the grants and the restricted folder `HR` at the root on every start (`support/users.ts`, ADR-0049);
`specs/smoke/access.spec.ts` reads every persona's effective permissions from `/jfb-testing/access`. Two members, in
the demo's member group, may open the members area.

## Fixtures

`fixtures.ts` gives every spec:

- `loginAs(role)`: signs in through the login screen; `apiAs(role)`: signs in through the Management API's PKCE
  flow (`support/api-login.ts`). Sign-ins and password resets run one at a time across workers, because parallel
  sign-ins hang Umbraco 17 on SQLite (`support/user-lock.ts`).
- `accountOf(role)`, `memberOf(index)`, `requestPasswordReset(role)`.
- `waitForMail(match)`: reads the host's pickup folder (`support/mail.ts` also decodes headers and bodies).
- `fillForm(values)` and `submitForm()`: answer fields by alias and send the form through the markup contract
  (`support/form.ts`, ADR-0015).

The support code has its own tests: `npm run support`.

## Projects

Edge locally (`channel: 'msedge'`), Playwright's Chromium when `CI` is set; `retries: 0`. Each project joins the
config with its first spec.

| Project      | What it checks                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| ------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `smoke`      | the site in both languages, every persona's sign-in, groups and grants, the members area, the mail pickup, the test host (status, stub, Delivery API switch) and the form helpers                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `website`    | the demo's forms as a visitor without JavaScript posts them (ADR-0100): errors with the answers kept, the pages of a form, two forms on one page, a consent whose wording changed, and the same stored values as the runtime's post, read from `/jfb-testing/entries`; the themes on every demo form; and the runtime's frame (ADR-0110): each form enhanced once with its `jfb:ready`, `init(root)`, the chunk with the logic loaded only once a form is touched; and the rules on the page (ADR-0111): the feedback form's fields shown, hidden and required as answers change, in English and German, axe clean in each state; and the element on a static page of its own on 5521, cross-origin to the host (6.6f); and refused uploads over HTTPS with HTTP/2, on a host of their own (ADR-0006); and the privacy facts sheet held to what the contact form's page requests and stores (`privacy-facts.spec.ts`, ADR-0009)                                                                                                                                                                    |
| `backoffice` | the Form Builder section as each persona sees it: the tree, the library and its dialogs, the form workspace's views and the designer (its canvas, field picker, page tabs, moves and inspector, ROADMAP 9.3, 9.4 and 22.1; `support/picker.ts` and `support/designer-row.ts` hold the picker's and the row's markup); a form's views picked first, each check of ROADMAP 22.3 (`form-views.spec.ts`; `support/form-views.ts` holds the Settings view's list and group picked, the inspector's sections and Add action's kinds); the section's left side, the five places and their tabs, as each persona sees them (`places.spec.ts`, ROADMAP 22.2; `support/places.ts` holds their markup and opens a place or a tab, from the product's own table); the designer locks run on a host of their own on 5535 whose configuration locks settings of the text type (`designer-locks.spec.ts`, ADR-0060); Deliveries on 5536 with sending paused (`deliveries.spec.ts`, ADR-0219); and the setup checklist on 5537, a fresh demo without its content or team (`setup-checklist.spec.ts`, ROADMAP 12.6) |
| `site-wide`  | what every page and screen of the one host reads, after every project that reads it: the settings sections saved from their screens, Privacy › Retention's days with what will be deleted, and Settings › Access with a folder's Permissions…, each put back after its test (ADR-0221, ADR-0222)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `startup`    | sites that must not start: an invalid host configuration and invariant globalization each stop the demo with a message that says what to change (ADR-0042). No browser: each test runs the demo in a process of its own on 5526 or 5527, without a database, and reads its log                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `budgets`    | section 4.12's interaction budgets, alone and after every other project (one worker; the gate runs it on a site of its own once every other site has ended): a form of 300 fields keeps every designer interaction and every frame under 100 ms, as Event Timing and Long Animation Frames measure them (ADR-0169)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |

## The UX walk

A phase review's walk (ROADMAP 9.6a, `specs/ux/walk.spec.ts`) shoots every screen of the backoffice at three widths, in
the light and dark themes, in English and German, with what axe and the layout measures found; the `ux-sheets`
project puts each screen's twelve shots on one sheet. Both join the config only with `JFB_E2E_UX=1`.

The walk and the journeys also fail a screen that opens a section inside a section (ROADMAP 22.3): a control with
`aria-expanded`, or a `details`, inside the region another such control opened, named with both
(`foldsInFolds` in `support/ux-walk.ts`). A menu, a list box, a combobox, a popover and a tree are not folds, nor is
what the option editor's rows and the tools that write into a box open (`FOLD_TOOLS`, ADR-0259). They
fail a screen that draws a raw key of the backoffice's words (ROADMAP 22.4), a word built as a key is, such as
`jfbGlobalSettings_privacy_retention_rejectedHelp` (`rawKeys` in `support/ux-walk.ts`).

Each shot counts two numbers of words (`measure` in `support/ux-shots.ts`, ADR-0259):

- `words`: what the section's own elements draw, below the fold included, without the forms tree's rows (a site's own
  names) and without the section's navigation. Until step 22.2 the left side was Umbraco's menu, which no walk counted,
  so leaving the navigation out compares a screen with every walk made before.
- `navigation`: what the section's navigation draws, the places on the left (`jfb-places`) and the open place's tabs
  (`jfb-place-tabs`).

Each look's report, the walk's `summary.json` and the sheets' captions carry both; the comparison with an earlier walk
reads `words`.

| Variable        | Effect                                                                                                                                              |
| --------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| `JFB_UX_OUT`    | where the shots, reports and sheets go (default `.local/ux/latest`)                                                                                 |
| `JFB_UX_ONLY`   | only the screens whose names start with one of its comma-separated prefixes, such as `settings`                                                     |
| `JFB_UX_BEFORE` | the folder of an earlier walk: the sheets then write `words.json`, each screen's words in both walks, and fail when a screen shows more than before |
| `JFB_UX_GROWN`  | the screens of that comparison that grew by a row of data and were looked at, comma-separated: the review says why                                  |

## Checks and output

- `npm run check`: the type check, the formatting and the support tests. `npm run fix` formats.
- Reports, traces and screenshots go to `.local/e2e/` (git-ignored), each of several sites' to
  `.local/e2e/site-<n>/`. Traces can hold typed values; this run's passwords stop working when the next run starts.
  `npm run report` serves the one site's report on http://127.0.0.1:5551, and
  `npx playwright show-report ../../.local/e2e/site-2/report --host 127.0.0.1 --port 5551` another site's.
