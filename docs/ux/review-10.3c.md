# Step 10.3c: conflicts, and the guard against leaving unsaved

ROADMAP 10.3c makes a save that someone else's save got to first a conflict the user resolves, never an overwrite, and
asks before leaving with changes not saved ([ADR-0200](../decisions/0200-save-conflicts-and-review-both-versions.md),
building on ADR-0002, ADR-0045 and ADR-0196):

- a save refused because someone saved meanwhile says who saved, and when, and that the user's changes are still here,
  in Umbraco's dialog, with **Review both versions**; every later Save is refused the same way until the review;
- **Review both versions** lists each difference with both sides' values and a choice, starting each on the side that
  made it, and saves a draft with both people's work;
- leaving the form with a change on the canvas asks first, in the backoffice and when the tab is closed, as Umbraco's
  workspace does.

Per ADR-0019 and ADR-0020 the three design skills shaped it before it was built and ran again on the result; the design
comes from Phase 2 ([information-architecture.md](information-architecture.md), section 5.7, the designer wireframe's
conflict state, and [copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every
choice the skills would have asked about was decided here or in the ADR.

## What was looked at

Before building, the server refused a stale save with `409`, and the backoffice said so in a toast, then read the form
again and so let the next Save overwrite: the survey for 10.3's split had found it. Umbraco's own guard already asked
before leaving the form's route for any change in the workspace's data. IA 5.7 and copy.md answered the skills'
discovery questions: the dialog's words, its two buttons, and the review's place as a sidebar with Save and Cancel.

After building, the dialog and the review were looked at in Edge at 1440 and 375 px, light and dark, in English and
German, with a field both people relabelled, one only the other person relabelled and one only the user changed: the
dialog names who saved and when; the review heads the two columns of values with the two versions to choose, starts
two choices on their side and leaves the one both changed waiting under its chip; Save with it waiting says so above
and beside it, in red and in words. At 375 px each difference stacks: the choice first, then each value under its
version's name, with nothing squeezed. The same was seen in the journeys' own shots, a name both changed among them.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for a designer who finds that a colleague saved the
same form and wants to keep both people's work without fear of losing either; calm, native, the decision made easy.
Dials 2 / 1 / 5, as for 10.1a to 10.3b. What it asked held:

- **One look:** Umbraco's confirm dialog for the conflict, its large sidebar for the review, its radio group for each
  choice, its tag for "Changed in both"; the comparison's table style from History, stacked where the sidebar is narrow.
- **Every state composed:** the conflict with who and when, and without; a review whose versions can't be read; one
  without differences; a Save with choices still waiting; a review saved into a conflict again.
- **Pre-flight:** no emoji, no gradient, no motion of its own; every string in copy.md's voice.

## impeccable

Operate mode, a refinement inside Umbraco's design system. The bundled engine's `detect` found nothing in the review
sidebar, the conflict flow or the workspace (`[]`). The critique it guides found three things, each changed:

- **The choice sat far from what it chose.** The two radio buttons first stood in a row under the title, away from the
  values; now the versions' names head the two columns of values, as the choice, so each sits right above what it keeps.
  The table keeps its own header for a screen reader, hidden from sight, so the names aren't said twice.
- **Dividers that didn't show.** The sidebar's body is the background colour, where Umbraco's plain divider is the same
  grey; the differences are now set apart by the standalone divider, as the action editors' are.
- **Colour alone said what waited.** Each choice that waits now says "Choose a version" in words, beside the red title.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| a toast that told the user to reload, and a second Save that overwrote | a dialog that keeps the changes, and every Save refused until the review | the moment is rare and high-stakes: it earns a dialog, and a calm one, as nothing is lost yet |
| — | each difference starts on the side that made it | most conflicts touch different things; the review is then a look and a Save, and only a real clash asks for a choice |
| — | a difference both changed is marked and waits, and Save goes to the first that waits | the one decision that matters is never made by default |
| — | no animation of its own: Umbraco's dialog and sidebar open as they do | opened rarely; the backoffice's motion keeps the section one product |

## The words

English and German for every string: the dialog in the designer's start words, as it shows in any view; the review in
`lang/history.*.ts`, which load with it, as its comparison is History's. copy.md has each of them.

## A reader's review

A reader who hadn't built it read the part against IA 5.7, copy.md and ADR-0200 and found thirteen points. What came
of each:

| Found | Done |
| --- | --- |
| a field one side moved and the other relabelled was one choice, so taking either lost the other | an element's own properties and its place are two differences, `Field "Email"` and `Field "Email": place`, each by the side that made it; a rule or a check stays one, as History shows it |
| "Changed in both" was said where nobody knew: without the draft both started from, and where neither side's comparison named an element (a reorder) | who made a difference can be unknown: it waits, unmarked, and the sentence above drops "that version is chosen already" where nothing is |
| a container the user removed stayed whole when it held an element the user had moved elsewhere | what the user's version keeps goes where that version has it, and the container goes; where that place isn't in the merge, the container stays holding only that |
| an element added or removed said only "In the form: Yes / No" | a **Holds** row names what comes and goes with it, on the side that has it |
| the user's own version was lost for good once the review saved | the merged draft is a step to undo: Undo gives the user's version back, said as "Undone: Merged both versions" |
| a move to another folder, an access change or someone's publish forced a review of drafts nobody changed | a save is a conflict only where the draft was saved, or the name changed where the user changed it too, since the form was read; a name goes only where the user changed it, so someone's rename stays |
| the workspace guessed a conflict from the form's version on the server, which named the user as the one who saved after their own half-done save | the data source tells the workspace itself, with an event on its host element, as it answers; the dialog names who saved the form's draft, not who published since |
| a second Save while the versions were read opened a second dialog; edits made then were lost; a form left meanwhile could take the review | one dialog or review at a time for a workspace; a draft changed meanwhile is compared again, a few times at most; a form left is left alone; the review's own save, refused again, says itself again |
| the difference titles were `h3`, the level of the sidebar's own headline | `h4`, as History's sidebars have |
| a choice that waits was marked by colour alone, and the alert was said again with every choice | each one says "Choose a version", which its choice is described by; the alert shows from Save until the next choice, and counts what still waits without claiming why |
| German said "Beidseitig geändert" beside "die beide geändert haben"; places didn't quote names | "Von beiden geändert" throughout; `After "Name"`, `Nach „Name“` |
| two show rules, or two fields with one alias, can come of a merge without a word | kept as for any edit: the form check shows a draft's problems, which block publishing, not saving (ADR-0200) |
| the browser's question needs `returnValue` in some browsers | Umbraco's own workspace asks that question already, `returnValue` set, from 17.6.0 on; the tamper round showed the workspace's own handler only repeated it, so it went |
| missing tests: a save while conflicted, a rename conflict, a waiting Save, the narrow sidebar under axe, Publish while conflicted | each has one now: the data source's and the flow's unit tests, and a third journey (a name both changed, on a phone, Save with a choice waiting, Undo, Publish stopping at a conflict) |

## The tamper round

Each tamper takes back one rule of the part and must make a named test fail; "caught" needs a failed test. In a
throwaway clone of the commit before the part, with the part's files copied in, its client built and the base runs
passing (53 client tests, 12 integration tests): 36 tampers of the comparison endpoint, the differences and who made
them, the merge, the words, the sidebar, the flow and the save.

- 34 were caught at once.
- Two were missed and are caught now, by stronger tests: a choice that didn't end Save's alert (the test chose the
  last difference waiting, which ends it anyway; now it chooses one of two), and the comparison answering the
  definition as sent (the test looked at what both forms hold; now it sends one without settings, which only the
  form a save stores fills in).

The workspace's own lines are checked by the journeys only, so three tampers ran in the working tree against them:
the conflict the data source says not taken (caught: no dialog), and the review's draft not a step to undo (caught: Undo
didn't give the label back). The third, the tab closed without asking, was missed: Umbraco's detail workspace asks the
same question itself, from Umbraco 17.6.0 on, so the workspace's own handler only repeated it, and it went (ADR-0200).

## What the journeys found

`tests/e2e/specs/backoffice/conflict.spec.ts` holds three journeys in Edge against a demo site:

- **Two people, two browsers.** The admin relabels Email and saves; the designer relabels Name and saves, and the save
  is refused: the dialog names the admin and the time. Cancel keeps the change; a second Save is refused again and sends
  nothing. Review both versions starts Name on the designer's side and Email on the admin's, passes axe, and its Save
  keeps both labels; a later save goes through.
- **A name both changed.** The admin renames the form while the designer renames it too and relabels a field: the
  dialog names no one, as no draft was saved meanwhile. On a phone the review stacks; the name waits under "Changed in
  both", the label starts on the designer's side, and axe passes before and after Save says one difference still needs
  a choice. Taking the designer's name and the admin's label saves both; Undo gives the designer's label back, unsaved.
  A Publish after the admin saved a draft stops at the conflict, and nothing goes live.
- **Leaving.** Another form in the tree asks first, and Stay keeps the change; closing the tab asks the browser's own
  question, and staying keeps the page.

They found the first version's way of telling a conflict didn't work in the running backoffice: Umbraco's workspace
replaces a save's error with its own, so the dialog never showed. The data source now tells the workspace itself.

## The size of the start

The size check found what every backoffice session loads at its start over its budget, 65.30 KB of 64 KB, and 64.69 KB
already before this part: the screens of 10.2 and 10.3a/b had grown it, and nothing measures it between milestones. The
form workspace's own words, the inspector's, the rules', the Settings view's and the form check's, now load before its
editor does, as the Actions and History views load theirs ([ADR-0201](../decisions/0201-the-form-workspaces-words-load-with-it.md)):
the start measures 42.28 KB. All 62 journeys of the designer, its views and History passed on it.

## The checks

On the working tree: the formatters (`scripts/fix.mjs`), the client's typecheck, lint and build, the size check (the
backoffice at start 42.28 KB of 64 KB, its largest file 45.20 KB of 64 KB, the whole 378.65 KB of 512 KB), the whole
client suite (5349), the journeys' typecheck, the ASCII scan, `dotnet format`, the unit tests (8454), the integration
tests of the version endpoints, the permission matrix and the OpenAPI snapshot (17), the three conflict journeys and
their look in Edge (light and dark, 1440 and 375 px, English and German), the 62 journeys of the designer and History,
the name scan (0 hits), and the tamper rounds above.
