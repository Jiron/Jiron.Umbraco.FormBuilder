# 0107. Markup that reads without a theme

- Status: accepted; extends [ADR-0105](0105-the-theme-registry-and-the-base-rules.md)'s base rules and the markup
  contract of [ADR-0015](0015-themes-and-the-markup-contract.md) (a rating's numbers, the lists' roles), and amends
  [ADR-0106](0106-the-default-theme-ships.md)'s default theme only where it must hide what the markup now shows
- Date: 2026-10-30
- Roadmap step: 6.3c (decided and built); 6.3d gives `bootstrap5` its stylesheet on the same markup

## Context

ADR-0015 describes `unstyled` as semantic markup and the package's classes, for a site that writes its own CSS, plus
the functional base rules every theme ships (ADR-0105). Step 6.3c looked at the demo's specimen, a form with a field
of every built-in type, in Edge with `unstyled`: the browser's own look plus the base rules. Most of it read well,
but in five places the markup leaned on the default theme to say what a control means, or said a thing twice:

- **A rating's points** were radio buttons whose labels held only text for screen readers ("1, Poor"), since the
  default theme draws a star for each. Without it, five bare circles stood in a row.
- **A ranking's moves** were buttons holding a symbol the default theme draws and text for screen readers. Without
  it, they showed as empty boxes.
- **A scale's end words** ran together ("PoorExcellent", "Not likelyVery likely"), and the points stood in a column
  with the words below the last one, so neither word sat at its end.
- **A grid's column heads** showed above the rows as one line of words, and each answer's label named its column
  again.
- **A ranking's places** showed twice ("1. 1 Opening hours"): the list's own numbers and the place the markup writes,
  which only the ranked options have.

Edge also named a scale's first point "0 , Not likely", with a space before the comma, since 6.2d: the words at the
end sit in a visually hidden span after the number, and a visually hidden span is absolutely positioned, which makes
it a block, so the browser puts a space between it and the number.

A sixth point came up on the way: the themes draw three lists without markers (a ranking, the progress steps and the
error summary), and WebKit stops announcing a list drawn without markers as a list unless it says it is one.

## Options

**A ranking's moves:**

1. Visible words such as "Up" and "Down": new texts for every visitor language (seven), and words beside every option
   where a symbol says it more calmly.
2. The symbol inline in the markup (an SVG): every theme then gets the drawing whether it wants it or not, the default
   theme's icon rules would have to change, and the markup grows for every option.
3. A Unicode arrow in a pseudo-element: a glyph standing in for an icon, which the design skills refuse (the craft
   floor), and it renders differently in every font.
4. **The base rules draw the two arrows**, with the icons the default theme already uses, and a theme draws its own
   over them.

**A rating's points:**

1. Stars in the base rules too: a theme's symbol (a rating may be stars, hearts and more) would become every theme's,
   and a site writing its own CSS would have to undo it.
2. **The number shows**, which the label holds anyway, and a theme that draws the symbols hides it from sight.

## Decision

- **Every control shows what it means without a theme.** The views and the base rules carry what a control needs to
  read; a theme adds its look and may draw symbols in place of words it then hides from sight, never the other way.
- **A rating's points** hold their number in `<span class="jfb-rating-value">`, visible. The default theme hides
  `.jfb-rating-value` from sight, since its stars show the number, and keeps its stars side by side.
- **A point with a word at its end**, a rating's or a scale's first or last, says its whole name in one visually
  hidden span ("1, Poor", "0, Not likely") and keeps the shown number from screen readers (`aria-hidden`), so no
  browser puts a space before the comma; a point between the ends is named by its number alone.
- **A scale reads along a line** in the base rules: a scale's or a rating's points in a row that wraps, and the words
  at its ends (`.jfb-ends`) under its two ends. The markup puts a space between the two words, so they stay apart
  where no stylesheet applies at all, such as a reader view.
- **A grid's column heads** (`.jfb-matrix-head`) are hidden by the base rules; a theme that lines the columns up shows
  them, as the default theme does on a form wide enough.
- **A ranking's places once:** the base rules draw the ranking without the list's own markers. **Its moves** show the
  up and down arrows in every theme: the base rules give `.jfb-icon-up` and `.jfb-icon-down` Lucide's two chevrons as
  a mask in the text's colour, and `ButtonText` in forced colours. The base stylesheet therefore starts with Lucide's
  ISC notice in a `/*!` comment, which the build keeps, and THIRD-PARTY-NOTICES names it.
- **Lists say they are lists:** the ranking's `<ol>`, the progress steps' `<ol>` and the error summary's `<ul>` carry
  `role="list"`.
- **The markup contract stays at version 1:** nothing has been released, so the views change in place; a release
  that changes a view raises the version, as ADR-0015 says.
- **The default theme does not change:** the specimen's screenshots of 6.3b, light, dark and right to left at 375 and
  1440 px, are pixel for pixel the same with the new markup and base rules (the shots with errors differ from run to
  run by where the errors' fade-in stands, so the empty ones, which hold every control, are the measure).

## Consequences

- A site that takes `unstyled` gets a form that reads and works as it is, which it then styles from the classes; the
  base rules grow to 1.4 KB gzip of their 10 KB budget, the licence notice included.
- A theme that draws a rating's symbols must hide `.jfb-rating-value` from sight; one that lines a grid's columns up
  shows `.jfb-matrix-head`; one that draws its own arrows overrides the mask. `bootstrap5` (6.3d) draws stars
  and the column heads and keeps the base arrows ([ADR-0108](0108-the-bootstrap5-theme.md)).
- An error under a field reads as plain text in `unstyled`: the text names the problem and the error summary lists
  it, which is what the markup owes; colour is a theme's.
- **Revisit** if a field type gains a control whose meaning only a symbol carries: it needs a visible fallback in the
  markup or the base rules before it ships.
