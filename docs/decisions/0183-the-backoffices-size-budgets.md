# 0183. The backoffice's size budgets: what every session loads at its start, any one file, and the whole

- Status: accepted
- Date: 2026-11-05
- Roadmap step: 9.6b; builds on [ADR-0026](0026-client-workspace-and-asset-delivery.md) (the client workspace and its
  size check)

## Context

The size check has reported the backoffice bundle since 3.2 without a budget, and 9.6 sets one "from the measured size
plus a stated margin" (ROADMAP §4.12). Measured on `e67836f`, gzip at level 9 as the size check measures:

- **The whole bundle:** 106 files, 246.76 KB. No session loads it together: Umbraco loads the package's entry at every
  backoffice start, and each screen loads its own chunks when it opens.
- **The start:** the entry and its static imports, 14 files, 15.26 KB, in every section, Content as much as Form
  Builder. With them come the words: Umbraco's localization registry loads, at start, every package's words in the
  user's language and always those of its default culture, English, as the fallback for a missing word. English is
  18.48 KB, German 20.73 KB, so a German user loads 54.47 KB of the section at start and an English one 33.74 KB.
- **The largest file:** the Design view's chunk, 55.14 KB, loaded when a form opens.

The plan still adds the backoffice's settings, actions, preview and history (Phase 10), the entries, exports and
insights (11), the global settings (12), the privacy tools (13) and the language and member screens (14): each about
one more family of screens, which a session opens one at a time.

## Options

1. **One budget on the whole bundle,** the measured size and a fifth more. The next phases' screens would cross it by
   plan, and a budget raised at every phase holds nothing.
2. **One budget on the whole with room for the plan** (512 KB). It catches a library bundled instead of taken from
   Umbraco, but never a start that grows for every Umbraco user, or a screen that grows heavy to open.
3. **Budgets on what people pay:** what every session loads at its start, any one file a screen loads, and the whole
   with room for the plan.

## Decision

Option 3, enforced by `scripts/size-check.mjs` in the client stage of `ci-local` and listed in §4.12:

- **At start ≤ 64 KB gzip** (54.47 KB measured): the entry, its static imports, and the words of English and of the
  largest other language. Every Umbraco user downloads it at every backoffice start. The 9.5 KB of room is for the
  menus, trees and words the next steps add; once the words outgrow it, a screen's words are registered as the screen
  loads (Umbraco's localization manager takes words at any time, `registerManyLocalizations`), and only the words of
  what any screen may show stay at start: the section's menus, trees, actions and the dialogs opened from them.
- **Any one file ≤ 64 KB gzip** (55.14 KB measured, the Design view's): what opening a screen costs grows by chunks of
  the screen's own; a file past it is split, as the canvas and the inspector of the Design view can be.
- **The whole bundle ≤ 512 KB gzip** (246.76 KB measured): the room is about the size of the backoffice still to come,
  so the cap catches what no plan holds, above all a library bundled instead of taken from Umbraco.

## Consequences

- An Umbraco user who never opens the section pays at most 64 KB gzip for it at each start.
- Moving a screen's words to load with it is the known next move when the start fills, not a surprise; its test then
  holds that every word a screen shows is registered before the screen draws.
- The size check's tests hold each budget: what counts at start (Umbraco's own modules and a screen's chunk not), the
  language of a words file, and each budget failing alone.
- Rejected: options 1 and 2 alone, and a budget per screen by name, which every new screen would have to add.
