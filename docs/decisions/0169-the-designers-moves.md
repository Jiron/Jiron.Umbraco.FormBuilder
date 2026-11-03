# 0169. The designer's moves: a handle on every element, refusals where they happen, keys on the focused element

- Status: accepted; narrows IA 5.1's "deleting or moving something shows a toast with Undo"
- Date: 2026-11-03
- Roadmap step: 9.3c (decided and built); builds on ADR-0016 (the pointer drag and its keyboard path) and ADR-0168 (the canvas)

## Context

Step 9.3c makes the canvas of 9.3b (ADR-0168) move things: drag and drop across groups and pages, "Move to…" for keyboard
users, refused moves explained in place, the keyboard of IA 5.1 (arrows, Alt+arrows, Delete with an undo toast, Ctrl+D,
Ctrl+Z/Y and the `?` overlay), and a focus that is never lost. ADR-0016 settled the engine: our own pointer drag on Pointer
Events with pointer capture, a 6 px threshold, an insertion line, autoscroll near the scrolling area's edge, Escape, one model
change on drop, and every move through the editor model's pure functions. What it left open, and what building it on the canvas
raised:

- **Where the handles are and when they show.** IA 5.1 puts the handle on the selected element. The wireframe shows it as a tab on
  the frame's top left, opposite the tools.
- **How groups and pages are dragged**, and where a field may land that is not on the page shown.
- **The palette:** IA 5.1 says "dragging places it".
- **"Explained in place":** where the reason of a refused move shows, for a drop, for Alt+Up at the top of the form, and in Move to….
- **What the keys act on** once Up and Down move the focus from element to element while the selection stays where it was.
- **Toasts:** IA 5.1 gives every move a toast with Undo; a keyboard user moving a field down five places would get five.
- **The 300-field budget** of §4.12 ("designer interactions under 100 ms"), and how to measure it so that it means something.

## Options

For the handles:

1. **On the selected element only** (IA 5.1 as written). One handle on the screen, but a drag always takes a click first, and a
   keyboard user tabbing through the canvas meets no handle.
2. **On every field and group, shown on hover, on keyboard focus and while selected.** One more tab stop per element; a drag starts
   at once; the canvas stays calm because a handle shows only where the pointer or the focus is.

For refusals:

1. **A toast**, as 9.3b says other refusals. Far from where the eye is, and it outlives the move it explains.
2. **At the line while dragging, and at the element after a drop or a key**, said by the live region, gone with the next change.

For what the keys act on:

1. **The selection only.** Up and Down would move the focus to one field while Delete deleted another.
2. **The element the focus is on, else the selection.**

For move toasts:

1. **Every move**, as IA 5.1 says.
2. **Drops and Move to… only.** A pointer can miss and a dialog's choice may surprise; keyboard moves come one after another, each
   said by the live region, with Ctrl+Z at hand.

## Decision

**Option 2 in each.**

