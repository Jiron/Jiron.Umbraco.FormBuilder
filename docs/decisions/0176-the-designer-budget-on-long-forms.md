# 0176. The designer's budget on long forms

- Status: accepted; builds on [ADR-0169](0169-the-designers-moves.md) (the budgets project, section 4.12) and
  [ADR-0168](0168-the-canvas-draws-with-the-elements-views.md) (the canvas draws the form in its theme)
- Date: 2026-11-04
- Roadmap step: 9.4d (its gate found it)

## Context

Section 4.12 holds the designer to 100 ms per interaction on a form of 300 fields, which the `budgets` project measures
in the browser (Event Timing) as the last project of the gate. The gate of 9.4d failed it: the first click on the form,
which selects a field and opens the inspector, took 176 ms while another run shared the machine, and alone it took
96 to 120 ms (three of seven runs over the budget). A profile and a trace of that click in Edge found three causes:

1. **Umbraco's workspace scroll container** fades its scrollbar in and out as the pointer or the focus comes and goes,
   by a transition of `scrollbar-color` (120 ms in; 480 ms out, after 480 ms). The property is inherited, so every
   frame of a fade restyled every element below it: the whole Design view, about 4,200 elements, 18 ms a frame for up
   to half a second after a click, a key or a drag.
2. **The page's buttons** (Back, Next, Send), which the canvas writes with the element's own views, were a new node at
   every render, so a selection took them out and put new ones in; the theme's rules that look inside the form
   (`:has()`) then restyled the whole page.
3. **What remains grows with what is drawn:** a page of 100 fields is about 4,700 elements, and a click restyles all
   of them once when Umbraco marks the body as the mouse is pressed, lays the canvas out again when the inspector
   opens beside it, and restyles the page again for the theme's `:has()` rules when the selected field's tools go in.
   The same first click measured 80 to 104 ms as built, 64 ms with every `:has()` rule taken out of the canvas, and 40
   to 48 ms with the groups out of sight not drawn.

## Options

1. **A looser budget, or a warmer first click.** Either weakens a check (CLAUDE.md rule 9), and the first click is what
   an editor does first.
2. **Theme rules without `:has()`.** The website's markup, its Razor views and the element's views would carry classes
   the rules read instead; a theme a site brings could still use `:has()`, and the gain (to 64 ms) leaves the budget
   thin on a slower machine.
3. **`content-visibility: auto` on every group.** The browser skips the styling, layout and painting of each group out
   of sight. It is the largest gain, but its containment always applies: each group clips what reaches past it (its
   outline and its tools, which `overflow-clip-margin` would let through) and becomes a stacking context, so the next
   group's select button would cover the insert point at a group's end and take its clicks.
4. **`content-visibility: auto` on the groups far from the view only:** a group a whole view above or below what is in
   sight is marked, at the height it has then; a group in reach stays as it is.

## Decision

Option 4, with causes 1 and 2 fixed where they start:

- **Far groups wait.** The canvas watches its page's groups with an `IntersectionObserver` on its viewport, one view's
  height above and below (`canvas/canvas-far.ts`). A group outside that is marked `data-far`, its height kept as its
  `contain-intrinsic-size`, and only a marked group has `content-visibility: auto` (`canvas.styles.ts`). The drag never
  measures a far group: the pointer is never over one, and measuring it would make the browser draw it.
- **The page's buttons are kept** while their classes and words stay the same (`form-canvas.element.ts`).
- **The view stops the fade at its edge:** it sets `scrollbar-color` itself, to the colour the container shows while in
  use, so the transition no longer reaches the canvas and the inspector (`form-design.view.ts`).

## Consequences

- The budget holds with room: alone, the slowest interaction is 72 to 80 ms and the longest frame at most 52 ms (four
  runs), against 96 to 120 ms and 66 to 90 ms before.
- A far group stays in the accessibility tree, the browser's find still finds its words, and anything that scrolls to
  it (a link, the keyboard, `scrollIntoView`) draws it first. A group changed while far (by Undo, say) takes its new
  height as it comes near; the browser's scroll anchoring keeps what is in sight still.
- The canvas's and the inspector's scrollbars keep the colour the workspace shows while in use, rather than fading.
- A theme's `:has()` rules still cost a restyle of the page shown at a selection; a page of hundreds of fields in one
  group would still be drawn whole. Both stay within the budget measured here, which the gate keeps checking.
