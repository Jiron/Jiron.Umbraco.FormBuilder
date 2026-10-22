# 0016. Designer interaction: our own pointer drag, with keyboard moves and announcements

- Status: accepted
- Date: 2026-10-22
- Roadmap step: 1.2a (measured in the sandbox); applies to 9.3c and to every reorderable list

## Context

The form designer moves fields between groups and pages, and groups and pages among themselves (§1, §4.13
"Editing", step 9.3c). The same need comes back in smaller lists: options in the option editor, the order of
actions, ranking settings. D18 and §4.13 ask for keyboard paths, and WCAG 2.2 AA asks for a single-pointer
alternative to dragging (2.5.7), names for every control and announcements for changes. Step 9.3c wants focus
never to be lost after a move.

Two candidates were built side by side in the 1.2a sandbox, editing the same nested model (two pages, five
groups, eleven fields), and measured with Playwright in Edge (`docs/research/umbraco-backoffice.md`, section
"Drag and drop"):

- **Umbraco's sorter:** `UmbSorterController` from `@umbraco-cms/backoffice/sorter`, one controller per level
  (pages, groups, fields), each with its own identifier, as in the official nested-containers sample.
- **Our pointer drag:** the pointer-capture drag that Ligata.Forms 0.3.2 used for one flat list, grown to
  nested containers: Pointer Events with `setPointerCapture`, a 6 px start threshold, `touch-action: none` on
  the handle, an insertion line, autoscroll near the edge of the scrolling area, Alt+Up/Down on the focused
  handle, and a polite live region.

What was measured, in Umbraco 17.7.1:

| Measure | Umbraco's sorter | Our pointer drag |
| --- | --- | --- |
| Mouse drag inside a group and into another group | works | works |
| Keyboard | none: the handles cannot take focus (`tabIndex` -1) | Alt+Down moves a field, also into the next group; focus stays on the moved field's handle |
| What a screen reader gets | the field labels as plain text: no list, no control, no name | a list per group named by its heading, a button "Move First name" per field, and an announcement such as "Moved. Phone: position 1 of 5 in Address, Page 1: About you." |
| Touch (Chromium touch events) | nothing moves: native HTML drag and drop does not start from a finger | the field moves |
| Autoscroll while dragging near the bottom edge | scrolls (0 → 415 px) | scrolls (0 → 427 px), once the scroll parent is found through slots (see below) |
| Model updates | the model changes live while dragging; the result must be read at the end | one change, on drop |
| Code we own | small; behaviour is Umbraco's | about 180 lines for the spike, plus pure model functions that are tested in Node |

The spike's pointer drag moved fields only. Moving groups and pages uses the same engine with the same rules,
but has not been measured yet; 9.3c builds and tests it.

## Options

1. **Umbraco's sorter, plus a separate keyboard path.** Least code for the mouse. But keyboard, names,
   announcements and touch would all have to be added around a controller that does not expose them, with
   handles it keeps unfocusable and a model it rewrites during the drag. Touch would stay broken.
2. **Our own pointer drag, with keyboard moves and announcements built in.** One engine for mouse, pen and
   touch, keyboard moves that share the same pure move functions, and one place for focus and
   announcements. More code to own and test.
3. **No drag at all, only "Move to…" and keyboard moves.** Fully accessible, but slow for the common case and
   below what editors expect from a designer (§1).

## Decision

Option 2. Jiron Form Builder's designer, and every reorderable list in the backoffice, uses one shared
pointer-drag controller of its own, built on Pointer Events with pointer capture. Every move goes through pure
model functions (move, step to the neighbour, refuse a move that breaks a rule), which drag, keyboard and the
"Move to…" dialog all call. The rules:

- **Pointer:** a drag starts after 6 px on the handle; the handle has `touch-action: none`; an insertion line
  shows the target; the scrolling area scrolls while the pointer is within 48 px of its edge; Escape cancels;
  the model changes once, on drop.
- **Keyboard:** every handle is a real button named "Move <label>"; Alt+Up/Down moves one place and crosses
  into the neighbouring group or page; "Move to…" opens a dialog for any target; focus stays on the moved
  element after every move.
- **Announcements:** a polite live region says the new position in words ("position 1 of 5 in Address, Page 1:
  About you"), and says so when a move is refused or not possible.
- **Finding the scrolling area:** walk up through `assignedSlot` before `parentNode`, and leave shadow roots
  through `host`. Content slotted into a workspace scrolls inside the slot's shadow container, so a walk that
  skips slots finds no scrolling area at all (the spike's first version did exactly that).

Umbraco's sorter is not used in Jiron Form Builder.

## Consequences

- One controller to build and test well in 9.3c: mouse, touch (Chromium emulation), keyboard-only journeys and
  the focused element after every operation, as 9.3c's "Done when" asks.
- The same controller serves the option editor, the action order and any other list, so every list gets the
  same keyboard path, names and announcements without extra work.
- We own the autoscroll and the drop-target maths; the spike's versions are the starting point.
- Revisit if a later Umbraco version gives the sorter keyboard support, accessible names and touch, and moves
  its model only on drop; then compare again with a spike like 1.2a's.