- **Handles.** Every field (a repeatable group's fields too) and every group has a handle, a 28 px accent tab at the top left of its
  frame, named "Move Email address". It shows on hover, on keyboard focus and while its element is selected, as the selected
  element's tools do at the top right, and it is a button in the tab order after the element's own. A press that moves 6 px drags;
  a click selects. Pages move by their steps: a step pressed and moved drags its page among the steps, a click still shows it.
- **Where things land.** A field lands between the fields of a group or a repeatable group's row on the page shown, in reading order,
  which holds for fields side by side in columns too; a group among the page's groups; a page among the steps, where the line stands
  upright. Whether a field shares its row is judged with the dragged field still counted, so a field dragged out of a row of two
  doesn't make the other look alone. A page's step also takes a field or a group: dropped there, it goes to the end of that page (a
  field into its last group), and resting on the step for 600 ms shows that page, so the drop can go exactly where it should. A page
  that shows only its sentence takes a field anywhere on it, into its one group. A page lands among the steps only when it is let go
  within 24 px of them; anywhere else it stays.
- **The palette.** A type pressed and moved in the palette beside the canvas drags a new field of that type, which lands where it is
  dropped; a click still adds it. Over the canvas (700 px and narrower) the palette hides the page, so there a click adds.
- **While dragging,** in the top layer: a dashed slot keeps the dragged element's place, a 2 px line with round ends shows where it
  would land and jumps from place to place with the pointer, and a copy with its name follows the pointer. The selection's frame,
  its tools, the handles, the insert points and any refusal step back until the drop, so the line is the one accent on the page. The
  canvas scrolls within 48 px of its top or bottom edge, and below its bottom edge, where a hand overshoots; above its top lie the
  steps, which take drops themselves, so it does not scroll there.
- **Refusals in place.** The editor model is asked once per place as the pointer reaches it, by the same move or insert the drop
  makes, without changing anything. Where the form refuses, the line (or the step) takes the refusal colour, and the reason follows
  the pointer under the dragged copy, so it shows over a step too and is never cut off at a scrolling area's edge; a drop there
  changes nothing, the live region says the reason, and the element shows it beside its handle, below the selected element's
  tools, until the next change. Alt+Up at the
  top of the form and Alt+Down at its end say "It can't move any further." the same way. Move to… lists the places the form refuses
  as radio buttons that can't be chosen, each with its reason. None of these is a toast.
- **Keys on the focused element.** Alt+Up and Alt+Down, Delete, Ctrl+D and F2 act on the element whose button, handle or step has the
  focus, else on the selection, and select it first, so the step Undo goes back to holds it selected and Undo gives the focus back
  to it (a drop selects what it moves first, for the same reason). While a drag lasts, the keys change nothing. Up and Down go through the page shown in reading order: the page, each group (not the one group of a
  page that shows only its sentence), each field, a repeatable group's fields after it. A delete selects its element first, so the
  step Undo goes back to holds it selected and the focus returns to it. `?` opens Keyboard shortcuts, also in the canvas's "…", with
  only Close; the keys are named as the keyboard has them (Ctrl, Strg in German, Cmd on a Mac).
- **Focus.** After a keyboard move, on the same control of the moved element (its handle, its button or its step), on another page
  too; after a drop or Move to…, on the moved element's button (a page dragged by its step keeps it on the step); after a delete, on
  the neighbour that takes its place; after Undo or Redo, on what they restore, or on the page's Add field when they leave nothing
  selected and the focused element went; after Move to…'s Cancel, on the element; after the shortcuts, where it was. The user's own first change ends the wait for a linked field's selection (ADR-0166), so that wait never
  takes the focus from a move.
- **Words.** "Moved. Phone: position 1 of 5 in Address, Page 1: About you." for a field, "… position 2 of 3 on Page 1: About you."
  for a group and "… page 2 of 3." for a page, named after the move, since an untitled page or group is named by its place.
- **Toasts.** A drop and Move to… show "Field moved." (or Group, Page) with Undo; keyboard moves show none.
- **Move to….** "To the end of", the places by page as the browser's own radio buttons: each group, each repeatable group inside its
  group (Alt+Up and Alt+Down step over a repeatable group, so this is the keyboard's way into one), never the element itself or what
  it holds; for a group, the pages. Where the element is now says so, and is off only when it is at that end already. Move stays off
  until a place is chosen, Enter moves, and the move is made on the form as it is when the dialog closes.
- **Motion.** Only a drop moves: the dropped element settles from where its copy was, over 150 ms with `cubic-bezier(0.23, 1, 0.32, 1)`,
  on the compositor (a transform), and not at all with reduced motion. Keyboard moves, Move to… and Undo never animate (ADR-0016's
  amendment); the line jumps.
- **One drag at a time.** A second pointer while one lasts is ignored, Escape and a lost pointer end it without a drop, and the
  click that follows a drop, or the release after Escape, goes nowhere. Before the drag begins, Escape is the page's as ever. On a
  step or a palette type, which let the page scroll up and down under a finger, a finger moving mostly up or down scrolls and
  starts no drag.
- **The budget, measured as the browser sees it.** The browser test opens a form of 300 fields (three pages of ten groups of ten)
  and records every interaction from input to the next paint with Event Timing, and every frame the main thread held longer than
  50 ms with Long Animation Frames, while it selects, moves with Alt+Down and Up, duplicates, deletes, undoes and drags down the page
  with autoscroll; each must stay under 100 ms. The `budgets` project measures alone, after every other project, with one worker,
  so no other browser test shares the machine. Two things brought it there: a field's words and state are kept while what they are
  made of stays the same, so a selection or a move draws only the fields it changes, and the handle shows by sibling rules on its
  own button rather than by `:has()`, which the browser rechecked for every field at every move of the mouse.

## Consequences

- `designer/drag/` holds the engine (`pointer-drag.ts`) and the drop arithmetic (`drop-place.ts`), for the option editor and the
  action order later (ADR-0016); `canvas/canvas-drag.ts` puts them on the canvas, `moves/` holds the pure helpers (a drop's edit,
  where an element is, Move to…'s places), the two dialogs and the keys' names.
- The element's "…" of IA 5.1 (Change type…, Start a new page after this, Copy alias) comes with the inspector in 9.4, and Save group
  as template… with saved groups in 12.3; both steps say so.
- IA 5.1 now says: a handle on every element, refusals in place, keys on the focused element, and toasts for drops and Move to….
- Measured with Playwright in Edge on the demo machine, 300 fields, alone: the slowest interaction took 80 ms from the input to
  the next paint, and no frame held the main thread over 50 ms. Before the kept states and the sibling rules, the same interactions
  took up to 168 ms.
