# Step 10.3d: the publish dialog says what changed

ROADMAP 10.3d has the publish dialog say what changed since the version visitors get, grouped, with the changes not
saved yet, or, on a first publish, what the first version holds
([ADR-0202](../decisions/0202-the-publish-dialog-says-what-changed.md), building on ADR-0196 and ADR-0200):

- a form published before lists, under **Since the published version**, History's parts (fields, settings, actions,
  rules): what was added, removed or moved, and each changed element once with what changed of it, the first three
  lines of each part and how many more;
- a form taken offline is compared with the last version visitors got, and says so;
- a form never published says what its first version holds; a draft as published says nothing changed.

Per ADR-0019 and ADR-0020 the three design skills shaped it before it was built and ran again on the result; the design
comes from Phase 2 ([information-architecture.md](information-architecture.md), section 5.7, and [copy.md](copy.md)).
The owner answers no questions during the run (CLAUDE.md rule 8), so every choice the skills would have asked about was
decided here or in the ADR.

## What was looked at

Before building, the dialog showed the form check's result, a sentence and the comment; IA 5.7 asks for what changed
first, and History already said a comparison in words, with a row for each property and both values, too much for
Umbraco's small dialog. After building, the dialog was looked at in Edge at 1440 and 375 px, light and dark, English
and German: a first publish says what its first version holds in one sentence; a changed form lists its fields' part
under the headline, three lines and "and 2 more", between the check's note and the sentence above the comment, and on
a phone nothing is squeezed.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for a designer about to make a form live who wants
to know, in a glance, what visitors will get that they don't get now. Dials 2 / 1 / 5, as for 10.1a to 10.3c. What it
asked held:

- **One look:** Umbraco's dialog; the check's note as it was; the changes as plain text, a quiet label over each part
  and a bullet per line, as History's comparison lists its sentences.
- **Every state composed:** comparing, the parts, one sentence (a first version, nothing changed, can't be read), and
  none while an error blocks publishing.
- **Pre-flight:** no emoji, no gradient, no motion of its own; every string in copy.md's voice, "the published
  version" as the chip says it, never "live".

## impeccable

Operate mode, a refinement inside Umbraco's design system. The bundled engine's `detect` found nothing in the dialog
or its words (`[]`). The critique it guides kept the dialog's order: what blocks (the check's errors) first, then what
changed, then the sentence and the comment, so the first thing a blocked publish shows is what to fix; and it kept the
part's name quiet (muted, not bold), as the headline already says what the list is.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| a sentence and a comment box: publishing blind | what changed, grouped, above the sentence | the moment before going live is when a person wants to know what goes live |
| History's rows, a property each with both values | each changed element once, with what changed of it | a decision needs the what, not every value; History keeps them all |
| every line of every part | three lines a part, then "and 2 more" | the dialog stays a dialog; the count says nothing is hidden by surprise |
| — | the answer in a status region that stays in place | the comparison comes after the dialog opens; a screen reader hears it, sight sees no jump of the buttons beyond the lines themselves |

## The words

English and German for every string, in `lang/history.*.ts`, which the publish flow loads before the dialog opens, so
its first frame is in words. copy.md has each of them.

## A reader's review

A reader who hadn't built it read the part against IA 5.7, copy.md and ADR-0202. It found no defect in what is
compared or how it is grouped: the draft as it is now, unsaved changes included; first publish told right after an
unpublish; nothing left waiting. What it found, and what came of it:

| Found | Done |
| --- | --- |
| a second click while History's words load opened a second dialog on a slow first publish | one dialog at a time for a workspace, while the words load and while it is open |
| the status region read the whole list, over a dozen lines, beside the check's own region | the region says one sentence: which parts changed, or the sentence in their place; over the parts it is for a screen reader alone, and the parts follow outside it |
| "Comparing with the published version…" and its failure sentence on a form that has none | "Reading the draft…" there, and a failure sentence that names no version; an offline form with nothing changed says "since the last published version" |
| "and 2 more" beside the check's "And 2 more, which the form check lists." | "And 2 more.", a sentence as the check's |
| a line of names had no end | five names at most, then "3 more" |
| the first version's checks counted as rules, where the glossary keeps them apart | "2 rules, 1 check" |
| a second localization controller on the same host took the first one's place, History's own among them | the flow says its words through its caller's controller |
| the comparison's code loaded with every workspace's Publish… action; an offline form read 20 versions | the code loads with the dialog; the offline form reads one |
| mocks that ignored their arguments; no test of the flow's changes, of a dialog that hides its changes when an error comes in, of real plurals | the mocks record what is sent, read and listed, and each of those has a test; the journey covers a form taken offline, and the form check's journey a blocked dialog |
| a moved and changed field named on two lines; an outcome named by its content when moved and by its place when changed | kept: two facts, as History says them, in History's words |

## The tamper round

Each tamper takes back one rule of the part and must make a named test fail; "caught" needs a failed test. In a
throwaway clone of the commit before the part, with the part's files copied in and the base runs passing (39 client
tests): 22 tampers of the words, of what the dialog is told, of the flow and of the dialog.

- 21 were caught at once.
- One was missed and is caught now: the flow's fallback for a comparison that fails on its way, its code not loading
  among it, had no test; the fallback now covers the whole chain, and a test has the comparison fail and the dialog
  say so.

## What the journeys found

`tests/e2e/specs/backoffice/publish.spec.ts` publishes a form in Edge: its first publish says "The first version holds
1 page and 2 fields."; after a field added elsewhere and a label changed and not saved, the dialog lists, under "Since
the published version", the fields' part with "Field added: Phone" and "Full name: Label", passes axe, and what goes
live holds exactly those labels; published as it is, it says nothing changed; taken offline, it compares with the last
published version. The form check's journey sees the blocked dialog say nothing of the changes. The designer's journeys
and the restore-and-publish journeys (20 in all) pass with the dialog's new part.

The first run found the journey's own race: it read the version visitors get before the publish that follows the save
was done; it now waits for it.

## The checks

On the working tree: the formatters (`scripts/fix.mjs`), the client's typecheck, lint and build, the size check (the
backoffice at start 42.01 KB of 64 KB, its largest file 45.20 KB of 64 KB, the whole 381.90 KB of 512 KB), the whole
client suite (5365), the journeys' typecheck, the ASCII and name scans, the journeys of publishing, the form check, the
designer and History's restore and publish (20), their look in Edge (light and dark, 1440 and 375 px, English and
German), and the tamper round above. No server code changed. The whole suite, run near midnight, found an older test
of the Actions view whose words depend on the hour (a next try after midnight is "on Nov 7"): it now runs at a fixed
midday.
