# Jiron.Umbraco.FormBuilder: rules for Claude

Jiron Form Builder is a form builder for Umbraco 17. Editors design forms in a **Form Builder** section of the backoffice (pages, groups, fields, rules, actions), visitors fill them in on the website (Razor or headless), and teams work with the entries (review, approve, export, privacy tools). It is free to use, including commercially, under the PolyForm Perimeter License 1.0.1 ([LICENSE.md](LICENSE.md)). The plan is [ROADMAP.md](ROADMAP.md). Work through it in order and keep its checkboxes current.

## Binding rules

1. **The license comes first, and you never change it.**
   - The very first commit of this repository, `781401e` (made by the owner on 2026-10-20), contains `LICENSE.md` exactly as the owner placed it. ROADMAP step 0.1 checks its SHA-256. Never edit, reformat, re-encode or replace it.
   - The one force-push the owner had approved is used up. Never force-push, rebase published history or remove the license.
2. **Jiron Form Builder is an original work (clean room).**
   - Write every line of code, markup, CSS, text, icon and schema yourself. The only exception is a dependency or snippet whose license allows it (rule 4), listed in `THIRD-PARTY-NOTICES.md`.
   - Never decompile, disassemble, unpack or read the binaries, scripts, styles, views, templates, database schemas or data of any commercial or closed-source product, and never install, run or start a trial of one.
   - Feature ideas may come only from public descriptions such as documentation, release notes and product pages. Write them down again in your own words and design them your own way: your own names, structure, syntax (for example the `{{…}}` merge tags), database layout and screens. Never copy documentation text; rewording it sentence by sentence still counts as copying.
   - Never use screenshots of other products as design references, and never mirror another product's public API names, type hierarchy, configuration keys, file and folder conventions or database names. Jiron Form Builder's designs come from its own Phase 2 work (ROADMAP §3.6 and Phase 2).
   - **Jiron Form Builder stands on its own.** Tracked files, code, comments, commit messages, tags, release notes, issue and pull request texts, and package metadata never name, compare to or position against any other form product or its vendor. "Umbraco" may be named as the platform.
   - `scripts/term-guard.mjs`, run by the pre-commit and commit-msg hooks, blocks the owner's private term list.
   - `.private/` is git-ignored owner material. Read it when the goal tells you to. Never stage it, and never copy its contents, terms or conclusions word for word into tracked files, commits or messages.
3. **No secrets in git, ever.** That means no API keys, passwords, connection strings with credentials, tokens or private URLs.
   - From step 0.2 on, run `node scripts/secret-scan.mjs` before every commit (the pre-commit hook does it too).
   - Generated local credentials live only in the git-ignored `.local/` folder. Scripts and test setups never print passwords.
   - Never run `git credential fill`, and never pull tokens out of credential stores, the environment or config to call GitHub or any other API.
   - **This build uses no real third-party keys.** Captcha providers and AI features are tested only against local stubs and fakes. Owner-supplied keys for real checks are an OWNER-TODO item.
4. **Official building blocks only; licenses checked.**
   - **Umbraco:** use Umbraco HQ's packages: `Umbraco.Cms.*`, plus `Umbraco.AI.*` for the optional AI add-on only. Use no Umbraco community packages as dependencies.
   - **What ships to sites** (runtime dependencies, direct and transitive): MIT, Apache-2.0, BSD-2/3-Clause, ISC or 0BSD only, each listed in `THIRD-PARTY-NOTICES.md`. Prefer writing a small piece yourself over adding a dependency, except for security-critical parsing such as HTML and CSS sanitising, which uses an established library (ROADMAP §4.10).
   - **Dev-only tools** (build, test, lint, for example axe-core) may also be MPL-2.0, BlueOak-1.0.0, CC-BY-4.0 data or Python-2.0, used unmodified.
   - **Never, anywhere:** GPL, AGPL, LGPL, SSPL, source-available or commercially licensed packages (for example FluentAssertions 8+), and no direct ImageSharp reference.
   - **Nothing loads from third-party hosts on a visitor's page.** No web fonts from CDNs and no analytics. The only exception is a third-party captcha that an editor deliberately chooses, and it loads only per ROADMAP §4.10.
