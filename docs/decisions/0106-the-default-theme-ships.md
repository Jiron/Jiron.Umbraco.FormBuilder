# 0106. The default theme ships

- Status: accepted; builds [ADR-0022](0022-default-theme-and-tokens.md)'s default theme on
  [ADR-0105](0105-the-theme-registry-and-the-base-rules.md)'s registry, amends ADR-0022's layout (fields side by side
  share their rows) and ADR-0105's base rules (the honeypot is clipped, not moved), and extends
  [ADR-0015](0015-themes-and-the-markup-contract.md)'s dark scheme (the class may sit on an ancestor); hides a
  rating's numbers, which [ADR-0107](0107-markup-that-reads-without-a-theme.md) shows in 6.3c, behind its stars
- Date: 2026-10-30; amended 2026-10-31 (6.8d: a refused rating's stars and a refused scale's points)
- Roadmap step: 6.3b (decided and built); 6.3d gives `bootstrap5` its stylesheet under the same rules
  ([ADR-0108](0108-the-bootstrap5-theme.md)), 6.4a the error summary's punctuation (built), 6.4l the ground check,
  12.2 the library themes built on `default`

## Context

Step 2.3 drew the default theme as mock CSS (`docs/ux/website/assets/theme.css`), checked by the mock checker and
reviewed again in 2.4; ADR-0022 fixed its tokens and its look. Step 6.3a built the registry, the class map and the
stylesheets a page writes, each theme's once, with `default` still rendering on the base rules alone. Shipping the
mock as the `default` theme left these questions:

- **Another theme on the same page.** Every rule of the mock sits under `.jfb-form`. ADR-0105 writes the stylesheet of
  each theme a page uses, so on a page with a `default` form and a `bootstrap5` or `unstyled` form, the default
  stylesheet would style the other form too.
- **Opting into the dark scheme.** ADR-0015 opts in with `jfb-scheme-dark` or `jfb-scheme-auto` on the form, but no
  setting writes that class yet (the theme editor comes in Phase 12), so a site could only get it through a theme of
  its own or a view override, and the demo could not show it at all.
- **The theme's token defaults**, which `IFormBuilderTheme.Tokens` declares for themes built on it.
- **What the real markup showed** that the mocks did not: Edge screenshots of a form with a field of every type found
  three defects (below), and a form whose direction differs from its page's found one in 6.3a's base rules.
- **The icons' licence**, which ADR-0022 asked to keep in the shipped CSS.

## Options

**Scoping the default stylesheet:**

1. Keep `.jfb-form` and accept that a page with two themes mixes them: wrong as soon as a site shows two themes, which
   ADR-0105 supports on purpose.
2. Prefix every selector with `.jfb-form.jfb-theme-default`: right, but every rule gains specificity, so a site's
   `.jfb-form .jfb-control` loses where it won before.
3. **Nest every rule under `.jfb-form:where(.jfb-theme-default)`**: only the theme's forms match, and `:where()` adds
   nothing to the specificity, so the cascade is the mock's.

**Where the scheme class may sit:**

1. On the form only, as the mock has it: no site can opt in without code until the theme editor.
2. **On the form or on any ancestor**, as the tokens may (ADR-0015): a site opts in with one class on its page's root,
   or on a section with a dark ground, without JavaScript and without code.

## Decision

- **Each theme's stylesheet styles only its forms.** `packages/themes/src/default.css` is the mock ported rule by rule,
  every rule nested under `.jfb-form:where(.jfb-theme-default)`; only `@keyframes` stay at the top level, where they
  must. A later theme's stylesheet nests under `.jfb-form:where(.jfb-theme-<alias>)` the same way (6.3d). A site's own
  theme that builds on the default stylesheet adds `jfb-theme-default` to its form slot through its class map. The
  base rules keep `.jfb-form`, since every form takes them.
