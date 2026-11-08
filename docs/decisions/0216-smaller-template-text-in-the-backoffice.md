# 0216. Smaller template text in the backoffice bundle

- Status: accepted; amends [ADR-0026](0026-client-workspace-and-asset-delivery.md) (how the backoffice is minified);
  section 4.12's budgets and [ADR-0183](0183-the-backoffices-size-budgets.md)'s measure stay
- Date: 2026-11-08
- Roadmap step: 12.1, its part 12.2 (decided and built); every later step is held to the same budget

## Context

Section 4.12 gives the whole backoffice bundle 512 KB gzip. After 12.1's option lists it was 505.65 KB, and 12.1's
themes and email layouts and its templates, and the screens of 12.4 and later, still have to fit. The bundler's
minifier leaves the text of a template literal as written, so each Lit `css` template ships its comments and its
indentation, and each `html` template the line breaks and indentation its formatting gives it: in the source, about
31 KB gzip of CSS text that is 18 KB without them, and 41 KB of HTML text that is 35 KB. A terser pass, which the
runtime takes (ADR-0119), saves 0.9 KB here, as it does not touch template text either.

## Decision

1. **A build plugin, `lessTemplateText` (`scripts/templates.mjs`),** runs before the bundler compiles the backoffice's
   own modules. TypeScript's parser finds each template tagged `css` or `html` exactly, so strings, untagged templates
   and other tags are never read as templates.
2. **A `css` template** loses its comments; each run of whitespace becomes one space, and the spaces around braces and
   semicolons go, as do those at its start and end. Every other space stays, as it can part a selector's compounds
   (`:host ::slotted`) or a calculation's terms (`calc(1px + 2px)`).
3. **An `html` template** has each run of whitespace that holds a line break made one space, which the browser reads
   the same outside preformatted text. A template that holds `<pre`, `<textarea` or `white-space` is left as written,
   and so is a backslash's line continuation.
4. **Not in a debugging build** with source maps (`JFB_SOURCEMAP=1`), as its changes have no map, and not in the tests,
   which run the sources; the browser tests run the built bundle, so they see what visitors' and editors' browsers see.

## Consequences

- The bundle was 482.89 KB after this, 22.76 KB less; the largest file 33.80 KB, 5.13 KB less.
- Prettier's line breaks inside an `html` template no longer show in a `pre-line` box of the built backoffice; the
  sources still keep text that such a box shows on one line, as the tests read the sources.
- **Revisit** if a template needs its whitespace in a way the three exceptions miss: the exception goes in the plugin,
  with a test.