5. **Only this project is yours.**
   - **Where you write:** create and change files only inside this repository's folder (the one holding this file, whatever its name), plus throwaway folders named `jfb-*` under `%TEMP%`, plus the user-level caches that the .NET and npm tools manage themselves.
   - **Read-only references.** Read source and docs there, port code from them freely (the owner owns all three), and never edit, build in place, run or commit anything there. Read-only git commands (`log`, `show`, `ls-tree`, `archive`) are fine; never `fetch`, `pull`, `checkout`, `reset`, `stash` or `worktree` there.
     - `C:\Code\Ligata.Forms`: the owner's earlier forms package. Its checked-out `main` is still 0.2.1; the version the roadmap ports, 0.3.2 (`8f4b0c5`), exists only as `origin/main`. Read it with `git -C C:\Code\Ligata.Forms show 8f4b0c5:<path>`, or export it once with `git archive 8f4b0c5` into `%TEMP%\jfb-ligata-forms-0.3.2` (ROADMAP §3.1).
     - `C:\Code\Umbraco.Community.AI.Tandem`: the owner's chat package, finished on 2026-10-22 (all 110 steps). It was renamed to `Jiron.Umbraco.ChatSupport` mid-run (its packages and namespaces use that name; the folder kept the old one). Its scripts, test harness, ADRs and research notes are proven on this machine (ROADMAP §3.3).
     - `C:\Code\ligata`: the owner's website. Skip everything Cloudflare-related there.
   - **Off-limits:**
     - `C:\Coding\Umbraco.Baseline*` and `C:\Code\Umbraco.Baseline*`, entirely;
     - in other folders: `**/appsettings*.Local.json`, `**/.env*`, `**/.runtime/`, `**/keys*`, `*-key.txt` and `anthropic-key.txt`.
     - Exclude these globs in every search of another folder (for example `--glob '!**/.runtime/**'`). A broad search in Tandem's build once read an off-limits folder.
