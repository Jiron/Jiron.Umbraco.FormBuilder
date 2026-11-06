# Step 10.3b: restoring a version, and how long draft saves stay

ROADMAP 10.3b lets a designer go back to an earlier version as a new draft in three places, and gives the draft
cleanup's settings their screen ([ADR-0198](../decisions/0198-restoring-a-version-and-how-long-draft-saves-stay.md),
building on ADR-0045, ADR-0052 and ADR-0196):

- the sidebar that compares a version with the current draft has **Restore as a new draft** and **Restore and
  publish…**;
- a History row's "…" restores at once, with Undo;
- **Restore an earlier version…**, first in the form's "…", offers the same from every view;
- **Settings › General** holds how long a form's History keeps draft saves, its first settings screen.

Per ADR-0019 and ADR-0020 the three design skills shaped it before it was built and ran again on the result; the design
comes from Phase 2 ([information-architecture.md](information-architecture.md), sections 5.6 and 8, and
[copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every choice the skills would
have asked about was decided here or in the ADR.

## What was looked at

Before building, the server had restored a version as a new draft since 4.5b (`POST form/{key}/versions/{number}/restore`),
and kept the cleanup's settings in the General section with a default and a service, but no endpoint and no screen.
IA 5.6 and 8 and copy.md answered the skills' discovery questions, among them the restore buttons' words, the toast
with Undo, and the History group's place in General.

The result was looked at in Edge, light and dark, English and German, at 1440 and 375 px: Settings › General with its
values and with a value out of range; the sidebar comparing an earlier version with the current draft, with both
restore buttons; a row's "…" with Restore as a new draft first; the form's "…" with Restore an earlier version… first;
and the earlier versions sidebar. On a phone the German sidebar's buttons didn't fit its footer and cut off the
primary; they now wrap, and a phone stacks them full width, the primary first (below). Everything else held: Umbraco's
own sidebar, footer, menu and toast, the History's row styles in the earlier versions sidebar, the settings' short
number boxes with their units, and the error under its box.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for a designer who wants an earlier version back
without fear of losing the current one, and an administrator who sets how long the History remembers; calm, native,
nothing new to learn. Dials 2 / 1 / 5, as for 10.1a to 10.3a. What it still asks held:

- **One look:** Umbraco's sidebar, its footer buttons (Close, the secondary Restore and publish…, the primary Restore
  as a new draft in the positive colour), its entity menu, its toast; the History's own row and list styles in the
  earlier versions sidebar; the settings boxes as the inspector and the Settings view draw them.
- **Every state composed:** a restore refused, a comparison that can't be read (no restore offered), no earlier
  version, a read that fails (Try again), the History's words that can't load; the settings reading, failing, a value
  out of range, a field the server refuses, a save made meanwhile, a section the host locks.
- **Pre-flight:** no emoji, no gradient, no motion of its own; every string in copy.md's voice.

## impeccable

Run only through its bundled engine (no downloads, hooks or live mode). Its context step loaded PRODUCT.md and
DESIGN.md; its discovery questions were answered from IA 5.6 and 8 and copy.md, as above. Mode: Operate, a refinement
inside Umbraco's design system. What it asked of the part:

- **Say it once:** the toast names what changed once, as the History's summary says it; the earlier versions sidebar
  shows a version's name, chip and moment as a History row does, without what changed, which its comparison says;
  Settings › General has no title over its one box, as the sidebar names it.
- **The right weight:** Restore as a new draft is the sidebar's primary action, Restore and publish… secondary beside
  it, and the live version, which visitors get already, has no Restore and publish… at all; Save is General's only
  button.
- **No dead ends:** a version that can't be read has no restore, with Close; no earlier version says so; a failed read
  has Try again; a locked section names the key that locks it.

`impeccable detect` on the earlier versions sidebar, the comparison, the History view and Settings › General found
nothing, before the reader's review and again after it.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | the restore buttons appear with the comparison, not before | they act on what the comparison shows; buttons that come and go while the reader decides would move under the pointer, so they wait for it once |
| — | no animation of their own: Umbraco's sidebars stack and close as they do | opened a few times a week; the backoffice's own motion keeps the section one product |
| — | the toast stays ten seconds, Undo in it | long enough to reach Undo, as every Undo toast of the package |
| — | the settings' numbers in short boxes with their unit beside them | a number of days reads at a glance; a full-width box for three digits looks unfinished |
| a narrow German sidebar squeezed its three buttons, each label on three lines, the primary cut off | the buttons wrap to rows where they don't fit, and a phone stacks them full width, the primary first | a label on one line reads at a glance, and no action is ever out of reach |

## The words

English and German for every string: the restores and the earlier versions sidebar in `lang/history.*.ts`, loaded with
the History; the form's menu item in the start words, as the menu shows before the History's words are needed; Settings
› General in `lang/general.*.ts`. copy.md has each of them.

## A reader's review

A reviewer with no part in the work read the part's code against IA 5.6 and 8, copy.md and ADR-0198. What it found,
and what came of it, each with a test:

| Found | Done |
| --- | --- |
| Undo and a restore under way acted on whichever form the workspace held by then, as the workspace serves one form after another | A restore holds to the form it began in (`restore-draft.ts`): another form stops it before the server is asked, and after, the restore stands while that form's views and toasts are left alone; Undo of a form left says it can't |
| Undo after a change since restored the draft before over it, so the change was only in History | Undo goes back only while the draft is still the restored one, in its form, with nothing unsaved; otherwise it says the draft before stays in History |
| Two restores in quick succession: the second was refused as someone else's change | One restore runs at a time |
| The new draft's number came from reading the form again, which keeps the old form where the read fails | The number is the server's answer; a read again that doesn't show it says so, without Undo |
| Restore and publish… from Restore an earlier version… left that sidebar open, stale, under the publish dialog | Both sidebars close first, then the dialog opens |
| A refusal of a General field the screen doesn't show marked nothing and said nothing | It is said as a save that failed; a field's refusal also takes the focus there |
| The numbers' units weren't read with them | Each input is described by its unit, then its help |
| German: "Immer die neuesten behalten [50] gespeicherte Entwürfe" broke the sentence; the toast's Nach dem Senden wasn't set off in quotes; "Der Entwurf davor ist zurück" read stiffly | "Immer behalten [50] neueste gespeicherte Entwürfe"; After sending in German quotes within the toast; „Der vorherige Entwurf ist wiederhergestellt." |
| A draft save named in a sentence lacked its closing comma ("the draft save of today, 09:12 is the new draft") | Within a sentence the moment reads "today at 09:12" (German "heute um 09:12") |
| English joined the list with a comma before "and" for an en-US user, unlike copy.md | The dictionary joins the list itself, in each language's way |
| The rows weren't keyed, so after a read the focused "…" could belong to another version | Rows are keyed by version |
| The sidebar compared the saved draft while a restore would first save changes not saved yet | The sidebar says so where there are any |
| A version the cleanup had removed was refused in English only | Said in the user's language, with Choose another one |
| The settings save merged onto a server's cached copy, so with several servers older values of other General fields could be written back | The service merges in the save, onto the row it reads (`UpdateAsync`), and a stale version is refused as before |
| A user without `manageSettings` coming by a link saw "couldn't be read" with a Try again that wouldn't help | The screen names the permission it needs, without Try again |

One finding was looked at and kept: an edit typed while a restore runs is lost when the form is read again. A restore
starts from a sidebar or the History's menu, so the editors are covered or in another view while it runs, which takes
the time of two or three requests.

The look found one more: on a phone, the German sidebar's three buttons didn't fit its footer, and the primary was cut
off. The buttons now wrap where they don't fit a row, and a narrow sidebar stacks them, the primary first. The first
try made the sidebar's host the container, which took the layout's height from the sidebar and left the footer under
the content; the shots showed it, the layout itself is the container now, and a journey checks the buttons' place.

## The tamper round

Fifty tampers, each taking back one rule of the part in a clone of the working tree, with the named check that must
fail: 6 on the server (the endpoint's merge, its locks, limits and permission, and the service's update of the stored
values), 44 on the client (Undo's guards, the restore's steps and its one-at-a-time, the toast's words, the keyed rows,
the sidebar's buttons and note, the earlier versions sidebar and its publish order, the words in a sentence, and
Settings › General's checks, refusals, unit, locks and permission). 49 were caught at once. The one missed took out
the restore's first form check, which a second check after saving covers, except that the workspace would first save
another form's changes not saved yet: the test now asks for a restore while another form with such changes is open,
and catches it.

## What the journeys found

`history-restore.spec.ts` restores from the sidebar and Undoes it, restores from a row's "…" at once, restores from
Restore an earlier version… after a change not saved yet, which Undo brings back, publishes a restored version from
the publish dialog, and checks that the sidebar's buttons sit at its foot, stacked on a phone with the primary first;
axe runs on both sidebars. `general-settings.spec.ts` saves how long draft saves stay and refuses a value out of range
under its box. History's journey from 10.3a runs beside them. What they found on the way:

- The first Restore and publish… journey restored the live version, so the publish changed nothing: the live version
  doesn't offer Restore and publish… now, and the journey restores an earlier one.
- Settings › General's box was an h5 under Umbraco's sidebar, which axe refused as a skipped heading level; it is an
  h2.
- A toast's text is said in Umbraco's announcer too, so the journeys read the toast itself.
- The footer's place, above.

## The checks

The formatters, the client's typecheck, lint, build and its whole suite (5303), the journeys' typecheck, `dotnet
format`, the unit tests (8454), the integration tests of the settings service, the new endpoint, the version
endpoints, the permission matrix and the OpenAPI snapshot (60), the journeys above (7) and the look in Edge, light and
dark, English and German, at 1440 and 375 px (ADR-0194).
