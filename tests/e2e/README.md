# Browser tests

Playwright Test against a fresh demo host for every run ([ADR-0011](../../docs/decisions/0011-testing-strategy.md),
[ADR-0031](../../docs/decisions/0031-browser-harness.md)).

```
cd tests/e2e
npm ci
npx playwright test --project=smoke
```

## The host

`global-setup.ts` builds the client assets (when stale) and the demo, then starts the demo's DLL on
`http://127.0.0.1:5520` with its own content root in `.local/e2e/host-5520/`: its SQLite database, the mail pickup
folder `mail/` and the log `host.log`. The teardown stops it with its whole process tree.

- **The Testing project** is on in this host only: the test-only endpoints under `/jfb-testing/`, the test clock, the
  stub on `http://127.0.0.1:5560`, and the start-up marker (ADR-0029, ADR-0031).
- **The start-up wait:** Umbraco 17 registers its jobs and rebuilds its indexes about a minute after it starts, and
  the tests start only after that (`support/host-health.ts`). The wait ends on a log line that appears whether the
  Content Delivery API is on or off. If the work does not finish, the set-up fails with the reason and the end of
  the host's log.
- **The hang marker:** when the host's database hung, the run leaves `.local/e2e/host-5520/database-hung.txt`, and
  the gate runs the browser projects once more. That is the only rerun ("Flaky means broken").
- **The static page** on `http://127.0.0.1:5521` (`support/static-site.ts`, ADR-0031): a site of its own whose page
  loads `<jfb-form>` from the host; the host's public API allows its origin, and `http://localhost:5521` is the same
  page on an origin the API does not allow. The headless spec starts it in its own process.
- **The settings come from the harness only:** variables in your shell that would configure Umbraco or the demo are
  not passed on.

| Variable                  | Effect                                                         |
| ------------------------- | -------------------------------------------------------------- |
| `JFB_E2E_DELIVERY_API=on` | runs the host with the Content Delivery API on (default `off`) |
| `JFB_E2E_SKIP_BUILD=1`    | uses the demo and the client assets as they were last built    |
| `JFB_E2E_CONFIGURATION`   | the demo's build configuration (default `Debug`)               |

## The people

Every run generates new passwords into `.local/credentials.json` under `e2e`; they are never printed.

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

| Project      | What it checks                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| ------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `smoke`      | the site in both languages, every persona's sign-in, groups and grants, the members area, the mail pickup, the test host (status, stub, Delivery API switch) and the form helpers                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `website`    | the demo's forms as a visitor without JavaScript posts them (ADR-0100): errors with the answers kept, the pages of a form, two forms on one page, a consent whose wording changed, and the same stored values as the runtime's post, read from `/jfb-testing/entries`; the themes on every demo form; and the runtime's frame (ADR-0110): each form enhanced once with its `jfb:ready`, `init(root)`, the chunk with the logic loaded only once a form is touched; and the rules on the page (ADR-0111): the feedback form's fields shown, hidden and required as answers change, in English and German, axe clean in each state; and the element on a static page of its own on 5521, cross-origin to the host (6.6f); and refused uploads over HTTPS with HTTP/2, on a host of their own (ADR-0006) |
| `backoffice` | the Form Builder section as each persona sees it: the tree, the library and its dialogs, the form workspace's views and the designer (its canvas, palette, moves and inspector, ROADMAP 9.3 and 9.4); the designer locks run on a host of their own on 5535 whose configuration locks settings of the text type (`designer-locks.spec.ts`, ADR-0060); Deliveries on 5536 with sending paused (`deliveries.spec.ts`, ADR-0219); and the setup checklist on 5537, a fresh demo without its content or team (`setup-checklist.spec.ts`, ROADMAP 12.6)                                                                                                                                                                                                                                                    |
| `site-wide`  | what every page and screen of the one host reads, after every project that reads it: the settings sections saved from their screens, Privacy › Retention's days with what will be deleted, and Settings › Access with a folder's Permissions…, each put back after its test (ADR-0221, ADR-0222)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `startup`    | sites that must not start: an invalid host configuration and invariant globalization each stop the demo with a message that says what to change (ADR-0042). No browser: each test runs the demo in a process of its own on 5526 or 5527, without a database, and reads its log                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `budgets`    | section 4.12's interaction budgets, alone and after every other project (one worker): a form of 300 fields keeps every designer interaction and every frame under 100 ms, as Event Timing and Long Animation Frames measure them (ADR-0169)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |

## The UX walk

A phase review's walk (ROADMAP 9.6a, `specs/ux/walk.spec.ts`) shoots every screen of the backoffice at three widths, in
the light and dark themes, in English and German, with what axe and the layout measures found; the `ux-sheets`
project puts each screen's twelve shots on one sheet. Both join the config only with `JFB_E2E_UX=1`.

| Variable      | Effect                                                                                          |
| ------------- | ----------------------------------------------------------------------------------------------- |
| `JFB_UX_OUT`  | where the shots, reports and sheets go (default `.local/ux/latest`)                             |
| `JFB_UX_ONLY` | only the screens whose names start with one of its comma-separated prefixes, such as `settings` |

## Checks and output

- `npm run check`: the type check, the formatting and the support tests. `npm run fix` formats.
- Reports, traces and screenshots go to `.local/e2e/` (git-ignored). Traces can hold typed values; this run's
  passwords stop working when the next run starts. `npm run report` serves the report on
  http://127.0.0.1:5551.
