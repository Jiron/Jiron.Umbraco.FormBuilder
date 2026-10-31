# 0119. A terser pass for the website runtime

- Status: accepted; amends [ADR-0026](0026-client-workspace-and-asset-delivery.md) (how the website runtime is
  minified); [ADR-0116](0116-the-runtimes-budget-counts-one-language.md)'s measure of the budget stays
- Date: 2026-10-30; amended 2026-10-31 (6.8b: the room its fixes needed; 6.8c: its marks within the budget; 6.8g: the room its fixes needed)
- Roadmap step: 6.4h (decided and built); every later step of 6.4 is held to the same budget

## Context

Section 4.12 gives the website runtime 40 KB gzip with every feature, and ADR-0116 left 0.07 KB of it after 6.4g.
6.4h's defaults filler and the merge tags' refused paths came to 40.57 KB, and `docs/OWNER-TODO.md` asks the owner
whether the budget should grow; until the owner answers, each step makes room for what it adds. The bundler minifies
with its own minifier (oxc): every name is mangled already, and the code that fills the form chunk is the logic both
runtimes share (D10), with nothing spare to cut without changing what the vectors hold.

## Options

1. **Cut code.** The largest modules are the messages, the reading, the checks, the rules and the patterns of
   `packages/logic`, each held to Core by the vectors; trimming them would weaken what both runtimes share.
2. **Leave the budget broken** until the owner decides: a failing check, which the roadmap does not allow.
3. **A second minifier.** Terser over the bundler's output takes about 1.6 % off: 0.65 KB of the runtime with every
   feature. It is a build tool only, BSD-2-Clause, maintained for years, and nothing of it reaches a site. Chosen.

## Decision

- `scripts/vite-shared.mjs` gains `terserPass()`, a plugin that runs terser on each chunk as it is rendered, before
  the bundler's own minifier, so a chunk's content hash covers what it writes: an ES module of the build's target, three
  compress passes, names mangled, and legal comments kept (the licence banner is added after both anyway).
- Only the website runtime takes it (`packages/runtime/vite.config.ts`): its size is what section 4.12 budgets and
  what every visitor downloads. The backoffice and the element keep the bundler alone.
- A debugging build with source maps (`JFB_SOURCEMAP=1`) goes without it, as terser would cut the maps' thread.
- `terser` 5.51.2 is a pinned dev dependency of the client workspace; `scripts/check-npm-licences.mjs` reads it and
  its dependencies (MIT and BSD) as dev-only.

## Proof

`scripts/scripts.test.mjs` builds a chunk with and without the pass: with it the chunk writes no more, keeps the
banner and runs as before, and a build with source maps skips it. The size check: the runtime with every feature is
39.89 KB gzip of 40.00 after 6.4h. The browser projects run the built runtime.

## Consequences

- Every page that loads the runtime downloads a little less.
- It is a one-time gain: the features 6.4i to 6.4o add still need room or the owner's answer on section 4.12's budget
  (`docs/OWNER-TODO.md`).
- **Revisit** when the bundler's own minifier closes the gap: then the pass costs build time for nothing.

## The room 6.8b made (2026-10-31)

6.8b's fixes came to 40.21 KB. Terser's other options (more passes, `unsafe_arrows`, `pure_getters` and the like) took
off about 30 bytes, so they stay as they are. The room came from the code: the runtime's own copies of a walk over every
field and of the built-in test are one each in `page.ts` (`allFieldsOf`, `builtIn`); `wordsFor` drops a throw no
culture reaches; and the country codes are written as their letters two by two (`.match(/../g)`), about 90 bytes, which
`CountryCodesTests` still holds to Core's list. A generic element maker for the error summary's fallback gzipped worse
than the calls it replaced and was left out. The runtime is at 39.94 KB.

6.8c's marks on a group's choices came to 39.99 KB, within the budget; one look-up of a field's fieldset instead of two
brought it to 39.98 KB. Twenty bytes are left, so the next step that adds to the runtime makes its room first.

6.8g's fixes came to 40.01 KB. A walk over the row state written twice is one (`rowOf`), a field hidden or shown is one call (`hide`), `showFieldError` clears first instead of calling itself, and the results waiting to be said are one `WeakSet`, whose `delete` also says whether a result is among them. A helper for the trimmed text of an element gzipped worse and was left out. The runtime is at 39.99 KB.

6.8h landed a summary's link on the control itself, which took the label's look-up away: 39.96 KB. 6.8i brought the look-up back, as a long legend or a grid's stood under a sticky header that way, with a plain focus bringing in a control still out of view.
