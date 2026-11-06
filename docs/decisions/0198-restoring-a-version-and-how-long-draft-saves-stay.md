# 0198. Restoring a version as a new draft, and how long draft saves stay

- Status: accepted
- Date: 2026-11-06
- Roadmap step: 10.3b; builds on [ADR-0008](0008-settings-model.md) (settings and their locks),
  [ADR-0045](0045-versions-comparison-and-draft-cleanup.md) (the versions and the draft cleanup),
  [ADR-0052](0052-version-endpoints.md) (the restore endpoint) and
  [ADR-0196](0196-the-history-view-and-the-comparison-in-words.md) (the History view); IA 5.6 and 8, copy.md

## Context

IA 5.6 lets a designer go back to an earlier version in three places: the sidebar that compares a version with the
current draft ("Restore as a new draft", and "Restore and publish…"), a History row's "…", which acts at once and
offers Undo, and "Restore an earlier version…", first in the form's "…", which offers the same from every view. A
restore never loses anything: it copies the version into a new draft, and the draft before stays in the History. The
server has done so since 4.5b (`POST form/{key}/versions/{number}/restore`, ADR-0052); nothing in the backoffice called
it.

The draft cleanup (ADR-0045) keeps a form's newest draft saves and those of the last days, by `general.history`, a
setting every site shares. Its values had a default and a service, but no endpoint and no screen: IA 8 puts them in
Settings › General, whose other settings are 12.4a's.

The design skills shaped it before it was built (ADR-0019, ADR-0020): Operate mode inside Umbraco's design system, for
a designer who wants an earlier version back without fear of losing the current one, and for an administrator who
sets how long the History remembers. The owner answers no questions during the run (CLAUDE.md rule 8), so IA 5.6, IA 8
and copy.md answer the skills' discovery questions, and the rest is decided here.

## Options

**What happens to changes not saved yet**

1. Refuse the restore until they are saved.
2. Drop them: the restore replaces the draft.
3. **Save them first, as Publish… does:** they stay in the History, and Undo brings them back.

**Undo**

1. Delete the restored version: the History would lose a version someone may already have compared or published.
2. **Restore the draft that was current before, as a new draft:** reversible in the same way, and the History shows
   both steps (IA 5.6: the draft before "also stays in History").

**Comparing two ticked versions**

1. Restore buttons there too: which of the two would come back is unclear.
2. **Only the comparison with the current draft restores,** which says what the restore changes.

**The settings' endpoint**

1. One endpoint for the History settings alone, beside the one 12.4a writes for the rest of General: two endpoints
   for one section.
2. **`settings/general`, holding the History group now,** which 12.4a grows: a save writes the groups it sends and
   keeps the rest of the section as it is.

## Decision

Option 3, 2, 2 and 2.

- **Restoring** (the History view, `designer/history/`):
  - the sidebar that compares a version with the current draft has **Restore as a new draft** as its primary action and
    **Restore and publish…** beside it, with Close; the comparison of two ticked versions has Close only;
  - a row's "…" starts with **Restore as a new draft**, except on the current draft's own row, and acts at once;
  - **Restore an earlier version…**, first in the form's "…" in the workspace only (not in the tree, where the form
    isn't open), opens a medium sidebar listing the versions by date, newest first, with the same Show draft saves
    switch; choosing one opens the same comparison on top of it, with the same two buttons, and a restore closes both.
- **A restore** saves the changes not saved yet first (a refused save stops it, and says why), restores, and reads the
  form again, so every view shows the new draft; the sidebar says beforehand that changes not saved yet are left out
  of its comparison and saved first. A toast names what changed between the draft before and the new one, as the
  History's summary says it, joined as the reader's language joins a list (the dictionary's own words, so English has
  no comma before "and" and German sets off „Nach dem Senden“): "Version 13 is the new draft: 4 fields, After sending,
  1 rule and 1 action changed." **Undo** restores the draft before as a new draft and says so, but only while the
  draft is still the one the restore made, in the same form, with nothing unsaved: a change since would be lost, so
  Undo then says the draft before stays in History. A refused restore (an action changed without `manageActions`, a
  sensitive field without `viewSensitive`, someone else's save, or a version the cleanup removed meanwhile) is said by
  the backoffice's notification, and nothing changes.
- **A restore holds to its form** (`designer/history/restore-draft.ts`): where the workspace holds another form before
  the server is asked, it stops; after, the restore stands, and that other form's views and toasts are left alone.
  The new draft's number is the server's answer, and a read again that doesn't show it says so, without Undo. One
  restore runs at a time: a second click would be refused as a change made meanwhile.
- **Restore and publish…** restores, with the same toast and Undo, then opens the publish dialog for the restored
  draft; cancelling it leaves the restored draft. From Restore an earlier version…, both sidebars close first. The
  dialog's flow is the one Publish… uses, shared, not copied. The live version doesn't offer it: visitors get that
  version already, and publishing a draft equal to it adds nothing, so its sidebar has Restore as a new draft and
  Close. Where the sidebar's buttons don't fit one row they wrap, and a narrow sidebar stacks them, the primary first.
- **The endpoint** `GET settings/general` answers the section's version, the History group (`draftDays`,
  `draftCount`), their limits from the code (1 to 3,650 days, 0 to 1,000 saves), and the host's locks by field;
  `PUT settings/general` saves the groups it sends with the version it read. Both need `manageSettings`, held
  globally. A locked section answers `409` with the key that locks it; a value out of range `400` with its field; a
  save made meanwhile `409`. The groups sent change the stored values as the save reads them (the settings service's
  `UpdateAsync`), not a server's cached copy, so the fields a save leaves keep their stored values also while another
  server's save is still on its way to this one's cache (ADR-0008).
- **Settings › General** (`settings/general-settings.element.ts`) replaces its "coming" screen: one box, **History**,
  with a sentence on what is kept for good, then **Keep draft saves for** (days) and **Always keep the newest** (draft
  saves), each with its help below it, and **Save** in the footer. It has no title of its own (the sidebar names it,
  ADR-0021), and no site switch until 12.4a adds the first setting a site can have of its own. Each number's unit is
  read with it. A locked section shows one line naming the key, the fields read-only and no Save; a field's error from
  the server shows under it, and one of a field the screen doesn't show is said as a save that failed; a save made
  meanwhile says so and shows the values saved; a user without `manageSettings`, coming by a link, is told which
  permission it needs, without Try again.

## Consequences

- A designer can go back to any kept version from three places, and every restore can be undone.
- The draft cleanup's settings can be changed without host configuration; the cleanup reads them on its next run.
- `settings/general` has its rows in the permission matrix and the OpenAPI snapshot; 12.4a adds the rest of General to
  the same endpoint and the same screen, with the site switch.
- A restore adds a version, and so does its Undo; the cleanup lets them go as it lets any draft save go.
