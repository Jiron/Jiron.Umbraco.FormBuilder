# 0216. A smaller backoffice bundle: template text and fewer chunks

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

The bundle was also 230 files, a hundred of them under 1.5 KB: every module the backoffice loads on demand (a
repository, a store, an action, a dialog's token) became a chunk of its own, and each carries the licence banner,
about 165 bytes gzip, 38 KB in all, besides compressing worse alone than with its neighbours.

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
5. **Fewer, larger chunks** (the bundler's code-splitting groups, `packages/backoffice/vite.config.ts`): every module
   of the package's own that the backoffice's start imports goes into one chunk, `start`, and the modules each folder
   of `src` loads on demand into chunks of that folder, split where one would pass 160 KB before compression, so no
   file nears the 64 KB budget of one. The words of each language and the generated client keep their chunks: a user
   loads one language's words, and the client is shared by every folder. A screen now loads its folder's on-demand
   modules with it, a few kilobytes it may not use yet, for far fewer requests.
6. **The modules run in their source order** (the bundler's `strictExecutionOrder`). Chunks made by folder, not by
   what imports what, import each other both ways, and a chunk can then run before the one that defines a class it
   extends: the first build without it stopped the whole section with "Class extends value undefined". In this mode
   each module runs when it is first imported, as it would unbundled, wherever its chunk puts it.

## Consequences

- The template text made the bundle 482.89 KB, 22.76 KB less, and its largest file 33.80 KB, 5.13 KB less.
- With the themes' screens added, the groups made it 407.69 KB in 47 files instead of 504.08 KB in 230, the start
  39.59 KB instead of 45.60 KB, and the largest file 25.45 KB.
- The source order costs the wrappers that run each module once: with the email layouts' screens added as well, the
  bundle is 430.99 KB in 50 files, the start 42.13 KB and the largest file 26.06 KB. The browser tests are what show
  an order a chunk breaks, as every screen of the section then fails to draw; the unit tests run the sources.
- Prettier's line breaks inside an `html` template no longer show in a `pre-line` box of the built backoffice; the
  sources still keep text that such a box shows on one line, as the tests read the sources.
- **Revisit** if a template needs its whitespace in a way the three exceptions miss: the exception goes in the plugin,
  with a test; and if a folder grows so large that loading it whole costs a screen more than its requests saved.
