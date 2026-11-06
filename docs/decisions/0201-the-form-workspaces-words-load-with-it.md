# 0201. The form workspace's words load with it

- Status: accepted
- Date: 2026-11-06
- Roadmap step: 10.3c; builds on [ADR-0183](0183-the-backoffices-size-budgets.md) (the backoffice's size budgets),
  [ADR-0188](0188-the-actions-view.md) and [ADR-0196](0196-the-history-view-and-the-comparison-in-words.md) (the
  Actions and History views load their own words)

## Context

ADR-0183 holds what every backoffice session loads at its start, in any section, to 64 KB gzip: the bundle's entry,
its static imports, and the words of English, which Umbraco always loads, and of the largest other language. It
measured 54.47 KB. Umbraco's localization registry loads every word a package registers at start, so each new screen's
words grew the start for every Umbraco user, Content editors among them, who never open a form. With 10.3b the start
stood at 64.69 KB, over its budget; 10.3c's dialog added 0.61 KB. Nothing between the milestones measures it (ADR-0194),
so the size check found it in 10.3c's checks.

The Actions and History views already load their words when they open, so the start doesn't grow by them. Four more
areas are only ever shown inside the form's workspace: the inspector, the rules, the form's Settings view and the form
check, about 15 KB of English and 17 KB of German.

## Options

1. Raise the budget: every Umbraco user's start grows with every screen.
2. Load each area with the screens that show it, as History does: the check's words alone are used by five screens,
   each of which would have to wait for them, and a missed one shows keys.
3. **Load the four areas once, before the form workspace's editor loads:** everything that shows them draws inside it,
   so one wait covers them all.

## Decision

Option 3.

- `lang/workspace.en.ts` and `workspace.de.ts` gather the inspector's, the rules', the Settings view's and the form
  check's words; the start's tables (`en.ts`, `de.ts`) no longer hold them.
- `designer/workspace-words.ts` loads them for a user's language, English always and German beside it for German, once,
  and registers them with Umbraco's localization manager; the workspace's route waits for them before it loads its
  editor, and opens without them if they can't be read. The loader is the one the Actions and History views use
  (`wordsLoader`).
- A test holds the four areas out of the start's tables, and holds every file outside `designer/` to asking for none of
  them, so no screen outside the workspace can show their keys.

## Consequences

- The start measures 42.28 KB, room for the screens Phase 11 and 12 add.
- Opening the first form of a session waits for the workspace's words, one small file (gzip) per language, read once.
- A new area only the workspace shows goes into the workspace's tables; one shown anywhere else stays at the start.
