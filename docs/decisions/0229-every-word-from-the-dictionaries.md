# 0229. Every word from the dictionaries: every key used, and no word in a template

- Status: accepted; amends [ADR-0057](0057-field-types-endpoint-and-availability.md) (the dictionaries' check)
- Date: 2026-11-11
- Roadmap step: 14.1 (part 14.1, backoffice localisation)

## Context

The backoffice's words live in dictionaries, one file per area in English and German, and each step that added a
screen added its keys. Two promises were only partly kept. The dictionaries' check held some areas to the keys their
code uses (the section's, the library's, the designer's, the form check's, the preview's, the pickers', and the
screens that brought their own words check), but not the inspector's, the rules', the merge tags', the Settings view's,
the Actions view's, the History view's, the entries', the insights', the global settings', the export formats' or the
action types' areas; and nothing kept a word from being written into a template, where German would never see it.

Many keys are not written whole in the code: a term function adds the area (`#term('loading')`), and a key is built
of a family and a part (`section_${id}`, `spam_${code}`), the parts named by a list elsewhere, sometimes on the
server.

## Options

**How a check knows the keys the code uses:**

1. **Run the screens and record what they ask for.** Every state of every screen would have to be reached.
2. **Read the sources** (`lang/key-use.ts`), and take each family's parts from the list that names them. Chosen.

**Against words in templates:**

1. **An ESLint plugin from elsewhere.** None reads a Lit template's text and its spoken attributes in the way the
   backoffice writes them, and a new dependency is not worth one rule (CLAUDE.md rule 4).
2. **A rule of our own,** a small file beside the other build scripts, with its own tests. Chosen.

## Decision

1. **Every key is used** (`lang/key-use.test.ts`). An area holds exactly the keys its code uses, read from the sources:
   - a key named whole (`jfbArea_key`, `#jfbArea_key`) anywhere in the backoffice;
   - the rest of a prefix (`jfbArea_part_${...}`) in the file that builds it;
   - in the area's own folders (the inspector's are the designer's, the entries' are the entries' screens and views),
     a literal, a prefix with a literal and a literal with a suffix (`style_${style}` beside `['plain', 'card']`,
     `${prefix}Nowhere`), since those files hand short keys to the area's term functions;
   - each part of a family whose list lies elsewhere, taken from that list: the operators', effects' and the visit's
     parts in the rules, the run, mail and filter states and spam reasons on the server, the settings sections' keys
     and boxes, the retention spans;
   - each key the server serves, from the snapshots its integration tests hold: field, action and captcha types, merge
     tag paths and sources, the export formats, and now the settings sections (`docs/api/settings-keys.json`, written
     by `SettingsApiTests` like the other snapshots).
   Every area is held by one check, here or in its screens' own words check, and a new area without one fails the
   check. Eight keys no code used went (the entries' sort words, its select column, a signature's answer and a
   timeline line).
2. **No word in a template** (`scripts/no-hard-coded-text.mjs`, `jfb/no-hard-coded-text` in the lint for the
   backoffice's sources but its tests, dictionaries and test helpers). A Lit template (`` html`...` ``) holds no letter of
   its own between its tags or in an attribute a person reads or hears (label, aria-label and the other spoken ARIA
   attributes, title, placeholder, headline, alt, description), only what it puts in. Character references, keys inside
   `<kbd>` (the held key comes from the dictionaries), an address in a spoken value (`https://`), styles and comments
   are no words. A word set from code (a manifest's label, a toast) goes through a `#` key or `localize.term`, which
   a review of the sources found everywhere but the header search's tab, which Umbraco shows as written, as its own.

## Consequences

- A key no code uses, and a word typed into a template, fail the client's checks in every area.
- The check is a reading, not a run: a key whose parts both happen to appear in its area's folders passes though no
  screen says it. Planting a key no code uses in each area, in English and German, failed the checks every time: 37
  plantings in the 36 areas, as the email layouts' names sit in two files, the Actions view's holding the built-in
  layouts' names it shows.
