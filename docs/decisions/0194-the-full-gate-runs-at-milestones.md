# 0194. The full gate runs at milestones, not after every step

- Status: accepted; the owner's message of 2026-11-06; amends [ADR-0117](0117-long-checks-run-beside-the-next-step.md)
  and the roadmap's "How to use" (finishing a step)
- Date: 2026-11-06
- Roadmap step: between 10.2d and 10.2e (the owner's message; ROADMAP "When the owner writes during the run")

## Context

ADR-0117 ran the full gate, `scripts/ci-local.ps1` with the whole browser suite, in a throwaway clone on every step's
last work commit, and ticked the step only once it passed. On this machine one run takes 70 to 75 minutes, and its
browser stage needs the machine otherwise idle, so the next step's own browser runs and tamper rounds wait for it. A
failure outside the step costs a further run: 10.2d took four gates (a journey's drag helper, a time limit that aborted
an answer, a hung database, then a budget 4 ms over), though the step's own checks passed on the first.

The owner wrote during the run, on 2026-11-06, that the gate runs take too long, and asked for them only at bigger
milestones so the project moves faster.

## Options

1. **Keep a gate per step** and shorten it. Its time is the integration suite and the browser suite, which are the
   point of it; a shorter gate per step is still a wait per step.
2. **The full gate at each phase review only**, which the roadmap's phase-review gate requires already, and a fixed set
   of focused checks per step.
3. **A gate every few steps.** Arbitrary: a failure is no easier to place than at a phase end, and it still waits.

## Decision

Option 2.

- **A step is finished on its focused checks,** run here on its last work commit:
  - `node scripts/fix.mjs`, then the build with warnings as errors and `dotnet format --verify-no-changes --no-restore`;
  - the unit tests, and the integration test classes of what the step touches;
  - the client's lint, typecheck and tests, and the e2e typecheck;
  - its "Done when" checks, its journeys among them;
  - the hooks' secret scan and term guard, as before.

  A step that changes the build, the packages, the test harness or the host's start-up runs the stages of
  `ci-local.ps1` it changes as well.
- **The full gate runs at each phase review** (the phase-review gate in "How to use"), in a throwaway clone with
  `node scripts/gate-clone.mjs run <commit>`, and wherever a step's "Done when" names `ci-local.ps1`. Its failures are
  fixed at their cause there, under "Flaky means broken", before the phase is ticked.
- Tamper rounds stay with the steps that build behaviour, in a clone beside the step's other checks; with no gate on
  the machine they no longer wait for it to be idle.

No step and no "Done when" check changes: none of the steps' checks named a gate per step, and the phase-review gate
stays as it was.

## Consequences

- A step no longer waits an hour or more for its gate, nor its next step's journeys for the gate's ports.
- A failure the focused checks miss surfaces at the phase review, with several steps' changes between it and the last
  gate. The gate logs name each failing test, and the phase review bisects by commit where a cause is not plain.
- 10.2d is ticked on its own checks, which passed on e7fe1c3a in this session: its four webhook, Slack and Teams
  journeys against the stub, and the integration suite with the permission matrix and the OpenAPI snapshot. The gate
  that was running then was stopped. Its last run missed the designer's 300-field budget by 4 ms (a keyboard move of
  104 ms, against 64 to 96 ms measured for ADR-0186), with nothing 10.2d changed on that path; Phase 10's review gate
  checks it again.
