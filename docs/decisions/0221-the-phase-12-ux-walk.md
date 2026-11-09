# 0221. What the Phase 12 UX walk changed: what a delivery is under its address, a quiet paused notice, hidden controls that stay hidden, and a fresh demo for the setup checklist

- Status: accepted; builds on [ADR-0182](0182-the-phase-9-ux-walk.md) (the walk and the phone),
  [ADR-0213](0213-the-phase-11-ux-walk.md) (a fact under the name rather than a squeezed column),
  [ADR-0218](0218-the-settings-screens-and-access.md) (the settings screens and access),
  [ADR-0219](0219-deliveries-and-the-audit-log.md) (deliveries and the audit log) and
  [ADR-0020](0020-three-design-skills.md) (the design reviews); amends the scroller's solid colour of
  [ADR-0186](0186-the-designer-budget-measures-the-main-thread.md) (its point 2)
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
9. **The phase gate met a settings test mid-change.** The browser projects share one host and run their specs side by
   side. Settings › Integrations' test saves a signing secret by a reference no host holds, to see the screen say so,
   and puts the old value back after its checks; in those seconds the Teams editor's test, in another worker, sent its
   test message, which the site could not sign (`secret_missing`). Settings › Access's test likewise gives the HR team
   more for a while.
10. **The designer's budget at the edge again.** The gate's next run failed the 300-field budget alone: the keyboard
    move at 104 ms against 100. Phase 11's own gated commit, `914b66a`, built in a clone the same day, failed it once in
    three at the same 104 ms, so the machine's drawing had drifted again as ADR-0186 found, and Phase 12 changed
    nothing on that path. Measured apart, an interaction's handlers took 9 to 17 ms and the rest was presentation,
    mostly the graphics chip's raster. The browser's paint flashing showed why: a move, a duplicate or a delete
    painted the canvas's whole view again, as the scroller's own background, solid for ADR-0186, is painted with
    its contents and so with every change to them; a plain focus move painted only the fields it touched.
11. **A long name ran under its row's "...".** The gate's next run failed the templates journey: the tree row's
    "..." took no click for four minutes, the row's own name lying over it. The forms tree draws its own label,
    without the id Umbraco's items give theirs, which Umbraco's styles end with an ellipsis; so a name ran on past its
    box, 582 px of it in a 300 px sidebar, under the "..." and past the sidebar's edge. A click on the "..." opened
    the form instead, for an editor as for the tests, and the walk's lost shots had met the same.

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
9. **Specs that change what the whole site reads run in a project of their own,** `site-wide`, after `smoke`,
   `website`, `backoffice` and `limits`, and before `budgets`, as `limits` runs after what could meet its limits
   (ADR-0137): Settings and Access move there. Each still puts back what it changed, and the specs inside it touch
   different sections. A spec that saves a site-wide setting or grant belongs there.
10. **The canvas's scroller has no background of its own:** the host's colour, which is the same, shows through it,
    and `will-change: scroll-position` keeps it a layer of its own, so it still scrolls without painting the sheet
    again. A move, duplicate or delete now paints only what changed. The 300-field budget then passed 4 of 4 runs
    alone, where it had passed 1 of 3, its slowest interaction 64 to 80 ms back to back, against 72 to 112.
11. **The forms tree's label is Umbraco's `#label`,** so a long name ends with an ellipsis before the row's "...",
    as in Umbraco's own trees. A unit test holds the label's id, and a browser test that the name ends before the
    "..." and a click there opens the row's menu; it fails without the id (the name ending at 633 px, the "..." at
    236 px). The tests' `openRowActions`, like the walk's own, tries the hover and the click again together until the
    menu shows, as a tree still reading rows can move one away from the pointer.

## Consequences

- Positive: Deliveries reads one line per fact at the 620 px workspace and keeps what each delivery is on a phone; no
  Phase 12 screen scrolls sideways inside the section's own elements; the notices look alike across the section; the
  checklist's five items are proven to turn green from a new site's start.
- Negative: on a narrow screen, what a delivery is has no column header of its own: it reads as the address's second
  line, as a row's state does in the entries list. The browser projects take a little longer, as the settings and
  access specs no longer run beside the backoffice's. The canvas's scroller is a layer without an opaque
  background, so the browser draws its text with greyscale rather than subpixel smoothing.
- Kept, with their reasons in `docs/ux/review-12.md`: a delivery's sidebar names the address in its headline and its
  first sentence, as the headline cuts a long one; a long name in a sidebar's headline cut with an ellipsis; Settings ›
  Access scrolling sideways past the workspace with its first column fixed (ADR-0218); the Permissions sidebar's Add
  above the table; Umbraco's 920 px page, its tree's rows' names and its open row in the dark theme (ADR-0182,
  OWNER-TODO).
