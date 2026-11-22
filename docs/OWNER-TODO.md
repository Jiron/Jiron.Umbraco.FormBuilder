# Owner to-do

Things only the owner can do (CLAUDE.md rule 8). The build continues without them; each item says
what to do and why. Items for blocked roadmap steps name the step as "step X.Y", which
`scripts/roadmap-status.mjs` checks.

## Before the first run

- [x] **The license is committed (2026-10-20).** Your root commit `781401e` holds `LICENSE.md`, the PolyForm
  Perimeter License 1.0.1 with Jiron Form Builder's header and `Required Notice:` line (ROADMAP D2, §3.6).
  ROADMAP 0.1 checks its SHA-256.

## Licensing and naming

- [x] **The name is decided (2026-10-20): Jiron Form Builder (`Jiron.Umbraco.FormBuilder`).** A
  descriptive name under your own name, chosen after name research (ROADMAP D1, §3.6). The GitHub
  repository is already renamed.
- [ ] **Reserve the NuGet prefix `Jiron.`.** It stops anyone else from publishing `Jiron.*` packages,
  and yours get the verified checkmark.
  1. Create a nuget.org organization "Jiron", with your account as admin.
  2. Email account@nuget.org with the owner name, the prefix `Jiron.*` (private), and links that show it
     is you (github.com/Jiron, jiron.dev).

  No published package is needed to apply.
- [ ] **Publish the docs** under docs.jiron.dev/form-builder when you want a docs site; until then the
  Marketplace points to the repository's `docs/` (ROADMAP 18.1).

- [ ] **Have a lawyer review the licence choice and texts** before the repository becomes public:
  - **`LICENSE.md`:** Perimeter's "competes" clause against your intent. Agencies building and hosting
    client sites, paid add-ons and starter kits should be fine; selling Jiron Form Builder, publishing
    your own version of it (free or paid, open source or not) or offering any other competing product
    or service should not. Contributions through forks and pull requests should stay possible.
  - **The plain-language summaries:** the one in `README.md` and `docs/license-faq.md` (written in
    ROADMAP 17.2a).
  - **An optional, separately drafted "additional permission"** that states what is allowed (client
    builds, hosting, maintenance, add-ons, starter kits). If you want a community fork to be allowed
    when the project stops being maintained, it should cover that too (Perimeter has no such clause).
    Never edit the PolyForm text itself.
