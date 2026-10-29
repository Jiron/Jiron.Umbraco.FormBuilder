# 0022. The default theme and its tokens

- Status: accepted; the error summary's heading for one answer or several, and its links to a default message naming
  their field, amended by [ADR-0099](0099-visitor-error-messages.md) in 6.2g
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
  words under the field, with an icon, in bold error red; the control gets an error-coloured double border. (A link
  to one of the package's default messages puts the field's label before the words, ADR-0099.)
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

## Amendment (2026-10-23, step 2.4)

The design critique of step 2.4 (`docs/ux/critique-2.md`) found gaps that change or extend the decision above.

- **The ground check.** A form with no `jfb-scheme-*` class takes the light tokens. On a dark site that did not opt
  in, its labels sat at about 1.15:1 on the page. After layout, the runtime (6.4) reads the first opaque background
  behind the form; when it is dark and no scheme class is set, it adds `jfb-scheme-dark`. The site's own choice
  always wins: a class set by the site is never changed. Without JavaScript the class must be set, which the theme
  editor and the docs say. A Playwright check in 6.4 covers it.
- **Reduced motion stops movement, not feedback.** The theme moves things by two private distances (`--_rise` 4 px,
  `--_slide` 8 px) over `--_move`. With `prefers-reduced-motion: reduce` those three become zero, so an error or a
  new page fades in where it ends, a pressed button or a switch's knob jumps instead of gliding, and colour changes
  keep their timing. The first
  version set every duration to zero, which also removed fades that move nothing.
- **Sending.** The primary button carries `data-jfb-busy` and `aria-disabled="true"` while the answers are on their
  way: it keeps its size and focus, its label gives way to a turning ring (a slow pulse with reduced motion), and the
  live region says "Sending your answers". A refused or failed send (`rate_limited`, `server_error`, `timeout`) puts
  an error notice just above the buttons with `role="alert"` and moves the focus to it; every answer stays.
  `form_changed` puts a notice in the error summary's place (ADR-0004). A choice that filled up meanwhile is an
  ordinary error: on the summary page the error summary links to that answer's "Change".
- **Notices** have three places: instead of the form (closed, not yet open, full, members only, sent before, an
  expired link), above it (the form has changed, welcome back), or just above the buttons, lined up with them (not
  sent, save for later, saved). `jfb-notice-error` is the variant for a send that did not go through.
- **The verification page** that `GET verify?t=` serves (8.5) is a plain page of the package's own, outside the
  site's templates: the system font, the site's name, the form's theme with `jfb-scheme-auto`, one button that posts
  the confirmation, and on an expired link one button that sends a new one (`assets/standalone.css` in the mocks).
- **Step folding.** Step names show from 480 px for up to four steps and from 720 px for up to six; seven or more
  always fold into "Step 2 of 7" with short bars. The browser cannot always hyphenate (headless Edge has no German
  dictionary), so a long compound broke inside a word; the runtime therefore also sets `data-jfb-compact` when a word
  of a step name is wider than its step, after layout and on every resize. The title shows whenever the names fold.
- **Read-only is not disabled:** read-only answers keep full ink on the wash with the ordinary edge; disabled ones are
  muted with a dashed edge.
- **New parts of the markup contract:** `jfb-other` (the text an "Other" choice asks for), `jfb-captcha` (the consent
  notice and "Load the check" for a third-party captcha in the `explicit` mode of §4.10), `jfb-skeleton` (the
  headless element's placeholder while the definition loads, with `aria-busy` on the form), `data-jfb-pending` on
  the outcome (a letter instead of the tick while the address is unconfirmed), and the redirect notice with "Go now"
  and "Stay here" (WCAG 2.2.1). A move button that cannot act is `aria-disabled`, so it stays in the tab order.
- **Right to left** is shown on an Arabic page (`book-rtl.html`), since English text set right to left showed the
  mirroring wrongly.
- **Shots** are taken at 375, 768 and 1440 px, as §4.13 asks, and the checker measures every state at 480, 600, 720
  and 1024 px as well.