- **The lint.** `scripts/lib/stylesheets.mjs` holds the stylesheet lint the mock checker had: a top-level selector that
  does not start with the stylesheet's scope, and anything physical (left or right in a property name or as a float,
  clear or text-align value, a four-value shorthand whose sides differ, a border-radius with different corners). The
  size check runs it on every stylesheet as it is served, after Lightning CSS: the base rules against `.jfb-form`,
  each theme's against its own scope; the gate fails on either.
- **The dark scheme** applies with `jfb-scheme-dark`, or `jfb-scheme-auto` while the visitor's system asks for dark, on
  the form or on any ancestor. Without a class anywhere the form takes the light tokens; 6.4's ground check counts an
  ancestor's class as the site's choice too.
- **The token defaults** are the public tokens of `docs/ux/tokens.md` with their light values; the radius steps are the
  expressions they derive from (`calc(var(--jfb-radius, 12px) * .75)`); `--jfb-font-family` and `--jfb-font-size` have
  none, since the form takes the site's; `--jfb-scroll-margin` belongs to the base rules, not to a theme.
- **Fields side by side share their rows.** In a group of two to four columns on a form at least 480 px wide, each
  field takes eight rows of the group's grid (`grid-template-rows: subgrid`) and fills them in its own order; a label
  without help takes two, so every control starts on the third row and the controls of a row line up, whatever the
  labels and help above them hold. The rows meet without a gap; each field's padding spaces the rows of fields. This
  amends ADR-0022's layout, where a longer label or a help text pushed one control below its neighbours.
- **An option's description** sits right under its label: the option's grid had kept the flex row's gap as a row gap,
  which put the description nearer the next option than its own.
- **The honeypot** is clipped to nothing where it stands (one pixel, `clip-path: inset(50%)`), as visually hidden text
  is, instead of moving to the start side: on a form whose direction differs from its page's, the start side is the
  side the page scrolls to, and the page grew 10,000 px wide. It stays out of view and, through `aria-hidden`, out of
  the accessibility tree; nothing in it is visible and nothing shows where it stands.
- **The icons' licence.** The default stylesheet starts with Lucide's ISC notice in a `/*!` comment, which Lightning
  CSS keeps, so every served copy carries it; THIRD-PARTY-NOTICES lists the icons under "Shipped to sites".
- **The demo** shows the theme on a specimen page (`/en/specimen/`, `/de/muster/`): a form with a field of every
  built-in type, a picture of its own for the image field and the picture choices, and a `?scheme=dark|auto` switch
  that gives the demo site its dark design and its page's root the scheme class, kept in a cookie like `?theme=`.

A refused rating's stars take the error's colour until one is chosen, and a refused scale's points the error's border,
keyed on each choice's own `aria-invalid`, so a scale with words at its ends, whose choices sit a level deeper, shows it
too (6.8d). In forced colours the stars keep the system's text colour, and the error's words say it.

## Consequences

- A page may show forms of several themes, and each looks as its theme draws it; the default stylesheet weighs
  8.0 KB gzip of its 10 KB budget, the nesting included.
- A site opts into the dark scheme without code: `<html class="jfb-scheme-auto">` is enough, and the theme editor of
  Phase 12 writes the class on the form for a library theme.
- The specimen's review (`docs/ux/review-6.3.md`) found one defect that is not the theme's: the error summary writes
  "Where would you like to sit?: Choose an answer" after a label that ends in a question mark. The summary entry is
  shared logic of C# and TypeScript with vectors in every visitor language, so 6.4, which reworks that logic for the
  runtime, takes it. **Built in 6.4a:** a label whose last character that cleaning keeps is a question mark, an
  exclamation mark, a full stop or a colon (or the ellipsis, or one of the Greek, Arabic, ideographic or full-width
  forms) takes the words' marked entry, `{label} {message}` in English and German, in both runtimes, with vectors and
  mutants; a row's legend keeps its colon after the bracket.
- **Revisit** if browsers make `@scope` reliable enough to scope a theme without the nesting, or if a field type needs
  more than eight rows side by side.