- [ ] **Set up a contributor licence agreement before accepting outside contributions** (ROADMAP §3.6).
  - Under GitHub's inbound = outbound rule, contributions would arrive under Perimeter, which would stop
    you from relicensing them later.
  - Use a CLA with a perpetual, worldwide, royalty-free, **sublicensable** copyright and patent licence
    grant, modelled on the Apache ICLA, collected with CLA Assistant.
  - Under German law, draft it as a licence grant listing every type of use (§§ 29, 31(5), 31a UrhG),
    not an assignment.
  - Add an original-work attestation ("I wrote this myself and did not copy, decompile or derive it
    from proprietary software").
  - **Then open the commit identity check** (added 2026-10-22, ROADMAP 0.2): during the build the
    `pre-commit` hook accepts only your noreply identity (`scripts/lib/identity.mjs`). Before outside
    pull requests are accepted, change it so contributors can commit under their own names, for example
    by keeping the check only for commits to `main` made in your own clone. `CONTRIBUTING.md` says this.
- [ ] **Optionally put the full code of conduct text in the repository** (added 2026-10-22, ROADMAP 0.2).
  `CODE_OF_CONDUCT.md` adopts the Contributor Covenant 2.1 by reference: a link to the official text, a
  short summary in the project's own words, the contact address and the required attribution. If you
  prefer the whole text in the file, replace the summary with the official Markdown (CC BY 4.0).
- [ ] **Optionally ask Umbraco HQ** (accounts@umbraco.com) whether they're fine with "Umbraco" as a
  platform qualifier in the package id `Jiron.Umbraco.FormBuilder`. Their brand rules forbid "Umbraco" in
  product names; the display name "Jiron Form Builder" doesn't use it.
- [ ] **Never call Jiron Form Builder "open source".** Its licence forbids competing products, so the right term
  is "free and source-available" (ROADMAP §3.6).
- [ ] **Check the Cyber Resilience Act before monetising anything** (paid add-ons, paid support,
  possibly donations). Non-monetised software is generally out of scope (ROADMAP §3.6).
- [ ] **Decide later whether to offer an importer** from other form systems as a separate package.
- [ ] **Decide whether paid add-ons will exist** before the first stable release. It affects the CLA, the
  CRA check above and how the add-on packages are shaped. The open-core suggestions written for Tandem
  (`docs/research/open-core-plan.md` in that repository) apply here too. The build adds no paid features.

## Repository, CI and dependencies

- [ ] **Make the repository public** when ready, after Phase 22 of the roadmap (a simpler backoffice), as you decided
  on 2026-11-16 (ADR-0254). The Umbraco Marketplace reads the listings
  `umbraco-marketplace.json` (the product) and `umbraco-marketplace-jiron.umbraco.formbuilder.ai.json` (the AI
  add-on, shown with the product) from the repository's root, and their screenshots from GitHub's raw addresses, so
  the listings need this (ROADMAP 18.1, ADR-0246). Before that, run `node scripts/term-guard.mjs --history` with your
  term list in `.private/terms.txt`, which the guard skips where it is missing.
- [ ] **Repository settings:**
  - enable private vulnerability reporting (`SECURITY.md` points to it);
  - enable Discussions (the issue template links questions there);
  - protect `main`.
- [ ] **Set `JFB_CI`, raise the Dependabot limit, and check the first CI run (including the SQL Server and
  load-balancing jobs)** (ROADMAP 3.5, ADR-0032).
  1. Set the repository variable `JFB_CI` to `true`, so pushes to `main` and `v18/main` run the jobs of
     `.github/workflows/ci.yml`. Until then only a run started by hand or a `v*` tag runs them.
  2. Raise `open-pull-requests-limit` in `.github/dependabot.yml` for each ecosystem (nuget, npm,
     github-actions). It is 0 because nobody reviews pull requests during the build.
  3. Check the first run. It is the first time that the repository scripts, the browser tests and the SQL
     Server integration tests run on Linux, and the SQL Server and load-balancing jobs never run on this
     machine. Until step 8.1 fills it in, the load-balancing job only starts SQL Server, creates the
     shared database and ends with a notice that names 8.1. From 4.3a on, the repository tests also run
     SQL written only for SQL Server (its paging, the lock hints and the recursive folder walks of
     ADR-0036), which has never run against a server. The PHP sample that verifies a webhook's signature
     (`docs/samples/webhook-signature/verify.php`, ROADMAP 8.4, ADR-0161) also runs for the first time here:
     this machine has no PHP, so `ThePhpSample` is skipped on it, and on a CI runner without PHP it fails.
  4. Read how long the integration jobs took (ROADMAP 24.1, ADR-0261). They run in shares, one job each: six
     for SQLite and ten for SQL Server, each planned at about 20 minutes of its 45, and that plan is an
     estimate made on this machine. If the longest job takes more than 22 minutes, raise the number of
     shares: the matrix's numbers and `JFB_SHARES` together, in `.github/workflows/ci.yml`. The job
     "Integration tests add up" must pass: it fails when a share did not report or a test never ran.

## Publishing

- [ ] **Create the nuget.org trusted-publishing policy** for this repository, the workflow `release.yml` and the
  environment `nuget`, then set the repository variables `JFB_NUGET_USER` (the nuget.org profile name) and
  `JFB_NUGET_PUBLISH` (`true`) (ROADMAP D17, §3.5, ADR-0247). [Releasing](releasing.md#turning-on-publishing-owner)
  walks through it.
- [ ] **Publish to NuGet and list on the Umbraco Marketplace.** Run the validator at
  marketplace.umbraco.com/validate after the repository is public, for `Jiron.Umbraco.FormBuilder` and for
  `Jiron.Umbraco.FormBuilder.AI`; check that the add-on shows its own listing as a sub-package of the product, not the
  product's (ADR-0246). The build checks both listings against the Marketplace's schema
  (`node scripts/check-marketplace.mjs`), but only the Marketplace can read them from the repository. Nothing is
  published during the build.

## Budgets

- [x] **Decided 2026-11-01: the website runtime's budget "with everything" grows from 40 KB to 60 KB (ROADMAP §4.12,
  [ADR-0146](decisions/0146-the-runtimes-budget-grows-to-60-kb.md)).** The steps below resume at step 7.4i. What was asked:
  It blocks step 6.4i, step 6.4j, step 6.4l, step 6.4m, step 6.4n, step 6.4o, step 6.4p, step 6.5d,
  step 6.6g, step 7.1e and step 7.2c ([ADR-0120](decisions/0120-sending-waits-for-the-runtimes-budget.md); 6.6g, the headless
  element's pages and sending, waits for 6.5d and 6.4i, [ADR-0128](decisions/0128-the-element-writes-the-servers-view-model.md)). After 6.4h the runtime with
  every feature it has is 39.89 KB gzip (counting the largest language's words, ADR-0116, after a
  terser pass, ADR-0119): the frame 1 KB, the form chunk 28 KB (the rules, checks and messages both
  runtimes share, D10), the calculation language 2.5 KB, the merge tags with the defaults' filler
  7 KB. Sending (6.4i), built on the branch `wip/6.4i-sending`, takes it to 41.38 KB. The plan's other
  runtime features, estimated: `form_changed` 0.8 KB, fitting the page and the developer surface 1.5,
  uploads 3, the signature pad 3, the other field behaviours 3, paging in the browser (6.5) 1.5, the
  challenge's use (7.1) 1, proof of work and the captchas (7.2) 2.5: about 58 KB with everything. The
  core, what every page with a form loads before the visitor touches it, stays 1 KB of its 16 KB, and a
  page loads only the features its form uses. Once you set a number, the blocked steps resume from the
  branch.

- [x] **Decided 2026-11-14: the website runtime's budget "with everything" grows from 60 KB to 100 KB at most, for
  now (ROADMAP §4.12, [ADR-0251](decisions/0251-the-runtimes-budget-grows-to-100-kb.md)).** Step 8.6b resumes after
  step 19.1. What was asked ([ADR-0163](decisions/0163-save-and-resume.md)): It blocks step 8.6b. The runtime with every feature stands at 59.50
  KB gzip. "Save and continue later" and bringing a saved form back come to about 2 KB as a chunk only a form offering
  it loads, which makes 61.71 KB. No visitor loads all of that: a form loads only the features it uses, and what every page
  with a form loads stays 2.3 KB. The bytes have been through a terser pass, and nothing obvious is left to take out.
  The server side of save and resume is finished and works for headless sites either way. **My recommendation: raise it
  to 64 KB,** which also leaves room for the insights beacons of step 11.5, a small addition. Keeping 60 KB means forms
  have no "Save and continue later" button of their own. Once you set a number, the step resumes from the branch
  `wip/8.6b-resume-control`. Retried at the Phase 9 review (2026-11-05): with Phase 9 merged in, 61.96 KB; at the Phase
  10 review (2026-11-07): with Phase 10 merged in, 61.95 KB; at the Phase 11 review (2026-11-08), the Phase 12 review
  and the Phase 13 review (both 2026-11-09): with each merged in, 62.15 KB; at the Phase 14 review (2026-11-11): with
  Phase 14 merged in, 61.53 KB, the runtime's words now in seven languages; at the Phase 15 review (2026-11-11): with
  Phase 15 merged in, 61.62 KB; at the Phase 16 review (2026-11-12) and the Phase 17 review (2026-11-13): with each
  merged in, 61.67 KB; at the Phase 18 review (2026-11-13): 61.69 KB; at the Phase 19 review (2026-11-13): with
  Phase 19 merged in, 61.71 KB.

## Legal judgements (privacy)

The privacy review of Phase 13 (2026-11-09, ADR-0226) left these questions, which only a lawyer can settle. The build
keeps its defaults meanwhile, and `docs/privacy/` describes them; a site can change most of them in its settings.

- [ ] **Default retention.** Is 365 days, counted from an entry's last change of state, a defensible default for contact
  and booking entries whatever their purpose (rejected 90, Spam 30 days)?
- [ ] **Proof of double opt-in.** How long must the proof of a confirmed subscription be kept, and must it include the
  confirmation mail's content? The entry's timeline keeps when it was confirmed for as long as the entry is kept; the
  mail itself goes 30 days after it was sent.
- [ ] **Shortened IP addresses.** Is an IPv4 address shortened to /24, or an IPv6 address to /48, still personal data
  under the GDPR and the revDSG? Is "anonymised" a fair name for it in the settings?
- [ ] **The confirmation mail as advertising.** Is a neutral confirmation mail (fixed text, no advertising) safe from
  being treated as advertising itself under UWG § 7? At least one appellate court has held that it can be.
- [ ] **The insights beacon.** Does the cookieless "view" counter need more than a legitimate interest? It fires as the
  page loads, reads and writes nothing on the device, and carries no identifier.
- [ ] **One consent for two things.** Can one consent field validly agree both to a form's purpose and to loading a
  captcha (TDDDG § 25)? The checklist advises a field of its own for the captcha.
- [ ] **Spam sorting.** Does sorting an entry into Spam automatically (it then runs no actions and goes after 30 days)
  count as an automated individual decision that must be disclosed (GDPR Art. 22, revDSG Art. 21)?

## Real-world checks (no real keys are used during the build)

- [ ] **Captcha providers:** test reCAPTCHA v2/v3 and hCaptcha with real keys, set as environment
  variables (`JironFormBuilder__Captcha__…`).
- [ ] **Email:** test with a real SMTP server and a few real mail clients (Outlook, Gmail, Apple Mail,
  dark mode). Check too (added 2026-11-12, ROADMAP 16.1) that an address a visitor typed into an answer stays plain
  text in a mail to the visitor (the mail breaks it with an invisible character so no client makes a link of it), and
  that the confirmation and resume links still open.
- [ ] **The AI add-on:** try it with a real Umbraco.AI provider and profile (ROADMAP 15.3, docs/ai.md): add the
  provider's package to a site with the add-on, create a connection and a chat profile in Umbraco.AI, choose it in
  Form Builder's Settings › AI, check that the health check "AI add-on" passes, and try Draft (a description and a
  pasted questionnaire), Translate (with a preview edited and applied) and Suggest. The build only ever talks to a fake
  provider.
- [ ] **Blob storage:** try the file-store sample against a real storage account if you use one.
- [ ] **Report to Umbraco HQ: a stall on SQLite** (added 2026-11-15, ROADMAP 21.1,
  [ADR-0253](decisions/0253-one-servers-test-host-reads-cache-instructions-once-a-day.md)): on Umbraco 17.7.1 with
  SQLite and the connection string Umbraco 17 writes (`Cache=Shared`), `DatabaseServerMessenger.Sync` reads
  `umbracoCacheInstruction` and then updates `umbracoLastSynced` in the same transaction, and
  `CacheInstructionsPruningJob` reads its highest id and then deletes from it, while a request that ends with
  `BatchedDatabaseServerMessenger.SendMessages` inserts into `umbracoCacheInstruction`. The insert holds the write
  transaction and waits for the table; the job holds its read lock and waits for the write transaction; the
  retry policy runs both statements again inside their transactions, and SQLite's shared cache refuses every other
  transaction meanwhile ("SQLite Error 6: database table is locked", "Cache instruction sync did not complete within
  00:01:00"). Four browsers saving forms at once met it in most 20-minute runs. The record holds what a memory dump
  of the stalled site showed, and on the development machine `.local/pass-21.1/stall-1-sqlite-state.txt` and
  `stall-2-sqlite-state.txt` hold SQLite's state as read from both dumps. Umbraco 18 no longer uses the shared cache,
  so this is for the 17 line.
- [ ] **Report to Umbraco HQ: a write that waits for good on Umbraco 18 with SQLite** (added 2026-11-22, ROADMAP 24.1,
  [ADR-0261](decisions/0261-where-the-test-time-goes.md)): on the 18 line (Umbraco 18.3, SQLite in write-ahead mode
  without the shared cache), a test site hung at start-up when Umbraco's distributed jobs ran for the first time, five
  seconds after they registered, while the site was still creating its test users through `IUserService.CreateAsync`.
  A memory dump of a hung site shows one thread only at work: `UserRepository.PersistNewItem` under
  `FaultHandlingDbCommand`'s retry policy, in Microsoft.Data.Sqlite's wait for a busy database, and the jobs' service
  idle. My reading, not proven: the user creation read first and wrote later in one deferred transaction, the jobs'
  service committed in between, and SQLite then refuses that transaction's write however often it is tried, so only
  starting the transaction again would help. The test sites now start the jobs after their seeding, so this is for
  Umbraco's own sake. Before reporting, repeat it on a plain Umbraco 18 site without the package: the dump is not
  kept, and `.local/gate/` holds the hosts' logs of the failed runs only until the next gate.
- [ ] **Report to Umbraco HQ** (added 2026-10-31, ROADMAP 6.7f): in Umbraco 17.7.1's dark theme UUI's positive
  primary button (`#347d39` under `#eeeeef`) is 4.37:1, below WCAG AA's 4.5:1, on every dialog's main button. Jiron
  Form Builder keeps Umbraco's buttons as Umbraco draws them; its own parts use tokens that pass (docs/ux/tokens.md).
  UUI's danger (2.5:1 on the surface) and selected (4.3:1) colours fall short there too. Since 9.6a (ADR-0182) the
  section sets white labels and a stronger focus ring on its own elements, Umbraco's footer and dialogs inside them
  included; what stays Umbraco's, for the same report:
  - the dark theme's default and danger labels (4.34:1, 4.32:1), its warning label (`#eeeeef` on `#af7c12`, 3.2:1),
    its focus colour (`#316dca`, 2.5:1 on the surface) and its `-standalone` danger and positive (2.5:1); the
    high-contrast theme's warning standalone (`#ffdd43`, 1.3:1 on white);
  - the open row of every tree (`--uui-color-current`, `#316dca` under `#eeeeef`, 4.34:1 in the dark theme);
  - `uui-menu-item`'s inner `#menu-item` carries `aria-label="menuitem"`, so its accessible name never matches its
    label (axe's `label-content-name-mismatch` on every tree row);
  - a `uui-checkbox` that is `indeterminate` writes `aria-checked="false"` on its native input, which the browser
    reports as mixed, so axe finds `aria-conditional-attr` on the "select all" of every table that ticks rows, Umbraco's
    own collections and the entries list alike (11.6);
  - Umbraco's own page is at least 920 px wide in every section, so a phone scrolls sideways in all of them.
- [ ] **Screen readers:** test a few forms and the designer with NVDA and VoiceOver. The build checks
  accessibility names and keyboard paths automatically, but no real screen reader runs here.
- [ ] **GitHub rendering:** once the repository is public, check that the README, docs and issue forms
  render as intended on GitHub, and once the first prerelease is on nuget.org, that `docs/README.nuget.md`
  renders there with working links. The build checks both only through a local renderer
  (`node scripts/render-markdown.mjs`).
- [ ] **The first release run: check the draft release.** The tag `v17.0.0-alpha.1` was pushed on 2026-11-13
  (ROADMAP 18.4), which starts the release workflow once, even before `JFB_CI` is set (if Actions are enabled for the
  repository at all). In GitHub, open **Actions › Release**: the run checks the version and its changelog section,
  runs CI, packs the four packages and drafts a release. Then open **Releases**: the draft should hold seven
  files, the four `.nupkg` and the three `.snupkg` (the meta package has no code, so no symbols), with the notes of
  `CHANGELOG.md`'s section. Publish the draft only when the version should be visible; nothing reaches nuget.org until
  `JFB_NUGET_PUBLISH` is set ([releasing.md](releasing.md)). Nobody could watch the run from the development machine.
  **The tag `v17.0.0-beta.1` was pushed on 2026-11-22** (the owner: "Yes, cut beta.1") and starts the same run: check
  it the same way. Its install tests passed on the development machine on exactly that version, on Umbraco 17.7.1
  and on the floor, 17.6.0. If the variables of publishing are set before a tag is pushed, that tag's run publishes;
  a tag pushed before them stays unpublished, so `17.0.0-beta.1` reaches nuget.org only by a run started again
  after they are set, or by hand.
