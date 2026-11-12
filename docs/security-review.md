# Security review

ROADMAP step 16.1, November 2026, on the code as it stood after Phase 15 (from `b6b36899`). This page says how the review
was made, what the audits found, what the threat model gained, and every finding with what became of it. The decisions
that changed what an earlier record said are in [ADR-0235](decisions/0235-what-an-action-needs-beyond-manage-actions.md)
and [ADR-0236](decisions/0236-what-the-security-review-changed.md); the threat model itself is
[architecture.md §10](architecture.md#10-threat-model).

## How it was made

- **By threat, on the code.** Four readings, each against the threat model and the checklist below: access control on
  every management and public endpoint; input, uploads and limits; outbound calls, mail and secrets; output encoding in
  the backoffice, the website, mail and exports. Each reading named files and lines, and each finding was confirmed on the
  code before it was fixed.
- **A test for each fix, and a tamper for each test.** Every fix has a test that fails without it. Each test was checked
  by putting the old behaviour back (a "tamper") and running it: all of them failed, which shows they hold what they
  claim. A tamper that did not compile was rewritten. A tamper a test let pass showed one of two things: a test that
  did not hold what it claimed, which was made to (X1's escaped functions), or a second guard beside the fix, which a
  tamper of both then showed the test holds (O7).
- **Severity** follows what an attacker gains and what they need: *high* reaches data or rights beyond the attacker's
  permissions without a mistake by anyone else; *medium* needs a permission or a crafted setup, or denies a service to
  others; *low* narrows a defence that others still hold; *info* is hygiene or a documentation gap.

## Audits

| Audit | Result |
| --- | --- |
| `dotnet list package --vulnerable --include-transitive` | six advisories, in MessagePack, `System.Security.Cryptography.Xml` and `.Pkcs`, ImageSharp and SQLite's native library, every one a package Umbraco or its tools bring; `scripts/check-audit-suppressions.mjs` finds each of their 19 entries across the projects under a reviewed suppression in `NuGetAudit.props` ([ADR-0024](decisions/0024-transitive-advisories.md)); none is reachable from Jiron Form Builder's code: it uses no MessagePack, signed XML, PKCS, ImageSharp or raw SQLite API itself |
| `npm audit --omit=dev` in the client, the browser tests and the scripts | none |
| `npm audit` with dev tools | two moderate advisories in dev-only packages (minimatch under ESLint, uuid under the storage emulator), fixed by overrides in `31b39edc` |
| Secret scan, term guard, name scan | clean on every commit of the step |

## The threat model

The STRIDE pass of [architecture.md §10](architecture.md#10-threat-model) was repeated on the code as it is. Its rows
gained what this review fixed: a content action held to the user's Umbraco rights, and copying sensitive answers held to
`viewSensitive`; a secret a header sends held to `manageSettings`; a trash that takes a restricted form; posts without
JavaScript that wrote files before any check; a flood of addresses that left new visitors one window; the uploads a
single IPv6 site could hold; theme tokens and custom CSS that could load from another host or reach past the form; a
forward or the deliveries log that showed sensitive answers; download tickets that outlived their user's rights; tests
without a limit; a held-back link mail that kept its code; and exceptions that reached a log as they were.

## The checklist

An ASVS-style list, by area, with where each requirement is held and where it is tested.

| Area | Requirement | Held by | Tested in |
| --- | --- | --- | --- |
| Access control | Every management endpoint declares its permission and resource; the matrix covers every endpoint | `FormBuilderPermissionAttribute`, the permission matrix | `PermissionMatrixTests` |
| Access control | What a request names belongs to what it was checked on: an entry to the route's form, a note to its entry, an export to its user, a ticket to its form | the services' lookups by both keys | `EntryPanelApiTests`, `EntryExportApiTests`, `PeopleApiTests` |
| Access control | An action does only what the user who sets it up may do | `ActionGrantChecks` (ADR-0235) | `ActionGrantApiTests` |
| Access control | A move, a trash or a restore takes nothing the user holds no right on | `FolderTakes`, ADR-0013 | `FolderApiTests` |
| Input | Every body, field and file is read against a limit before it is held | `EntryRequests`, `FormPostBuffers`, `FileUploads` | `PublicFormEntryBodyTests`, `FormPostNoRoomTests`, upload tests |
| Encoding | Every value written to HTML, a URL, JSON, XML, a header, CSV, mail or a chat is written for its context | merge tag contexts, the sanitiser, the formula guard, `WebhookChatText` | merge tag, sanitiser and export tests |
| Encoding | Nothing a site's editor writes loads from another host on a visitor's page | `ThemeTokenValue`, `ThemeCss`, the sanitiser | `ThemeTokenValueTests`, `ThemeCssTests` |
| Encoding | The backoffice writes no markup from data except through Lit's templates | ESLint rules, `confirm.test.ts` | the client's lint and tests |
| Redirects | An outcome leads where the editor chose, its template and its result alike | `RedirectUrls`, `SubmissionOutcomes` | `PublicFormEntryTests` |
| Outbound | Calls go only to allowed hosts, never to private addresses, never through a redirect | `OutboundHttp` | outbound and webhook tests |
| Outbound | A secret is a reference, and who sends one decides with `manageSettings` | `WebhookActionType`, `ActionGrantChecks` | `ActionGrantApiTests`, `WebhookActionTests` |
| Outbound | A captcha secret travels encrypted | the options validator | `FormBuilderOptionsTests` |
| CSRF | The management API reads its token from a header, never a cookie, so no other site's page can send as a user; a download link carries a one-minute ticket for one user's file; a member's post carries a challenge bound to the member and the origin | Umbraco 17's backoffice tokens, `ExportDownloadTickets`, ADR-0231 | `EntryExportApiTests`, member challenge tests |
| Sessions | A poll never renews a session nobody uses | `startIdleGate` | `idle-gate.test.ts` |
| Data | A sensitive answer reaches nobody without `viewSensitive`: not a forward, not the deliveries log, not an export | masking, `EntryForwards`, `DeliveryController`, `ExportDownloadHolders` | `EntryPanelApiTests`, `DeliveryApiTests`, `EntryExportApiTests` |
| Data | Logs hold exception types and stacks, never messages | `LoggedException` | `LoggedExceptionTests` |
| Data | A form for members is never kept by a shared cache | `Cache-Control: private` | `PublicFormChallengeTests` |
| Limits | A flood is limited per address, form and site, memory stays bounded, and new visitors keep room | `FormBuilderRateLimiter` | `FormBuilderRateLimiterTests`, `PublicApiRateLimitTests` |
| Limits | What waits for an entry is capped for the site and shared out per address and per IPv6 site | `FileUploads`, `FormBuilderRateLimiter` | upload and limiter tests |
| Limits | Tests a user runs are capped | `TestLimits` | `TestLimitApiTests` |
| Files | An upload is checked by name, content and kind, stored under a random name, served only as an attachment | ADR-0006, ADR-0143 | upload tests |

## Findings

| ID | Severity | What was found | Status |
| --- | --- | --- | --- |
| A1 | High | A content action saved and published Umbraco content as the site, held only to `manageActions`, so a form designer could write where they may not | Fixed in `2f9d1509` (ADR-0235) |
| A2 | High | Naming a sensitive field for an action to copy needed only `manageActions`, so a user could send themselves answers they may not read | Fixed in `2f9d1509` (ADR-0235) |
| A3 | Medium | A folder's trash and restore took restricted forms below it that the user held nothing on | Fixed in `f1c4a17b` |
| D1 | Medium | A post without JavaScript wrote its files to temporary files before any check, on any path | Fixed in `f1c4a17b` |
| D2 | Medium | A flood of addresses filled the limiter's bound and left every new visitor one shared window for up to an hour | Fixed in `e1529ac4` |
| D3 | Medium | One IPv6 site's /64 networks could fill what may wait for an entry for a day | Fixed in `e1529ac4` |
| O1 | Medium | Any user who manages actions could send any of the site's secrets to an allowed host, and test it from an unsaved draft | Fixed in `cf1b813e` (ADR-0235) |
| X1 | Medium | A theme token's value could load an image from another host (`image-set()`, an escaped `url(`) | Fixed in `a7fa701a` |
| A4 | Low | A forward sent sensitive answers to a user who may not see them | Fixed in `06ae431d` |
| A5 | Low | Polls renewed the session of a user without the Inbox | Fixed in `f1c4a17b` |
| A6 | Low | The deliveries log showed an address a visitor typed, which may be a sensitive answer, to anyone who manages settings | Fixed in `40091886` |
| A7 | Low | A form for members answered `no-cache` without `private` | Fixed in `8755ca2e` |
| A8 | Low | A download ticket counted for its minute after its user lost the right | Fixed in `09b7a40a` |
| D4 | Low | Rows past a group's most were read for files and signatures | Fixed in `f1c4a17b` |
| D5 | Low | A refusal named every unknown field of a body | Fixed in `f1c4a17b` |
| D6 | Low | A marked post under the public API's path was read as a form post | Fixed in `f1c4a17b` |
| D7 | Low | The orphan cleanup stopped at files it could not remove | Fixed in `f1c4a17b` |
| D8 | Low | A form whose alias Windows keeps for a device could not be mirrored | Fixed in `f1c4a17b` |
| O2 | Low | Gmail's dotted spellings of one mailbox each counted as their own toward the visitor limit | Fixed in `255407ec` |
| O3 | Low | Ten log calls passed an exception whose message may quote an answer | Fixed in `340c1e7e` |
| O4 | Low | Records holding answers, addresses or tokens printed them in their generated `ToString` | Fixed in `340c1e7e` |
| O5 | Low | A secret typed as itself was kept in drafts and their History | Fixed in `cf1b813e` |
| O6 | Low | A link mail held back by the limits kept its code | Fixed in `340c1e7e` |
| X2 | Low | Custom CSS could style what stands beside the form | Fixed in `a7fa701a` |
| X3 | Low | An escape in custom CSS could spell an address | Fixed in `a7fa701a` |
| X4 | Low | The outcome preview's blocklist missed SVG's animations | Fixed in `f1c4a17b` |
| A9 | Info | A person's export tickets shared their purpose with a form's | Fixed in `09b7a40a` |
| A11 | Info | The AI add-on's endpoints were outside the permission matrix's tests | Fixed in `340c1e7e` |
| A13 | Info | Test mails and action tests had no limit | Fixed in `340c1e7e` |
| A14 | Info | No test held an entry of another form to `404` on every entry endpoint | Fixed in `340c1e7e` |
| D10 | Info | The blob sample would store into an existing container that anyone may read | Fixed in `340c1e7e` |
| O7 | Info | A redirect's template was checked at publishing only, not when it ran | Not reachable (the renderer writes a tag before the path as it stands, so the address fails the rule); checked when it runs anyway in `340c1e7e` |
| O8 | Info | A captcha stub's verify address could be plain http to another machine | Fixed in `5f717de0` |
| X5 | Info | The lint guards missed the AI add-on's package and some HTML parsers | Fixed in `f1c4a17b` |
| X8 | Info | The confirmation link was not HTML-encoded | Not reachable (the base path is held to URL-safe characters, the token is URL-encoded); encoded anyway in `26ad24e0` |
| A10 | Info | `privacyTools` sees every form's entries | Accepted: a privacy officer answers a person's request across every form, which the role exists for (ADR-0223); it is a grant of its own, and every search and export is audited |
| D9 | Info | Behind a proxy, every visitor shares the proxy's address in the limits | Documented: the privacy checklist and facts sheet tell sites to set up ASP.NET Core's forwarded headers; Jiron Form Builder never reads `X-Forwarded-For` itself |
| X6 | Info | Whether real mail clients keep a visitor's address unlinked is not checked here | Owner: [OWNER-TODO](OWNER-TODO.md) (real mail clients) |
| X7 | Info | Slack and Teams make links of bare addresses in answers | Documented in [webhooks.md](webhooks.md); their own link and mention syntax in answers is already written as text |
| O9 | Info | Umbraco's own webhooks go where Umbraco sends them, outside the allowed hosts | Documented in [webhooks.md](webhooks.md): who may manage Umbraco's webhooks decides; the payload holds no answer |

No high or critical finding is open.
