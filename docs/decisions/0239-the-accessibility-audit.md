# 0239. The accessibility audit

- Status: accepted
- Date: 2026-11-12
- Roadmap step: 16.1 (part 16.3, the accessibility audit)

## Context

Part 16.3 asks for axe on every theme and field type and every backoffice screen, automated checks for what a person
would otherwise check by hand (names, roles and states, the keyboard alone, forced colours, reflow at 640 and 320 CSS
pixels, reduced motion), an accessibility-statement template for site owners, and a table of every WCAG 2.2 A and AA
success criterion that says what was tested and never claims conformance, with a test that fails when a criterion is
missing. Most of the automated checks came with the features (6.3, 6.8, 9.6, the UX walks); a survey of them against
the criteria found three that no suite held and one behaviour that fell short.

## Decision

- **Reflow and text spacing get a suite of their own** (`reflow`): the specimen, which holds every kind of control as it
  reads without JavaScript's help, the contact form, and the room hire, whose upload the runtime takes over, in every
  built-in theme and both languages, at 640 and 320 CSS pixels, with and without WCAG's text spacing. The page never
  scrolls sideways, nothing of a form reaches past the window, and no text of a form is cut off.
  It found four places, each fixed in the stylesheets:
  - without JavaScript, the browser's own file input kept its natural width (312 pixels in Edge) in a 320-pixel window,
    as a grid track grows to its widest item's least width; the input and its row may now shrink with the form (the
    base rules, so every theme);
  - a scale's two end words, at the body's size in the Bootstrap 5 theme and without a theme, set the scale's width by
    their longest words once the letters were spaced; they now break when nothing else fits, with a hyphen where the
    browser knows the language (the base rules);
  - in the Bootstrap 5 theme's columns, a field's one column grew to its date-and-time control's natural width; it is
    now as wide as the field, as in the default theme, and the control scrolls its own text as every text input does;
  - the demo's own page heading, one long German word, now breaks rather than scroll the page.
- **The entries list's keys of one character act only while the focus is in the list** (WCAG 2.1.4). They also acted
  while the focus was on the page's body, so a word that speech input typed there could approve, reject or mark entries
  as spam. The designer's `?` already listened on the designer alone.
- **The table lives in `docs/accessibility.md`,** one row for each of the 55 criteria, saying for the website (the default
  theme) and the backoffice (the section) whether it applies, which suite tests it, or "by hand", or "not tested", and
  every known gap, Umbraco's own among them as Umbraco's. `scripts/tests/accessibility.test.mjs` holds the list of
  criteria and fails on one missing, one twice, one that is not in WCAG 2.2 at A or AA, a wrong level, or an empty cell.
- **The statement template** (`docs/accessibility-statement-template.md`) is marked as a template on its first line, in
  English and German, and gives the facts of the forms for a site's own statement, whose form a site's law may prescribe.
- **A real screen reader** stays with the owner ([OWNER-TODO](../OWNER-TODO.md)).

## Consequences

- A change that breaks reflow, text spacing or the keyboard's reach fails a suite, as a change to contrast or names
  already did.
- The table is kept with the code: a new suite or a gap changes its row in the same commit.
