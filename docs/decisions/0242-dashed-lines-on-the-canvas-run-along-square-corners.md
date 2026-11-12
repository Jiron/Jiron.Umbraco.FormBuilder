# 0242. Dashed lines on the canvas run along square corners

- Status: accepted; builds on [ADR-0169](0169-the-designers-moves.md) (the 300-field budget),
  [ADR-0176](0176-the-designer-budget-on-long-forms.md) and
  [ADR-0186](0186-the-designer-budget-measures-the-main-thread.md); what its measures counted amended by
  [ADR-0243](0243-the-designer-budget-in-a-browser-that-has-drawn-before.md) in 16.5
- Date: 2026-11-12
- Roadmap step: 16.5 (its phase-review gate found it); §4.12, §3.5 "Flaky means broken"

## Context

The second phase-review gate of 16.5 failed on the designer's budget alone: on the form of 300 fields, Alt+ArrowDown,
which moves the selected field one place down, took 112 ms from the key to the next paint, against 100 ms. Five runs
alone on the same build then passed at 72 to 88 ms. The roadmap allows no rerun for such a failure: it is a timing
bug that the machine's load brought out, and it is fixed at its cause.

What the measurements found, on this machine, idle unless said otherwise (Edge, headless, drawing on its graphics
chip through Direct3D 11):

1. **The main thread was not the cost.** Splitting each interaction by Event Timing's own marks, Alt+ArrowDown's
   handlers took 1 ms and its wait for the paint 63 to 95 ms. No task or frame held the main thread 50 ms.
2. **Nor the backoffice, nor the form's length.** The same hover on the header's section tabs painted in 32 ms on
   Umbraco's Content section, on the Overview, on the Design view and on a blank page; a hover or a key over the
   canvas took 63 ms or more, on a form of ten fields as on one of 300.
3. **The graphics chip's raster was.** A trace of Alt+ArrowDown showed five tiles recorded in a millisecond, then one
   raster flush in the GPU process of 43 ms before the frame was shown. The compositor's layers were as ADR-0169 meant
   them (the canvas's scroller composited, its contents tiled); the `position: relative` that 16.6 gave the scroller
   changed nothing.
4. **The cost was a dashed line around rounded corners.** One change at a time, three to four runs each, the move by
   the keyboard (Alt+ArrowDown) and the next key (ArrowDown), in milliseconds:

   | Change | Alt+ArrowDown | ArrowDown |
   | --- | --- | --- |
   | none (two series) | 64 to 104 | 24 to 72 |
   | no shadows on the canvas | 72 to 88 | 32 to 40 |
   | the handles and insert points not drawn at all | 64 to 104 | 40 to 72 |
   | the selection buttons' corners square | 32 to 56 | 16 to 24 |
   | the same, only while a button shows a ring | 32 to 56 | 24 to 56 |
   | rounded buttons, the ring under the pointer solid | 32 to 56 | 24 to 56 |
   | the selection ring drawn as a rounded border | 88 to 112 | 64 to 72 |

   The button over each field draws the ring of the field selected (solid), focused (solid) or under the pointer
   (dashed). After a click the pointer stays over the canvas, so a move by the keyboard brings another field under it,
   and the dashed ring is drawn around that field. A dashed line along rounded corners is drawn as a path; along
   square ones, or solid around rounded ones, it is not, and the cost went.
5. **The drag had the same cause.** Its first moves took 64 ms in every series until the slot that keeps the dragged
   element's place, a rounded box with a dashed edge, was made square (24 to 32 ms) or solid (24 to 32 ms); the copy's
   shadow, the hidden chrome and the cursor changed nothing.

## Options

1. **Solid lines.** A solid ring under the pointer and a solid slot are as fast, but the dashes are what say "not
   chosen yet" and "a place", beside the selection's solid ring.
2. **Square corners where a line is dashed.** The canvas's selection buttons, the drag's slot, a hidden field's note
   and the places to add a field lose their 3 px corners; the dashes keep their meaning, and the selection reads as a
   selection box does in a design tool.
3. **Rerun the gate.** Refused by §3.5: the next loaded run fails the same way.

## Decision

Option 2. In `canvas/canvas.styles.ts`, the button over a field, a group or a page has square corners in every state,
and so do the drag's slot, a hidden field's note and an empty group's and a page's end's place to add. The rule holds
for every dashed or dotted line on the canvas: `canvas/canvas-rings.test.ts` finds each element that draws one and
fails if any rule gives that element rounded corners. The handle and the tools bar keep their rounded top corners:
their edges are solid. The visitor's form keeps its theme's corners; only the designer's own marks changed.

Dashed lines elsewhere in the backoffice (an empty trigger, the Overview's open steps, the inspector's locked
setting) are drawn once, not with every frame of an interaction, and stay as they are.

## Consequences

- Five runs of the budget on the built change: the slowest interaction 64 to 88 ms, no task of 50 ms, at most 1 ms of
  work in a frame; Alt+ArrowDown 32 to 56 ms and a drag's first moves 24 to 32 ms (`docs/performance.md`).
- What is left nearest the budget: the click that selects a field (64 to 88 ms: the inspector's work, a task of about
  42 ms) and Delete (72 to 80 ms: one raster flush of about 46 ms as the fields below move up, whatever the corners;
  ADR-0186 found a layer per group worse). Both stayed at those levels in the failed gate.
- A new dashed line on the canvas comes with square corners, or the test says which rule rounds it.
