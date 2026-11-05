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

- [ ] **Make the repository public** when ready. The Umbraco Marketplace reads
  `umbraco-marketplace.json` from the repository, so the listing needs this. Before that, run
  `node scripts/term-guard.mjs --history` in your working copy (which has `.private/`).
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

## Publishing

- [ ] **Create the nuget.org trusted-publishing policy** for this repository and the release workflow,
  then set the repository variable `JFB_NUGET_PUBLISH` to `true` (ROADMAP D17, §3.5).
- [ ] **Publish to NuGet and list on the Umbraco Marketplace.** Run the validator at
  marketplace.umbraco.com/validate after the repository is public. Nothing is published during the build.

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

- [ ] **The website runtime's budget, once more: 60 KB or about 62 KB? (ROADMAP §4.12,
  [ADR-0163](decisions/0163-save-and-resume.md)).** It blocks step 8.6b. The runtime with every feature stands at 59.80 KB
  gzip. "Save and continue later" and bringing a saved form back come to 1.94 KB as a chunk only a form offering it loads,
  which makes 61.95 KB. No visitor loads all of that: a form loads only the features it uses, and what every page with a form
  loads stays 2.3 KB. The bytes have been through a terser pass, and nothing obvious is left to take out. The server side of save
  and resume is finished and works for headless sites either way. **My recommendation: raise it to 64 KB,** which also leaves room for
  the insights beacons of step 11.5, a small addition. Keeping 60 KB means forms have no "Save and continue later" button
  of their own. Once you set a number, the step resumes from the branch `wip/8.6b-resume-control`.

## Real-world checks (no real keys are used during the build)

- [ ] **Captcha providers:** test reCAPTCHA v2/v3 and hCaptcha with real keys, set as environment
  variables (`JironFormBuilder__Captcha__…`).
- [ ] **Email:** test with a real SMTP server and a few real mail clients (Outlook, Gmail, Apple Mail,
  dark mode).
- [ ] **The AI add-on:** try it with a real Umbraco.AI provider and profile.
- [ ] **Blob storage:** try the file-store sample against a real storage account if you use one.
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
  - Umbraco's own page is at least 920 px wide in every section, so a phone scrolls sideways in all of them.
- [ ] **Screen readers:** test a few forms and the designer with NVDA and VoiceOver. The build checks
  accessibility names and keyboard paths automatically, but no real screen reader runs here.
- [ ] **GitHub rendering:** once the repository is public, check that the README, docs and issue forms
  render as intended on GitHub.
- [ ] **The first release run:** pushing the `v17.0.0-alpha.1` tag (ROADMAP 18.4) starts the release
  workflow once, even before CI is enabled; check its draft release.
