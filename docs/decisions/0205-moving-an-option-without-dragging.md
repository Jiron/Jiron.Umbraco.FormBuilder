# 0205. Moving an option without dragging

- Status: accepted
- Date: 2026-11-07
- Roadmap step: 10.4 (part of 10.3, ADR-0197); takes up what [ADR-0185](0185-the-outcomes-editor.md) left to the
  phase review; builds on [ADR-0172](0172-the-option-editor-and-the-option-sources.md) (the option editor) and
  [ADR-0016](0016-designer-interaction.md) (reordering); IA 5.1, copy.md

## Context

The inspector's option editor lists a choice field's own options, and a grid's rows, each with a handle. A row moves by
dragging its handle, or with Alt+Up and Alt+Down on it, which says where it went. WCAG 2.2's 2.5.7 (Dragging Movements,
AA) asks for a way that a pointer alone does without dragging; keys are no such way. An outcome's and an action's
sidebar meet it with "Place in the list", a select of "2 of 3" ([ADR-0185](0185-the-outcomes-editor.md),
[ADR-0188](0188-the-actions-view.md)). An option has no sidebar: its details open under its row, and a grid's rows
have none.

## Options

1. **Move up and Move down buttons on every row.** Two more buttons on each line of an already full row, and a long
   way for an option from first to last.
2. **"Place in the list" in an option's details.** The words of the sidebars, but a grid's rows would need details of
   their own just for it, and the move hides behind a disclosure that says nothing of moving.
3. **The handle's press.** The handle is a button named "Move Red" whose press does nothing today: a press that never
   becomes a drag opens the row's "Place in the list" under its line, the sidebars' select, and choosing a place moves
   the row there.

## Decision

Option 3. A press on a row's handle that does not become a drag (the shared drag starts only after 6 px,
[ADR-0169](0169-the-designers-moves.md)) opens "Place in the list" under the row, as its details open, with the
select's "1 of 3", "2 of 3" and so on; the
handle says it is expanded and controls it, and the select takes the focus. Choosing another place moves the row as a
drag or Alt and the arrows would: one step to undo, said in the status line ("Moved. Red: position 3 of 3."), the
handle keeping the focus. Choosing its own place, Escape, or a second press closes it and moves nothing. A list of one
row has no other place, so its handle opens nothing. A grid's rows and a field's own options work the same way; Enter
and Space on the handle open it too, beside Alt and the arrows.

## Consequences

- Positive: every way to reorder in the section now has a pointer path without dragging, each with the same words.
- Positive: nothing is added to a row's line; the handle that moves the row is where moving starts, for a pointer, a
  finger and the keyboard.
- Negative: the select lists every place, so a list of many options gives a long select; the number of own options is
  small by design (option lists hold long ones).
