# 0254. A simpler backoffice: the owner's five decisions

- Status: accepted as a direction; nothing of it is built. The owner's decisions on the draft
  [docs/ux/simpler-backoffice.md](../ux/simpler-backoffice.md). When its steps are built they turn round parts of
  [ADR-0021](0021-forms-section-structure.md) and [ADR-0165](0165-the-section-shell.md) (the sidebar),
  [ADR-0170](0170-the-inspector.md) (what the inspector shows with nothing selected) and rules of
  [information-architecture.md](../ux/information-architecture.md) (the field list beside the form, the footer on
  every view); until then those stand and describe the product
- Date: 2026-11-16
- Roadmap step: Phase 22, steps 22.1 to 22.4 (the owner wrote after the last step, so the message beats the plan)

## Context

The owner tried the demo after step 21.1 and found the backoffice rich but hard to find a way through: a page could
be added in two places and deleted in none that was found, "Add field" between two fields opened nothing, the left
side listed every settings screen at once, and the designer stood in four columns. The owner asked for a rough
roadmap of its own, and then for it to be gone over again and challenged before any work starts.

The draft names what was seen, why each screen is as it is, a direction for each, four steps, what to measure and
what it costs. It left five questions that were the owner's.

## Decision

The owner decided all five on 2026-11-16, each as the draft proposed it:

1. **The field list beside the form goes.** A picker, opened from "Add field" and from between two fields, is the one
   way to add. A pin that holds the list open comes only if it is missed.
2. **The left side's fixed items come before the tree of forms,** cut into Inbox, Library, Settings, Privacy and
   Activity, each one screen with tabs along its top.
3. **"After sending" is edited in the designer,** where it is shown, and leaves the form's Settings.
4. **Preview, Save draft and Publish show only on the views that change the form,** though Umbraco's document
   workspace keeps its buttons on every view.
5. **The work is done before the repository goes public.**

With them stand the draft's six rules: one way and one place; pick first, details after; the form gets the room; what
is fixed never gives way to what grows; one level of folding; Umbraco's pattern before an own one.

## Consequences

- **Nothing is built by this record.** The owner said to start nothing yet, and then, the same day, to add the
  draft's four steps to ROADMAP.md: they are Phase 22, steps 22.1 to 22.4. Each starts with a wireframe and its own
  record, which answers the reason the earlier decision had.
- **What the owner accepted with it:** one more click for every field added and no dragging of a type from a list onto
  the form; a departure from Umbraco's footer; and that the browser tests, the guides, the screenshots and the top
  tasks follow each step.
- **No function goes.** Things move behind a pick. Dragging to move fields and pages, the keyboard paths and WCAG 2.2
  AA stay.
