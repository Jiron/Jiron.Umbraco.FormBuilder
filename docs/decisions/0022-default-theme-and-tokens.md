# 0022. The default theme and its tokens

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 2.3 (decided); built in 6.2 (the views' markup), 6.3 (the theme) and 6.4 (the runtime's states)

## Context

ADR-0015 fixed how themes work: one set of views, a class map, a stylesheet and `--jfb-*` tokens per theme; every
rule under `.jfb-form`; logical properties only; state in attributes; dark scheme opt-in. It left the default theme's
look, and the token values, to step 2.3.

The constraints:

- **Where the form lives:** inside a customer's page, with the customer's font, colours and layout around it, often
  in a content column, often on a phone. The theme must look at home on a site it has never seen.
- **ROADMAP §4.13:** calm screens, say it once, one visual system (a corner scale, one shade, a spacing grid, a type
  scale), targets of at least 44 px, WCAG 2.2 AA.
- **§4.12:** the default theme's CSS within 10 KB gzip.
- **Visitors:** members of the public, mostly on phones, filling in a form they did not come for. The design skills'
  reading of the brief (`docs/ux/review-2.3.md`) is "trust-first, accessibility-critical": low variance, little
  motion, medium density.
- The owner answers no questions during the run (CLAUDE.md rule 8); the design skills of ADR-0019 and ADR-0020 ran on
  the work, and their outputs are in `docs/ux/review-2.3.md`.

## Options

**The page structure**

1. One column at every width: familiar, but on a wide screen every group heading is one more line in a long scroll,
   and the fields stretch to 960 px.
2. A card per group: clear grouping, but boxes inside the host's page compete with it, and repeaters or cards inside
   them nest boxes in boxes.
3. **A ledger** (chosen): when the form is at least 720 px wide, a titled group's heading and help sit in the first
   third and its fields in the other two; below that, one column. Hairlines separate groups. A card style stays
   available per group for an editor who wants one.

**Controls**

1. The browser's own checkboxes, radios and dropdown arrows, tinted with `accent-color`: least CSS, but they differ
   per browser, and unticked boxes often miss 3:1 against white (WCAG 1.4.11).
2. **Drawn controls on native inputs** (chosen): `appearance: none` on the real `input`, so keyboards, forms and
   screen readers work as ever; the border token is measured at 3:1 or more in both schemes; ticks and arrows are
   masks that take the text colour and survive forced colours.

**The page title under step progress**

1. Show both: the step list names every page, and the page title names the current one again.
2. **The current step stands in for the title** (chosen): from 480 px the page's `h2` is visually hidden (it stays for
   screen readers) and the step list's current item, in bold, is the visible title, so the page's name shows once.
   Narrower, the list becomes "Step 2 of 3" with short bars, and the title shows.

**Colours**

1. One brand colour for actions, chosen states and focus.
2. **An evergreen accent for actions, chosen states, links and done steps, and a separate blue for the focus ring**
   (chosen), so the keyboard's place is never confused with what is chosen. The error red is used for errors only.

## Decision

The default theme is the ledger with drawn controls on native inputs, built as `docs/ux/website/assets/theme.css`
and `base.css`, with the token values in `docs/ux/tokens.md` (nine colour, five corner, one shade, seven spacing,
seven type, two size, one focus and three motion tokens). The site's font and size are inherited; three type steps
(.875, 1.25 and 1.5 em) sit around them. Further decisions:

- **Field anatomy:** label, help, control, error, in that order; "(optional)" marks optional questions by default,
  and an editor can switch to asterisks on required ones, with a one-line note above the first question.
- **Errors:** an error summary at the top that names each problem as its fix and links to the field, plus the same
  words under the field, with an icon, in bold error red; the control gets an error-coloured double border.
- **The default button:** a page with other submit buttons before Next or Send (Remove, Change) starts with a
  visually hidden copy of its Next or Send button, so Enter in a field moves on instead of removing a row.
- **Uploads:** the native file input works without JavaScript; the runtime enhances it to "Choose files" and, on
  touch screens only, "Take a photo", hides the drop text and frame there, and lists files with their state.
- **The summary page and the outcome:** answers in rows under hairlines, each with "Change"; the outcome puts the
  reference on its own line and leaves the answers to the copy below it, so each fact shows once.
- **A card group** keeps its frame inside the form's box and aligns its field column, the line the inputs and the
  Next button follow, with every other group's; its heading and the right edge of its fields sit one card padding in.
  A frame that bled outward by its padding would put the headings on one line too, but it needs 25 px of free space
  on both sides of the form, which the theme cannot see or reserve. Hosts put forms in containers that clip their
  content (`overflow: hidden` on rounded cards, tabs, accordions, dialogs), where a bleed would cut off the card's
  frame and shade, and in full-width layouts with no gutter, where it would make the page scroll sideways (WCAG 1.4.10)
  at viewports just over 720 px. The inset costs a slightly narrower card; the bleed could break the host's page.
- **Motion:** a pressed button sinks 1 px; errors, new repeater rows and new pages come in over 200 ms with a strong
  ease-out, a page from the side the visitor moved towards; nothing moves with reduced motion.

## Consequences

- **Step 6.2** writes the views with the classes and attributes listed in `docs/ux/website/README.md`, which extend
  ADR-0015's table. **Step 6.3** ships `theme.css` and `base.css` as they are, and the token names become public API
  under the versioning policy of ADR-0010. **Step 6.4's runtime** sets `data-jfb-enhanced`, `data-jfb-drop` and
  `data-jfb-direction`.
- `scripts/website-mocks.mjs --check` is the gate for any change to the theme until 6.3 brings its own tests: it
  measures contrast, focus rings, targets, tokens, the grid and the CSS budget on every mock.
- The theme uses `:has()`, container queries, `color-mix()`, `@starting-style` and CSS nesting. All are in the
  browsers ROADMAP §4.12 supports; where `@starting-style` is missing, things simply appear without moving.
- Revisit if sites find the ledger wrong for their layouts (a per-form "one column" option is cheap to add), or if the
  `bootstrap5` theme of ADR-0015 needs a structure this one cannot share.
