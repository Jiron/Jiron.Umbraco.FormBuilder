# 0202. The publish dialog says what changed

- Status: accepted
- Date: 2026-11-06
- Roadmap step: 10.3d; builds on [ADR-0196](0196-the-history-view-and-the-comparison-in-words.md) (the comparison in
  words) and [ADR-0200](0200-save-conflicts-and-review-both-versions.md) (comparing a draft not saved yet); IA 5.7,
  copy.md

## Context

IA 5.7 asks the publish dialog to say what changed since the live version, grouped as fields, settings, actions and
rules, with the changes not saved yet included, or, on a first publish, what the first version holds. The dialog
already shows the form check's result and an optional comment (ADR-0181). History says a comparison in words, part by
part, with a row for each property and both its values (ADR-0196), and since 10.3c the server compares a definition not
saved yet with a version (`POST form/{key}/versions/{number}/compare`).

A form taken offline has no version visitors get (`publishedVersion` is null), yet it was published before: "first"
can't be read from the live version alone. The dialog is Umbraco's small dialog, read in a few seconds before a click.

## Options

**What the dialog lists**

1. History's whole comparison, a row per property with both values: a table in a small dialog, and more than a
   decision to publish needs.
2. **History's parts in a few words:** what was added, removed or moved as History says it, and each element that
   changed once, with what changed of it ("Full name: Label and Required"); the first three lines of each part, then
   "And 2 more."; a line names five elements at most and counts the rest. The History holds every value once the
   version is published.

**What it is compared with**

1. The version visitors get, or nothing: a form taken offline would read as a first publish.
2. **The version visitors get; for a form taken offline, the last one they got, said so** ("Since the last published
   version"); only a form never published says what its first version holds.

## Decision

Option 2 and 2.

- `history/publish-changes.ts` compares the workspace's draft as it is now, changes not saved yet included, with the
  version visitors get, through the comparison of a draft not saved yet, and says it with History's words
  (`publishWords`, beside `comparisonWords`). A form never published says "The first version holds 1 page, 7 fields,
  2 rules, 1 check and 1 action.", counted from the draft, a check apart from the rules as the glossary keeps them. A
  draft the same as the version says that nothing changed since it; an answer missing says that what changed couldn't
  be read, and that publishing works all the same. A form taken offline reads one version of its History, the newest
  published.
- The publish flow loads History's words before it opens the dialog, so the dialog's first frame is in words, and the
  comparison's code with the dialog; it opens without the changes where the words can't be loaded. One dialog opens at
  a time for a workspace: a second Publish… while the words load, or while the dialog is open, opens none. The flow
  says the dialog's words through its caller's localization controller, as a host holds one, and a second would take
  the first one's place.
- The dialog says "Comparing with the published version…" ("Reading the draft…" where none is published) in a status
  region that stays in place; the answer there is one sentence: what the first version holds, that nothing changed,
  or which parts changed ("Changed since the published version: Fields and Settings."), so a screen reader hears what
  changed without every line. Over the parts the sentence is for a screen reader alone, as the headline and the parts
  that follow it, a definition list, say it to the eye.
- While an error blocks publishing, the dialog says nothing of the changes, as it says nothing of the comment: it says
  what to fix.

## Consequences

- What goes live is what the dialog listed: Publish saves the changes not saved yet first, the ones it compared.
- The words are History's, in English and German, so a property is named as History and the inspector name it.
- Opening the dialog reads the version visitors get and one comparison; a form taken offline reads the newest entry of
  its History list too. The comparison's code loads with the dialog, not with the workspace's Publish… action.
