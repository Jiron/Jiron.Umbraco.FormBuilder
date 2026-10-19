# The /goal for building Jiron Form Builder

Start Claude Code **in this repository's folder**, preferably in auto mode, and paste the goal. `/goal` accepts up to 4,000 characters, and the text stays under that. The owner's own copy in the git-ignored `.private/GOAL.md` adds owner-only context; when it exists, paste that one instead.

```text
/goal Build Jiron.Umbraco.FormBuilder by completing ROADMAP.md in this folder step by step, in order, until no step is open. There is no deadline: take your time, quality beats speed.

Each turn: follow CLAUDE.md (binding). Read ROADMAP.md in full on the first turn; after a context compaction re-read its "How to use", §2, the current phase and the sections the step cites. Take the first open step ("- [ ]") and work only on it. A step is done only when its "Done when" checks ran in this session and passed, with the output shown. Then commit the work, tick the step (date + hash of its last work commit), add a WORKLOG line, commit "docs(roadmap): complete X.Y" and push to origin main before the next step. Steps 0.1-0.2 make no commit except the 0.2 root commit; their ticks go into 0.3. Each phase ends with a review and a handover note.

Non-negotiable:
- The 0.2 root commit holds LICENSE.md byte for byte as placed (SHA-256 in 0.2). Replacing GitHub's commit 06f126f with the exact --force-with-lease in 0.2 is approved once; never force-push otherwise.
- Write only inside this folder (plus %TEMP%\jfb-*). C:\Coding\Ligata.Forms, C:\Coding\Umbraco.Community.AI.Tandem and C:\Coding\ligata are read-only references: port freely, never edit, build or run there; skip Cloudflare parts.
- Jiron Form Builder is original work (CLAUDE.md rule 2): never install, decompile or copy from another product, and never name other form products in anything tracked or pushed.
- No secrets in git; the secret scan and term guard run before each commit. No real third-party keys: stubs and fakes only.
- Official Umbraco packages only; permissive licenses only (CLAUDE.md rule 4).
- Another session may be building Tandem here: never touch processes you did not start, ports 1210/1211 or 5400-5499, pm2 or MSSQL$UMBRACO. Use ports 5500-5599.
- Dev sandbox: create, reset and delete your own Umbraco sites, users and members freely; credentials in .local/, never printed.
- Don't ask the owner anything: pick the most robust, secure, accessible, user-friendly option, write an ADR, continue. Owner-only items go to docs/OWNER-TODO.md. Never publish packages or change repo settings.
- No Workflow tool or ultracode; ordinary subagents are fine.
- Quality first: clean architecture, a test for every behaviour, calm uncluttered UX (ROADMAP §4.13), accessibility, docs with the code, zero warnings. Never weaken or delete a step or check.

If a step is blocked by something outside your control, write the reason under it, mark it [~], add it to docs/OWNER-TODO.md and continue with the next step that doesn't depend on it. Retry blocked steps at each phase end.

End every turn with the output of "node scripts/roadmap-status.mjs" (before 0.3, name the current step) and of any checks run this turn.

The goal is met only when, in this session, step 20.1 passed with its summary shown, and the last roadmap-status line shows open=0, blocked_missing_in_owner_todo=0, clean=yes and head equal to origin_main.
```

## Before the first run (owner)

- Read `LICENSE.md`. Step 0.2 commits it unchanged and checks its SHA-256. If you change it, update the hash in ROADMAP step 0.2 to `node -e "console.log(require('crypto').createHash('sha256').update(require('fs').readFileSync('LICENSE.md')).digest('hex'))"`.
- The name is settled: Jiron Form Builder (`Jiron.Umbraco.FormBuilder`). The GitHub repository already carries it. The local folder's name doesn't matter; the plan always refers to it as "this folder".
- Leave the folder as it is. Step 0.1 checks for exactly the planning files, plus `.private/` and an optional `.claude/`.

## Tips for the run

- **Effort:** xhigh, at least through Phases 1 to 5 (architecture, data model, the logic engines). Use high for later UI and docs work if usage matters, but keep xhigh for the UX review steps.
- **It is long.** There are 21 phases and over a hundred steps. Every step ends with a push, so progress is always visible:
  - in `ROADMAP.md` (the checkboxes);
  - in `docs/WORKLOG.md`;
  - in the git history on GitHub.
- **Stopping and checking:** `/goal` with no text shows the status. `/goal clear` stops the run.
- **Compaction:** it happens automatically when the context fills up. Nothing is lost:
  - every step's state is in files;
  - CLAUDE.md is reloaded after a compaction;
  - the goal says to re-read the roadmap;
  - every phase ends with a handover note.

  To make compactions earlier and smaller, you can set the environment variable `CLAUDE_AUTOCOMPACT_PCT_OVERRIDE` (for example `70`) before starting Claude Code. This is optional.
- **Restarting:** if the session ends, start a new one in this folder and paste the same goal. It continues from the first open step.
- **Running next to Tandem:** both builds can run at the same time. They use different ports (Tandem 5400 to 5499, Jiron Form Builder 5500 to 5599) and never stop each other's processes.
