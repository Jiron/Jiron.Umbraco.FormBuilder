# The /goal for building Jiron Form Builder

Start Claude Code **in this repository's folder**, preferably in auto mode, and paste the goal. `/goal` accepts up to 4,000 characters, and the text stays under that. The owner's own copy in the git-ignored `.private/GOAL.md` adds owner-only context; when it exists, paste that one instead.

```text
/goal Build Jiron.Umbraco.FormBuilder by completing ROADMAP.md in this folder step by step, in order, until no step is open. There is no deadline: take your time, quality beats speed.

Each turn: follow CLAUDE.md (binding). Read ROADMAP.md in full on the first turn; after a context compaction re-read its "How to use", §2, the current phase and the sections the step cites. Take the first open step ("- [ ]") and work only on it. A step is done only when its "Done when" checks ran in this session and passed, with the output shown. Then commit the work, tick the step (date + hash of its last work commit), add a WORKLOG line, commit "docs(roadmap): complete X.Y" and push to origin main before the next step. Step 0.1 makes no commit; its tick goes into 0.2. Each phase ends with a review and a handover note. Every commit uses the repo-local noreply identity set in 0.1.

Non-negotiable:
- The owner's root commit 781401e holds LICENSE.md byte for byte as placed (SHA-256 in 0.1). Never change the license, never force-push.
- Write only inside this folder (plus %TEMP%\jfb-*). C:\Code\Ligata.Forms (read 0.3.2 from commit 8f4b0c5), C:\Code\Umbraco.Community.AI.Tandem and C:\Code\ligata are read-only references: port freely, never edit, build or run there; skip Cloudflare parts.
- Jiron Form Builder is original work (CLAUDE.md rule 2): never install, decompile or copy from another product, and never name other form products in anything tracked or pushed.
- No secrets in git; the secret scan and term guard run before each commit. No real third-party keys: stubs and fakes only.
- Official Umbraco packages only; permissive licenses only (CLAUDE.md rule 4).
- Another session may be working here: never touch processes you did not start (stop your own by PID, whole tree), ports 1210/1211 or 5400-5499, pm2 or MSSQL$UMBRACO. Use ports 5500-5599.
- Dev sandbox: create, reset and delete your own Umbraco sites, users and members freely; credentials in .local/, never printed.
- Don't ask the owner anything: pick the most robust, secure, accessible, user-friendly option, write an ADR, continue. Owner-only items go to docs/OWNER-TODO.md. Never publish packages or change repo settings. If the owner writes anyway, the message beats the plan: record it and adjust the roadmap.
- No Workflow tool or ultracode; ordinary subagents are fine.
- Quality first: clean architecture, a test for every behaviour, calm uncluttered UX (ROADMAP §4.13), accessibility, docs with the code, zero warnings. Never weaken or delete a step or check.

If a step is blocked by something outside your control, write the reason under it, mark it [~], add it to docs/OWNER-TODO.md and continue with the next step that doesn't depend on it. Retry blocked steps at each phase end.

End every turn with the output of "node scripts/roadmap-status.mjs" (before 0.2, name the current step) and of any checks run this turn.

The goal is met only when, in this session, step 20.1 passed with its summary shown, and the last roadmap-status line shows open=0, blocked_missing_in_owner_todo=0, clean=yes and head equal to origin_main.
```

## Before the first run (owner)

- The license is committed: your root commit `781401e` (2026-10-20) holds `LICENSE.md`, and step 0.1 checks its SHA-256. A licence change later is a normal commit that you make yourself, plus the new hash in ROADMAP step 0.1.
- The name is settled: Jiron Form Builder (`Jiron.Umbraco.FormBuilder`). The GitHub repository already carries it. The local folder's name doesn't matter; the plan always refers to it as "this folder".
- Leave the folder as it is. Step 0.1 checks for exactly the planning files (including `docs/research/tandem-lessons.md`), plus `.private/` and an optional `.claude/`.

## Tips for the run

- **Effort:** xhigh, at least through Phases 1 to 5 (architecture, data model, the logic engines). Use high for later UI and docs work if usage matters, but keep xhigh for the UX review steps.
- **It is long.** There are 21 phases and 107 steps (Tandem had 110). Every step ends with a push, so progress is always visible:
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
- **Running next to Tandem:** Tandem is finished, but a session working on it can still run at the same time. They use different ports (Tandem 5400 to 5499, Jiron Form Builder 5500 to 5599) and never stop each other's processes.
- **Talking to the run:** you can write to it at any time, for example after looking at the screenshots of a UX review. It records what you say and turns it into roadmap steps.
