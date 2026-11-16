# 0255. Phase 22 is built in one stretch: one coding agent, judged, and the long runs at its end

- Status: accepted; the owner's message of 2026-11-16. For Phase 22 it amends the roadmap's "How to use" (finishing a
  step) and [ADR-0194](0194-the-full-gate-runs-at-milestones.md), and it weakens no check
- Date: 2026-11-16
- Roadmap step: Phase 22, before 22.1 (the owner's message; ROADMAP "When the owner writes during the run")

## Context

Phase 22 stood in the roadmap with four open steps and the owner's word to start nothing yet
([ADR-0254](0254-a-simpler-backoffice.md)). On 2026-11-16 the owner wrote to finish the four steps, and how:

- the runs that take very long wait until all four steps are built, and run then;
- there is no hurry, and the result is to be as clean as it can be;
- where possible one agent, on Opus 5.5, writes the code, and the main session judges it critically and sends it back
  until the work is good; only then does the work go on.

"How to use" ticks a step once its checks passed for its last work commit, and starts the next step after that tick.
Each of 22.1 to 22.3 names the walk over every screen and the checks on `v18/main` among its "Done when" checks, and
those are long runs.

## Options

1. **Tick each step on its quick checks,** and leave its long checks to 22.4. A tick would then say "done" for a step
   whose checks have not all run, which the roadmap forbids.
2. **Build the four steps one after the other,** each on its quick checks, run every long check once on the phase's
   final build, and tick the four steps then, in order. Every check runs; none is weakened; a tick still means what it
   says.
3. **Run the long checks per step anyway.** Against the owner's word.

## Decision

Option 2.

- **Per step, on its last work commit, here:** `node scripts/fix.mjs`, the build with warnings as errors,
  `dotnet format --verify-no-changes --no-restore`, the unit tests, the client's lint, typecheck and tests, the e2e
  typecheck, `node scripts/size-check.mjs`, and the browser specs the step adds or changes, run by name. The step's
  screens are looked at in a real browser before the next step starts (CLAUDE.md rule 10).
- **Once, on the phase's final work commit:** the walk in its looks with the word comparison, the five journeys, the
  whole browser suite, the integration suites, the count of the top tasks, the checks on `v18/main`, and the
  phase-review gate (`node scripts/verify-clean.mjs`).
- **The ticks come after the long runs,** one commit a step, in order, each naming its step's last work commit. So in
  this phase a step's first commit comes before the tick of the step before it. What a long run finds is fixed in the
  step it belongs to, and the runs it touches are repeated.
- **`main` is merged into `v18/main` after each step's work,** with the build and the unit and client tests there, so
  the two lines never drift far; the browser suite there is one of the long runs.
- **One agent writes the code,** on Opus 5.5, one at a time. The main session draws the wireframes and writes each
  step's record, briefs the agent, reads its whole diff, runs the checks itself, looks at the screens, and sends the
  work back until it holds. Nothing is committed that was not judged so. This narrows the owner's word of 2026-11-15
  (no waves of agents) to one.

## Consequences

- `node scripts/roadmap-status.mjs` shows four open steps until the phase's end, though their work is committed and
  pushed. `docs/WORKLOG.md` has a line when a step's work is complete, so a context compaction loses nothing.
- A failure found by a long run is found later than a run per step would find it. The quick checks and each step's
  own browser specs keep that risk small, and the owner chose the trade.
- After Phase 22 "How to use" stands as written.