6. **Leave other processes and databases alone.** Another Claude session may be working on Tandem or another project on this machine at the same time.
   - **Processes:**
     - stop only processes you started, by PID, and stop the whole tree (`taskkill /T /F /PID <pid>`): a stopped shell or background task leaves its `dotnet`, `node` and `msedge` children running;
     - servers and long suites you start write a PID file under `.local/`, and the script that starts them also stops them;
     - never `taskkill /IM`, `Stop-Process -Name dotnet|node|msedge`, `dotnet build-server shutdown` or anything else that hits processes by name;
     - **throwaway builds** (clean clones, install tests, sites under `%TEMP%`) run with `MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_USE_MSBUILD_SERVER=0` and `-p:UseSharedCompilation=false`. Then no shared build server keeps their folders or a fresh NuGet cache locked, and they can be deleted without `dotnet build-server shutdown`.
   - **Ports:** never touch ports 1210, 1211 or 5400 to 5499 (Tandem's), and never run `pm2`. Jiron Form Builder's servers use ports 5500 to 5599 only (ROADMAP §4.11).
   - **Shared tools:** never change user-wide installs that another session relies on: no global `dotnet new install`/`uninstall` (use `--debug:custom-hive %TEMP%\jfb-templates-<version>`, ROADMAP §3.4), no global npm packages, no global .NET tools. Name your temp folders `%TEMP%\jfb-*`.
   - **Databases:** the SQL Server instance `MSSQL$UMBRACO` holds the owner's sites. **Never connect to it**, and never create or drop databases on any local SQL Server. Use SQLite locally; SQL Server tests run only in GitHub Actions.
7. **This machine is the owner's dev sandbox.** Create, run, reset and delete your own Umbraco sites (any 17.x or 18.x version), SQLite databases, backoffice users and members at any time, and log in and out with Playwright. Keep generated credentials in `.local/credentials.json`.
8. **Decide; don't ask.** The owner will not answer questions during the run. Choose the most robust, secure, accessible and user-friendly option, record non-trivial choices as an ADR in `docs/decisions/`, and continue.
   - **Owner-only items:** only the owner can do these, so list them in `docs/OWNER-TODO.md` and move on:
     - the license review and the contributor agreement;
     - name and trademark checks, and questions to Umbraco HQ;
     - publishing to NuGet or the Marketplace, and the nuget.org trusted-publishing policy;
     - repository visibility and settings (private vulnerability reporting, discussions, branch protection);
     - enabling CI and Dependabot, and checking CI runs;
     - real third-party keys (reCAPTCHA, hCaptcha, an AI provider) and a real SMTP server.
   - Never publish packages and never change repository visibility or settings.
   - **If the owner does write during the run,** the message beats the plan. Record it (an ADR when it changes a decision, otherwise WORKLOG), add or change roadmap steps for it without weakening any, and continue (ROADMAP "How to use").
9. **One step at a time.**
   - A step counts as done only when its "Done when" checks have run in the current session and passed.
   - **Commit order:** commit the work (Conventional Commits; a few commits if the step is large). Then tick the step with the date and the short hash of its last work commit, append a line to `docs/WORKLOG.md`, commit that as `docs(roadmap): complete X.Y`, and push to `origin main`. That means a push after every step at the latest. Each phase end is a milestone with a handover note.
   - **The bootstrap exception:** step 0.1 makes no commit. Its tick and WORKLOG line go into 0.2's commits.
   - **Every commit uses the repo-local identity** `Jiron <59139789+Jiron@users.noreply.github.com>` (set in 0.1; the pre-commit hook refuses any other). The machine's global identity is the owner's personal one and must never appear in this repository.
   - Never batch several steps into one commit. Never weaken or delete a step or a "Done when" check. A replacement needs an ADR and an equal or stronger check.
10. **Take your time.** There is no deadline. Quality beats speed every time.
    - Prefer the robust, tested design over the quick one.
    - When a step turns out bigger than written, keep it one step and work it in as many commits as it needs; never split a step or add small ones (the owner, 2026-11-06, ADR-0197), and never cut corners either.
    - Re-read the relevant ROADMAP sections before each step, and look at the result in a real browser before calling any UI done.
11. **No orchestration frameworks.** Do not use the Workflow tool or ultracode. Ordinary subagents are fine for focused research, reviews and parallel reading.

## Machine facts (checked 2026-10-22)

- Windows 11. Shells: PowerShell 5.1 and Git Bash. .NET SDK 10.0.401, Node 24.21, git 2.55.
- Microsoft Edge is installed. Use it for Playwright locally (`channel: 'msedge'`). The Claude desktop app's built-in browser is unreliable on Umbraco 17 backoffice screens. WebKit is not available, so phones are emulated in Chromium.
- Not available: Docker, SQL Server LocalDB, the `gh` CLI. Write scripts in Node, not Python. An unrelated SQL Server instance (`MSSQL$UMBRACO`) runs and is off-limits (rule 6).
- Tools damage text: the file tools, heredocs and PowerShell strings have eaten backslashes and turned escapes into real characters. Check unusual characters after writing (`scripts/check-text.mjs` from 0.2 on).
- You can't read GitHub Actions results from here, so `scripts/ci-local.ps1` mirrors CI and is the gate.
- `git push` to `https://github.com/Jiron/Jiron.Umbraco.FormBuilder` (private) uses the stored Git credentials.
- Commits in this repo use the repo-local identity `Jiron <59139789+Jiron@users.noreply.github.com>` (set in step 0.1, rule 9), so no personal address ends up in a repository that becomes public.
- Umbraco on 2026-10-19:
  - 17 LTS: the latest is 17.7.1 (17.8.0-rc is out);
  - 18 STS: the latest is 18.2.1;
  - 19: planned for 2026-12-10.

## Conventions

- **Names:**
  - NuGet and namespace root: `Jiron.Umbraco.FormBuilder`. Display name: **Jiron Form Builder**. Backoffice section label: **Form Builder** (the owner, 2026-11-03, ADR-0171; its forms tree stays "Forms").
  - Extension aliases: `Jiron.FormBuilder.*`. Database tables: `jfb*`. App settings section: `JironFormBuilder`.
  - **HTTP:**
    - public API: `/jfb/api/v1`, never under `/umbraco`;
    - management API: `/umbraco/jfb/management/api/v1`;
    - backoffice files: `/App_Plugins/JironFormBuilder/`;
    - website assets: `/jfb/assets/`.
  - **On the website:**
    - CSS classes `jfb-*` and custom properties `--jfb-*`;
    - data attributes `data-jfb-*`;
    - DOM events `jfb:*`;
    - JS global `window.JironFormBuilder`.
- **Vocabulary** (UI, docs, code), always these words:
  - **form**, **page**, **group**, **field**, **option list**;
  - **rule** (conditional logic), **action** (what runs after a submission);
  - **entry** (one submission), **theme**, **merge tag**, **template**.
- **Language:**
  - Code and docs are in English, and docs are updated in the same commit as the code they describe.
  - The backoffice UI comes in English and German.
  - Visitor-facing default texts come in English, German, French, Italian, Dutch, Spanish and Danish.
- **Quality bar:**
  - Nullable reference types on, warnings as errors (CS0618 included), async all the way, no sync-over-async, `TimeProvider` for time.
  - No static mutable state for anything that must work across servers.
  - Every behaviour has a test, and every public extension point has XML docs.
  - Rendered forms and the backoffice meet WCAG 2.2 AA.
  - UX follows ROADMAP §4.13: calm screens, and **say it once** (no fact shown twice on a screen, nothing squashed on a phone).
- **Branches:** `main` is the Umbraco 17 line, the most important one, and holds every ROADMAP and WORKLOG change. Umbraco 18 code lives only on `v18/main`; `main` is merged into it, never the reverse.
- **Progress line:** from step 0.2 on, end every turn by printing the output of `node scripts/roadmap-status.mjs`.
