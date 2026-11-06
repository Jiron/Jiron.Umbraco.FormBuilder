# Step 10.2f: the last run and its Details

ROADMAP 10.2f gives each action's card its last run, and a failed or uncertain one a **Details** sidebar: what happened,
the tries, never the entry, who can retry it and Test ([ADR-0195](../decisions/0195-an-actions-last-run-and-its-details.md),
building on ADR-0013's amendment, ADR-0040, ADR-0188 and ADR-0190). Per ADR-0019 and ADR-0020 the three design skills
shaped it before it was built and ran again on the result; the design comes from Phase 2
([information-architecture.md](information-architecture.md), section 5.3, and [copy.md](copy.md)). The owner answers no
questions during the run (CLAUDE.md rule 8), so every choice the skills would have asked about was decided here or in
the ADR. Since the owner's message of the same day (ADR-0194), the step is finished on its own checks, and the full gate
runs at Phase 10's review.

## What was looked at

Before building, the Actions view's card had its name, summary, problems and rule, and nothing of its runs: a run
stored no time of its latest attempt, and a form's runs could not be found by action without reading its entries. The
skills' discovery questions were answered from IA 5.3, copy.md's status words ("Delivered", "Failed at 14:02. Next try
at 14:30.") and ADR-0013's amendment on who may retry.

After building, a scratch look in Edge, light and dark, at 1440 and 375 px, shot a form whose two webhooks had had an
entry: the stub took one and answered the other 503. The delivered card says "Delivered just now", muted; the failed
one "Failed at 4:18 PM. Next try at 4:19 PM." in the danger colour, its Details underlined in Umbraco's link colour;
the Details sidebar says the line again, "The server answered 503.", "Tried once, at 4:18 PM." and what to do, with
Test and Close. The look found one sentence that read awkwardly ("…and an administrator can in Activity ›
Deliveries"); it says "An administrator can also retry it in Activity › Deliveries." now, in German too.

Shot again after the reviews, with the card scrolled into view at 375 px: each line sits under its name across the
card's width, the failed one "Failed at 5:08 PM. Next try at 5:09 PM. Details"; the sidebar's four parts are Last run,
What happened, Tries ("Tried once.") and What can be done, under small headings, in light and dark alike. At 375 px the
backoffice scrolls sideways around the view, as Umbraco lays it out at that width; the card and the sidebar fit.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for a designer who glances at a form's actions and,
where one failed, wants to know why and what to do without anyone's answers; calm, native, one line a card. The skill's
landing-page rules don't apply (ADR-0019); dials 2 / 1 / 5, as for 10.1a to 10.2e. What it still asks held:

- **One look:** the card's small muted text for a run that went well, the danger and warning text colours the form
  check's counts already use, Umbraco's link colour and underline for Details, Umbraco's small sidebar, outline Test and
  plain Close as the action's sidebar has them. No colour, motion or icon of the package's own.
- **Every state composed:** went through (by the type's own verb), running, skipped, failed and tried again, failed for
  good, uncertain, tried before the site kept the latest try's time, a run without times, an action that never ran (nothing said), who may only read (Details without
  Test), a read that fails (the lines stay as they were).
- **Pre-flight:** no emoji, no gradient, no spinner of its own; every string in copy.md's voice.

## impeccable

Operate mode, a refinement inside the incumbent world, shaped before it was built (`shape`, its questions decided in
ADR-0195) and critiqued after. The critique ran as two isolated assessments: a design review that read the code, IA 5.3,
copy.md, section 4.13, the ADR and every shot, and the bundled detector (`impeccable detect`, the bundled engine; no
hooks, live server or browser injection, which the owner rules out). The detector found nothing in `run-words.ts`,
`action-list.element.ts`, `action-run.modal.element.ts` and `form-actions.view.ts`, also with `--no-config`.

The design review scored the first build 29 of 40: the line and its colours read at a glance, and Details kept to what
a designer can act on, with no entry in sight. What it found, and what was done:

| Finding | What was done |
| --- | --- |
| "Cut off at 14:02" named a mechanism, while copy.md's status word is Uncertain | "Uncertain at 14:02. It may have gone through.", in German "Unklar um 14:02. Möglicherweise wurde der Lauf trotzdem ausgeführt." |
| Test was busy while a webhook's test only asked first, and a No left nothing to wait for | busy, and said in the live region, only once the test runs (ADR-0190's started call) |
| The tries repeated the line's time ("Tried 3 times, from 13:40 to 14:02.") | the line says the latest's time and the tries the first's: "Tried 3 times, the first at 13:40.", "Tried once." |
| "Can retry the run from its entry" did not say how to find the entry, which Details never name | "…finds the entry in Entries with the Actions filter, and can retry the run there. An administrator can also retry it in Activity › Deliveries." |
| A next try whose time had passed, as while sending is paused, said a time in the past | "Failed at 14:02. Next try soon." |
| "What you can do" over a sentence about what others can do | "What can be done" |
| Four landmarks in a small sidebar, each a section named by its heading | four parts under small headings (h4 under the sidebar's own), no landmarks |
| The line came after the switch and the "…" when read, though it is about the name above it | it comes before them in the card, on its own grid row under the name |
| Details' target was its words' height | a taller target, without moving the line |
| Words: "came out too long to send", "a state this move does not leave", German "gelaufen" and "antwortete 503" | "The body came out too long to send.", "The entry's state doesn't allow this move.", "ausgeführt", "antwortete mit 503" |

Decided against: Details' footer margin, which is Umbraco's own sidebar layout and matches the action's sidebar; and
"can find out why" over "finds why", which the backoffice's other failure words already say.

Questions skipped: the owner answers no questions during the run (CLAUDE.md rule 8); each was decided here.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | the last run on a line of its own under the rule | inside the card's button it would be read as part of its name, and a button can't hold Details |
| — | no transition when a line changes | it changes when the page is shown again, not under the pointer; a fade would draw the eye to nothing |
| — | Details as an underlined link-coloured word at the end of the line | the line says what happened; the word says where to read more, and takes no room of its own |
| — | the failure's time of day, a success's time relative | a failure is acted on at a time ("next try at 14:30"); a success only needs to feel recent |

## The words

English and German for every string, in `lang/actions.*.ts` (the keys starting with `run`); copy.md has the card's
lines, What happened and the tries, and What can be done. Times are the reader's: "14:02" or "2:02 PM", "5 Nov" or
"5. Nov.", and "yesterday" or "gestern".

## A reader's review

A reviewer who had not seen the step read the code, the tests and the ADR for leaks, permissions, query cost and
wrong words. It found no data leak and no permission gap: the answer holds no ids, entry or token; a run of another
form's entry never counts; a run that waits never hides the one before; the most actions a form can have is the most
one read takes; and every word has English and German. What it found, and what was done:

| Finding | What was done |
| --- | --- |
| A run tried several times before step 10 knows only its first try's time, and its line said that time as the latest's | such a line says no time ("Failed. Next try at 14:30.", "Uncertain. It may have gone through."); the tries still say the first's |
| The answer's next try was documented as null once final, but a run cut off or still running kept the time it was due at | the next try is said for a pending run only; a unit test holds each other state to none |
| ADR-0195 still had an earlier wording of the uncertain line | the ADR has the reviewed words |
| The read walks back past an action's runs that wait for their first try, each a lookup on SQL Server | the index holds what the read filters on, `(actionKey, id, state, attempts, entryId)`, while step 10 is unshipped |
| A retry from the log started the count again but kept the first round's first try, so the tries mixed two rounds | the retry clears both times with the count, as it starts the series over (ADR-0040); the entry's answer says so |
| A form chosen meanwhile kept the one before's runs if its own read failed, and two reads had no order | the runs are cleared when the form changes, and only the latest read's answer is said |
| No tests for those, nor for a shared key whose other form's run is newer, nor for uncertain, skipped and retried runs | each has a test |
| The line was squeezed into the name's column on a phone | it takes the width under the switch too |

Noted, not changed: an action key that a draft saved through the API shares with another form makes the read pass over
that form's runs, which never count; and a site that does not run its migrations by itself fails to queue actions
until an administrator runs step 10, as step 8 made entry events do. ADR-0195 lists both as known limits.

## The tamper round

In a throwaway clone of the commit before the step with the step's files copied in, its client built and its base
checks passing, 43 tampers each took back one rule, and each was caught by a named test failing: 13 in the server (a
run that waits hiding the one before, another form's run counting, the oldest run taken, an attempt not keeping its
time, an action listed twice, more actions than a take holds, the index without the id or without what the read
filters on, a retry keeping the first round's times, a next try said for a run cut off, the draft's actions not asked,
when it ended said as when it was queued, and the last runs needing more than `design`) and 30 in the backoffice
(the words, the card, the view and Details, among them the first try said for the latest, a due next try said at its
past time, an older read's runs said last, another form's runs kept on its cards, and Test busy while a webhook asks
first). A first round on the build before the reviews missed one, the first try said for the latest: the tests had no
run whose first and latest tries differ; they have now.

## What the journeys found

`action-runs.spec.ts` runs in Edge against the demo site: a form with two webhooks is published with the public API on,
an entry comes in through it, and the stub takes one webhook and answers the other 503. Once both runs have had their
first attempt, the delivered card says "Delivered just now" and the failed one its failure and next try; its Details say
"The server answered 503." and the tries, never the visitor's answer, pass axe, and Test asks first, then says the
server answered 503 again; Close closes it. The first run of the journey failed only on its own pattern, which did not
allow the space the line keeps around its words. With the reviewed words it passes, and so do the actions view's 15
other journeys.

## The checks

The formatters, the build, `dotnet format`, the unit tests, the integration tests of the runs, the actions, the
management API and the migrations, the backoffice's lint, typecheck and tests, the journeys' typecheck and the journeys
above (ADR-0194). WORKLOG has their results.
