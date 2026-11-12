# 0186. The designer's budget measures the main thread's hold, not the browser's drawing after it

- Status: accepted; the browser the budget is held in amended by
  [ADR-0243](0243-the-designer-budget-in-a-browser-that-has-drawn-before.md) in 16.5
- Date: 2026-11-05
- Roadmap step: 10.1b (its gate); amends [ADR-0169](0169-the-designers-moves.md) (the 300-field budget) and builds on
  [ADR-0176](0176-the-designer-budget-on-long-forms.md); §4.12

## Context

ADR-0169 set the designer's budget for a form of 300 fields: every interaction under 100 ms from the input to the next
paint (Event Timing), and "every frame the main thread held longer than 50 ms" under 100 ms, which the test measured as
each Long Animation Frame's `duration`. ADR-0176 then measured it with room: the slowest interaction 72 to 80 ms, the
longest frame at most 52 ms.

10.1b's gate failed on that test alone, the drop at 112 ms, though nothing 10.1b changed is on the drop's path. What
the measurements here found, on this machine, with the machine otherwise idle (`.local/look101b/budget-notes.md` holds
each run):

1. **The test counted its own recorder.** Playwright's trace, kept for failures, snapshots the page at every action by
   walking its whole DOM on the page's thread: 160 to 210 ms per action by the browser's CPU profiler, against 10 to
   16 ms of the designer's own work. The `budgets` project now records no trace (ADR-0169's addendum).
2. **Two fades and a scroller repainted the sheet.** The handle and the insert points faded in and out over 120 ms at
   every selection, focus and hover, each frame of it painting the whole sheet again; and the canvas's scroller, which
   the browser saw through, had no layer of its own, so every scroll painted the sheet again too. Both are gone
   (`canvas.styles.ts`, tokens.md): the handle and the insert points show at once, the scroller has the host's solid
   colour.
3. **What is left is the browser's drawing, not the main thread.** With 1 and 2 the slowest interaction is 64 to 96 ms,
   but a frame right after the drop still lasts 85 to 110 ms. Its parts, by the CPU profiler without a trace: the drop
   holds the main thread about 45 ms (moving the field's node, the inspector's scroll back to its top, the focus, a
   measure), and the main thread is then idle, but for each frame's few milliseconds, until that frame ends some 40 ms
   later. The browser reports no task in it of 50 ms or more, and none that blocked it (`blockingDuration` 0). A paint
   with the field in its new place comes about 30 ms after the release, which Event Timing counts for the drop.
4. **The same code measures differently today.** ADR-0176's own commit, `d6bad04`, run in a clone the same way, has
   longest frames of 92, 110 and 94 ms; 10.1a's `5a46a03` has 104, 108 and 100 ms, so its gate passed by luck. The
   machine's drawing has grown slower since ADR-0176 measured, by what no commit here changed (the browser, its
   drivers, the machine's load). The same build, run twice on one host, had no frame over 50 ms in one run and a
   frame of 133 ms in the next.
5. **What was tried for the drop and did not help:** delaying its Undo toast until the field had landed (and the
   previous toast's closing), half a view of drawn groups instead of one, no glide after the drop, a drag cursor rule
   by name instead of for every element, the inspector hidden, the high-performance graphics processor, and a layer
   per group (worse, 96 to 201 ms).

## Options

1. **Keep a frame's whole duration as the measure** and chase the machine's drawing. It fails or passes by the
   machine's state, which "flaky means broken" refuses, and no change to the designer moved it.
2. **Measure what ADR-0169 describes, the main thread's hold**, with the browser's own entries: every task that holds
   it (Long Tasks), and each long frame's own work, its scripts and its rendering (Long Animation Frames); keep the
   interactions as they are, and keep reporting a frame's whole duration.
3. **Retry, or take the median of several runs.** It hides a slow interaction that comes once in three.

## Decision

Option 2. The test (`tests/e2e/specs/budgets/designer.spec.ts`) asserts, for the same steps of the same form:

- every interaction under 100 ms from the input to the next paint, as before;
- every task that holds the main thread, scripts and the microtasks after them included, under 100 ms: the limit
  ADR-0169 set for a frame the main thread held;
- each long frame's own work, its scripts and its rendering, under 100 ms, and its rendering (style, layout, paint)
  under 50 ms, half the limit before.

A frame's whole duration stays in the test's report beside them ("longest frame"), so a change in the drawing shows.

## Consequences

- A designer that holds the main thread fails as before, or sooner: a 120 ms stall injected into the keyboard's
  handling (in the built chunk, the source untouched) failed with a task of 134 ms, 136 ms of work in a frame and an
  interaction of 192 ms. A frame whose rendering takes 50 ms fails where before only 100 ms did.
- No longer asserted: the time a frame waits for the browser's drawing after the main thread is done. Where an input
  waits for it, the interaction's own measure still counts it, input to paint.
- Five runs with the decision: interactions 72 to 96 ms, no task of 50 ms or more, at most 43 ms of work in a frame
  (rendering 5 ms), frames 91 to 128 ms long; the 128 ms one held the main thread for no task of 50 ms or more.
