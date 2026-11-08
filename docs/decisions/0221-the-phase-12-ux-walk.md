# 0221. What the Phase 12 UX walk changed: what a delivery is under its address, a quiet paused notice, hidden controls that stay hidden, and a fresh demo for the setup checklist

- Status: accepted; builds on [ADR-0182](0182-the-phase-9-ux-walk.md) (the walk and the phone),
  [ADR-0213](0213-the-phase-11-ux-walk.md) (a fact under the name rather than a squeezed column),
  [ADR-0218](0218-the-settings-screens-and-access.md) (the settings screens and access),
  [ADR-0219](0219-deliveries-and-the-audit-log.md) (deliveries and the audit log) and
  [ADR-0020](0020-three-design-skills.md) (the design reviews)
- Date: 2026-11-09
- Roadmap step: 12.4, its part 12.6 (ADR-0197)

## Context

Step 12.6 walks the screens Phase 12 added (the library's option lists, themes, email layouts and templates with their
editors, the settings sections for all sites and for one, Settings › Access and a folder's Permissions…, Deliveries and
the audit log with their sidebars, and Replace… on a form) at 375, 768 and 1440 px, light and dark, in English and
German (`tests/e2e/specs/ux/walk.spec.ts`), and has the three design reviews of ADR-0020 read the result against
ROADMAP §4.13. It also follows the Overview's setup checklist (9.1) through every item on a freshly reset demo. What the
walk and the reviews found:

1. **Deliveries squeezed at 620 px.** At Umbraco's narrowest workspace, 620 px at 375 and 768 px, the list hid the form
   and kept four columns, so a long address took three lines and the action's name beside it two ("Email the / team").
   Below 520 px it hid what each delivery is altogether.
2. **The paused notice** drew a 4 px warning edge down one side of a box, which the bundled detector names as a
   side-tab border, and which no other notice in the section has: the publish dialog's notes are a quiet frame with the
   weight said by an icon.
3. **A hidden control drawn at full width.** The option list editor's file input is visually hidden for its upload
   button, but the form controls' own sizing rule gave it the box's width, so the screen scrolled sideways by 43 px at
   1440 px.
4. **A long address past a sidebar's edge.** A delivery's sidebar names the address in its first sentence; an address
   has no break of its own, so at 375 px the sidebar scrolled sideways by 11 px.
5. **The email layout editor's groups touched.** The grid's gap sat on `uui-box`, which lays out its own content, so the
   groups had no space between them.
6. **"Standarddesign"** labelled the default theme in Settings › General, where every other German screen says
   "Standard-Theme" and the glossary keeps "design" off the word.
7. **The walk itself:** the Permissions shot found a folder with no group and so no table, and a tree row's "…" timed
   out now and then: the walk brought a row only just into sight, at the sidebar's edge, where it moves as the pointer
   reaches it and the click lands on its neighbour, which `support/tree.ts` had already met in the browser specs.
   Umbraco 17's SQLite hung under four workers.
8. **The checklist needs a site with nothing on it.** The demo seeds forms, content and the test team, so every item of
   the checklist starts green there.

## Decisions

1. **What a delivery is goes under whom it went to at 760 px and narrower,** as the entries list puts a row's state
   under its name since ADR-0213: a muted line under the address, in the same cell, with no column of its own, while the
   form gives way as before. It stays there below 520 px rather than going. The screen measures itself (`STACKS` in
   `deliveries.screen.ts`, held by its tests), so it is said once, in one place.
2. **The paused notice is a note as the publish dialog draws one:** the control border all round, the warning's icon
   beside its first line in the warning's text colour, and its sentence breaking a long key anywhere.
3. **`jfbVisuallyHidden` wins over any sizing rule** a control's styles bring: position, size, overflow, clip and wrap
   each `!important`, as the utility is only ever asked for to hide.
4. **A delivery's sentences break an address anywhere** (`overflow-wrap: anywhere`), as its rows and its details' values
   already did.
5. **The email layout editor's groups sit in a block of their own inside the box,** 24 px apart (UUI's space 6), as the
   other editors' groups are.
6. **The default theme is the "Standard-Theme"** in German.
7. **The walk** seeds an option list, a theme, a template, an email layout and three deliveries of each look's own, adds
   a group to the folder before the Permissions shot, brings a tree row to the sidebar's middle as `support/tree.ts`
   does, and tries its hover and its menu's opening again together until the menu's item shows. It runs with two
   workers.
8. **The setup checklist runs on a demo started fresh** (`setup-checklist.spec.ts`, port 5537): `Demo:Seed` false skips
   the demo's seeding, which `scripts/demo.ps1` never sets, the test team and the stub's port are left out, and
   Umbraco's sender carries a display name, which Umbraco accepts and forms' mail cannot use as its own sender. A host
   may name its own ready page and ready lines, as one without content or people has neither.

## Consequences

- Positive: Deliveries reads one line per fact at the 620 px workspace and keeps what each delivery is on a phone; no
  Phase 12 screen scrolls sideways inside the section's own elements; the notices look alike across the section; the
  checklist's five items are proven to turn green from a new site's start.
- Negative: on a narrow screen, what a delivery is has no column header of its own: it reads as the address's second
  line, as a row's state does in the entries list.
- Kept, with their reasons in `docs/ux/review-12.md`: a delivery's sidebar names the address in its headline and its
  first sentence, as the headline cuts a long one; a long name in a sidebar's headline cut with an ellipsis; Settings ›
  Access scrolling sideways past the workspace with its first column fixed (ADR-0218); the Permissions sidebar's Add
  above the table; Umbraco's 920 px page, its tree's rows' names and its open row in the dark theme (ADR-0182,
  OWNER-TODO).
