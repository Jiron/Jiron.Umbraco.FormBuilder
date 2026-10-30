# 0117. Long checks run beside the next step

- Status: accepted; the owner's message of 2026-10-30; amends the roadmap's "How to use" (finishing a step) and
  [ADR-0011](0011-testing-strategy.md) (where the gate runs)
- Date: 2026-10-30
- Roadmap step: between 6.4g and 6.4h (the owner's message; ROADMAP "When the owner writes during the run")

## Context

The owner wrote during the run, on 2026-10-30, asking that the run not wait long on the gate and the test runs when
it could go on with the next tasks meanwhile.

Until then a step ran its long checks in this folder and nothing else happened while they ran: the gate
(`scripts/ci-local.ps1`, a quarter of an hour or more), a clean clone of each work commit, and the hand mutants. Three
things kept the next step from starting beside them:

- the gate builds and tests the working folder, so the next step's edits would have changed what it checked, and a
  failure could have come from either step;
- the browser projects listen on fixed ports (5520 to 5522 and 5560, `tests/e2e/support/paths.ts`), so two browser
  runs at once collide;
- the hand mutants rewrite source files while they run.

Open were:

1. where the long checks run;
2. what the next step may do meanwhile;
3. when a step counts as finished and is pushed;
4. what happens when a long check fails after the next step has started.

## Options

1. **Keep waiting.** Safe, and against the owner's message.
2. **Run the gate in this folder and edit meanwhile.** The gate would check a mix of two steps, and the next step's
   build could lock or rewrite files under the gate. Rejected.
3. **Commit the step's work, run its long checks on that commit in a throwaway clone, and build the next step here
   meanwhile.** Chosen: the gate checks exactly the commit that will be pushed, and this folder stays free.

## Decision

- **Where the long checks run.** `node scripts/gate-clone.mjs run <commit>` clones the commit into
  `%TEMP%\jfb-gate-<short hash>`, runs `scripts\ci-local.ps1` there with the throwaway-build settings (CLAUDE.md rule
  6), writes its log and verdict under `.local/gate/`, copies a failed run's browser results there, and removes the
  clone, its workspace links one by one first. Its PID file records the start time, and `stop` ends its whole
  process tree. A clean clone of each work commit and the hand mutants run in clones of their own under `%TEMP%\jfb-*`
  in the same way, never in this folder.
- **Before the clone's gate.** The step's focused checks pass in this folder: `node scripts/fix.mjs`, lint and the
  typechecks, the unit tests and the Vitest projects, the integration tests of the area it touched, its own Playwright
  specs, axe and the size check. Then its work is committed, not pushed.
- **Meanwhile.** The next step starts in this folder: reading, research, design, code and its fast tests. Nothing
  here listens on the browser projects' ports while a clone's gate holds them (the script refuses to start when one is
  taken), and only one gate runs at a time.
- **Finishing.** As before, a step is done only when its gate and its other checks passed, now for its last work
  commit in the clone. Its tick and WORKLOG line are then committed, with only those two files staged while the next
  step's changes stay in the working tree, and pushed. The next step's first commit comes after that tick, so `main`
  keeps the steps in order and nothing unchecked is pushed.
- **A late failure.** The fix is committed on top of the step's work, with only the fix's files staged, and the gate
  runs again on the new commit, which the tick then names. The next step's work waits in the working tree meanwhile.

## Consequences

- The run waits for a long check only when nothing else can move: when the next step needs a browser run while the
  clone's gate holds the ports, or at a phase review, whose own gate and full Playwright suite are its checks.
- The machine carries two loads at once. "Flaky means broken" still holds: a check that fails under that load is a bug
  to fix at its cause.
- A clone costs a fresh `npm ci` and restore, a few minutes, which the overlap more than repays.
- The goal's order "push before the next step" now reads "push before the next step's first commit"; the owner's
  message decides (CLAUDE.md rule 8).
