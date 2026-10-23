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
     shared database and ends with a notice that names 8.1.

## Publishing

- [ ] **Create the nuget.org trusted-publishing policy** for this repository and the release workflow,
  then set the repository variable `JFB_NUGET_PUBLISH` to `true` (ROADMAP D17, §3.5).
- [ ] **Publish to NuGet and list on the Umbraco Marketplace.** Run the validator at
  marketplace.umbraco.com/validate after the repository is public. Nothing is published during the build.

## Real-world checks (no real keys are used during the build)

- [ ] **Captcha providers:** test reCAPTCHA v2/v3 and hCaptcha with real keys, set as environment
  variables (`JironFormBuilder__Captcha__…`).
- [ ] **Email:** test with a real SMTP server and a few real mail clients (Outlook, Gmail, Apple Mail,
  dark mode).
- [ ] **The AI add-on:** try it with a real Umbraco.AI provider and profile.
- [ ] **Blob storage:** try the file-store sample against a real storage account if you use one.
- [ ] **Screen readers:** test a few forms and the designer with NVDA and VoiceOver. The build checks
  accessibility names and keyboard paths automatically, but no real screen reader runs here.
- [ ] **GitHub rendering:** once the repository is public, check that the README, docs and issue forms
  render as intended on GitHub.
- [ ] **The first release run:** pushing the `v17.0.0-alpha.1` tag (ROADMAP 18.4) starts the release
  workflow once, even before CI is enabled; check its draft release.
