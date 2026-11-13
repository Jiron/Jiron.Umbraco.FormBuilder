# 0213. What the Phase 11 UX walk changed: the chip under the name beside six views, a row's state under its name, the open row's colour, and rows the bulk bar would hide

- Status: accepted; amends decision 1 of [ADR-0206](0206-the-phase-10-ux-walk.md) (the chip wraps its words beside
  the name) and builds on [ADR-0182](0182-the-phase-9-ux-walk.md) (the walk and the phone),
  [ADR-0207](0207-the-entries-list.md) (the entries list) and [ADR-0020](0020-three-design-skills.md) (the design
  reviews); refined by [ADR-0250](0250-the-umbraco-18-line.md) where Umbraco 18.3 leaves the header less than the
  name's 6rem
- Date: 2026-11-08
- Roadmap step: 11.4, its part 11.6 (ADR-0197)

## Context

Step 11.6 walks the screens Phase 11 added (a form's Entries view with its filters, chips, export, bulk bar and entry
panel, the entry's own screen, the Inbox, Spam, a form's Insights view with its Answers, the Privacy group of its
settings, Settings › Data collection and Forms at a glance) at 375, 768 and 1440 px, light and dark, in English and
German (`tests/e2e/specs/ux/walk.spec.ts`), and has the three design reviews of ADR-0020 read the result against
ROADMAP §4.13. What the first walk found:

1. **The chip under Umbraco's "...".** Umbraco's workspace header shares one row between the form's name and chip,
   its own "..." for the form's actions, and the views' tabs, each part as wide as its contents ask, and its box for
   the name gives way without a least width. Phase 11 added two views, Entries and Insights, so the tabs ask for more,
   and at Umbraco's narrowest workspace, 620 px at 375 and 768 px, the name and the chip keep 166 px. The name keeps
   its 6rem (ADR-0206), so "Veröffentlicht" ran 39 px past the box, under the "...", and "Published" 7 px; at 1024 px
   the German chip still ran 2 px over. The narrowest journey did not see it: its user has four views, not six.
2. **Rows of two lines beside the panel.** Beside the entry panel, and in a view under 44rem as on a phone or in
   German at 768 px, the state's column squeezed the name, so "Person 0023" and "Waiting for approval" each took two
   lines.
3. **The toolbar's "..." on a line of its own** where the list's toolbar wrapped, away from the count it belongs with.
4. **One word, two translations:** the Insights view's tab said "Auswertung" and History's property "Auswertungen",
   where copy.md's glossary has no word for it.
5. **The open row in the dark theme:** the entry open in the panel takes Umbraco's colour for what is open, which
   carries its text at 4.34:1 there, under AA (axe, on the row's name and its time).
6. **Rows under the bulk bar.** The bar sticks to the view's foot while rows are ticked; `j` and `k` scroll the
   cursor's row to the nearest edge, which is under the bar, and so does Tab: a row the keyboard reaches could lie
   wholly under it (WCAG 2.4.11), and axe found a row's name half under it at 768 px.
7. **Change lines broken in two:** Forms at a glance broke "no change" and "up from none" over two lines in its
   columns at the 620 px workspace.
8. **The walk itself:** Spam had nothing to show, its Privacy shot opened the wrong group, and Answers took "Show as
   tables" from the look before, as every look signs in as the same admin.

The owner answers no questions during the run (CLAUDE.md rule 8); each choice below is this record's.

## Decisions

1. **The chip goes under the name where the two do not fit on one line.** The header's box lets its parts wrap: the
   name keeps the first line and its 6rem at least, and the chip takes the line under it whenever the name's 6rem and
   the chip's words do not fit side by side, which the flex line decides by the widths alone. Under the name, both
   take a smaller height so they stay inside Umbraco's 70 px bar, the longest state's two lines too: the name's box at
   UUI's 27 px, the chip with 1 px above and below its words. Its words wrap inside it only where even the header's
   whole width is too short for them. This replaces ADR-0206's chip that wrapped beside the name: with six views there
   is no room beside it, and a state under the name reads at once. The section claims no more of Umbraco's row than
   its contents ask: widening its own share would fold the views' tabs on windows wide enough for all of them today. `narrowest.spec.ts` opens a
   published form and one with changes waiting as the admin, with six views, in German at 375, 768 and 1024 px: the
   name's box 96 px at least, the chip inside the section's box and under the name at 375 and 768 px, both inside the
   bar, nothing cut.
2. **A row's state goes under its name below 44rem.** Below 44rem (704 px), and beside the panel, the list has no
   state column: each row's state chip sits under its name, so the name, the state and the time each keep one line
   (`STACKS` in `entries.view.ts`, held by the view's tests).
3. **The count and the "..." keep together** at the toolbar's end, on whichever line the toolbar's wrap leaves them.
4. **Insights is "Einblicke"** in German, the tab and History's property alike, and copy.md's glossary has its row.
5. **The open row is drawn in `--jfb-bo-color-current`,** Umbraco's colour for what is open mixed 85% with the
   surface as the section's selected colour is (docs/ux/tokens.md): 5.0:1 in the dark theme, 9.9:1 in the light and
   high-contrast ones. A failure's words in that row take the row's colour.
6. **While rows are ticked, a row the keyboard reaches stops above the bar:** the rows keep 8rem free at the bottom
   when scrolled to (`scroll-margin-block-end`), more than the bar's two lines at the narrowest.
7. **A change line keeps to one line** in Forms at a glance; the form's name gives way instead. At the 620 px
   workspace the table still fits without scrolling, in German too.
8. **The walk** gives its applications form three entries in Spam, opens the Privacy group by its id, and sets "Show
   as tables" for each Answers shot rather than flipping it.

## Consequences

- Positive: the form's header says the whole state at every width without covering Umbraco's "..."; the list reads
  one line per fact beside the panel; the open row and the bulk bar pass AA and WCAG 2.4.11 for the keyboard.
- Negative: on a narrow window the name's box is 6 px shorter than Umbraco's other headers have it, while the chip is
  under it; a list scrolled by the keyboard while rows are ticked stops 8rem short of the bottom edge.
- Kept as Umbraco's, for the report in OWNER-TODO: a `uui-checkbox` that is indeterminate writes `aria-checked="false"`
  on its native input, which axe reports on the "select all" of every table that ticks rows, Umbraco's own included;
  its tree's open row and its rows' names; its 920 px page (ADR-0182).
