# Step 10.1b: after sending's outcomes

ROADMAP 10.1b fills the Settings view's After sending group: the form's outcomes as an ordered list, each edited in a
sidebar of its own (what visitors get, the message with merge tags, a copy of the answers, the page or address and its
wait, the place in the list, the rule), its rule in the rules builder, the form check's problems under their setting
and its Fix it leading to them; the canvas's After sending and the rules overview hand the outcome on the same way
([ADR-0185](../decisions/0185-the-outcomes-editor.md), building on ADR-0184, ADR-0181, ADR-0175 and ADR-0016). The
live preview with a sample entry is 10.1c. Per ADR-0019 and ADR-0020 the three design skills ran on the result; the
design comes from Phase 2 ([information-architecture.md](information-architecture.md), section 5.2, the wireframe
`form-settings.html`, states `outcome` and `outcome-deleted`, and [copy.md](copy.md)). The owner answers no questions
during the run (CLAUDE.md rule 8), so every choice was decided here or in the ADR, the `shape` brief included.

## What was looked at

The UX walk (`tests/e2e/specs/ux/walk.spec.ts`, ROADMAP 9.6a), which now shoots After sending too, run with
`JFB_UX_ONLY=settings` on the e2e host (the demo on `127.0.0.1:5520`, ADR-0031) in Edge: at 375, 768 and 1440 px, in
Umbraco's light and dark themes, in English and German, each shot with what axe and the layout measures found, and a
sheet per screen. The walk's grouped form has a redirect for German visitors, ruled by the language, before the form's
own message. The screens: the group open, the message's sidebar, and the redirect's with its rule.

| Shot | What it shows |
| --- | --- |
| [the list](review-10.1b/settings-after-1440.png) | the redirect for German visitors with its rule in words, the form's message "Otherwise", Add outcome; the handles wait for a hover |
| [the list, dark](review-10.1b/settings-after-768-dark.png) | the same at 768 px in Umbraco's dark theme |
| [a message's sidebar](review-10.1b/settings-outcome-1440.png) | What visitors get, the message with the default one as its placeholder and Insert merge tag, the copy of the answers, Place in the list "2 of 2", the rule "Used when no earlier outcome applies" with Add a rule; Delete outcome, Cancel and Update in the footer |
| [a redirect's sidebar](review-10.1b/settings-outcome-redirect-1440-dark-de.png) | German, dark: the address with its help and Insert merge tag, the place "1 von 2", the rule in words with Change rule |
| [on a phone](review-10.1b/settings-outcome-375-de.png) | the message's sidebar at 375 px in German, one column, Cancel and Update in the footer, Delete outcome below the content |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) on every screen of the Settings view in the twelve ways, 120 shots: nothing in the
  view's, the list's or the sidebar's own elements. Umbraco's tree reports `label-content-name-mismatch` on its menu
  items, as on every screen of the section (ADR-0182, OWNER-TODO).
- **The bundled detector** (`impeccable detect --json`): `[]` for the list, the sidebar, the view, the shared lines
  and both style sheets.
- **The layout measures:** no box scrolls sideways inside the view or the sidebar and no word is cut by an ellipsis at
  any width. The window scrolls sideways by 545 px at 375 and 152 px at 768, Umbraco's 920 px page (ADR-0182).
- **The budgets:** the backoffice at start is 61.37 KB of 64 (gzip), the largest file 45.97 KB; the outcomes' sidebar
  loads when it opens.
- **The journeys**, below.

The walk's first run on the reviewed build failed every way after two minutes: the e2e host's SQLite connection hung
("Cache instruction sync did not complete within 00:01:00"), so each sign-in waited on the one before. The run was
stopped by its process ids and run again on a fresh host: 13 of 13.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors who decide what a visitor gets after
sending, calm and native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 1 / 5, as for 10.1a. What
it still asks held:

- **No colour of its own:** the rows are Umbraco's surface and border, the kind's icon and the second line in the muted
  text, "Never used" and a count of warnings in the warning text, a count with an error in the danger text, each with
  words, so colour is never the only sign.
- **One radius scale** (UUI's) and the view's one column; the sidebar is Umbraco's medium sidebar with its own footer.
- **Every state composed:** no outcome (the default message said, Add outcome), one for every visitor (no default
  line), one that no visitor gets ("Never used"), a redirect that leads nowhere yet, a page whose name has not come yet
  ("a page of the site"), problems on a row and on the closed group, the list's own problems, the limit, a sidebar
  waiting for the check (Update's own waiting state), and a new outcome placed by its rule.
- **Pre-flight:** no emoji, no gradient, no motion of its own, the copy in plain words from copy.md's voice.

## impeccable

Operate mode, a refinement inside the incumbent world; the code, IA 5.2, the wireframe and copy.md were the context
(`impeccable context`, the bundled engine, no hooks or live mode). Critique against them and what the looks found:

| Finding | What was done |
| --- | --- |
| On a phone the sidebar's footer squashed Delete outcome, Cancel and Update into one row (the first walk) | Delete outcome moves into the sidebar's content at 600 px and under; Cancel and Update keep the footer |
| A row's button was named by the outcome's name alone while it shows when visitors get it too (axe, WCAG 2.5.3) | named by all it shows |
| The rule's button was Umbraco's outline button, which the rule's words could not describe: a description on its host never reaches the button inside | a native button in the outline look, described by the rule's words and "Never used" |
| Moving an outcome needed a drag or Alt and the arrows (WCAG 2.5.7) | the sidebar's Place in the list, shown only where the list has another place |
| A count of warnings on a row or a closed group looked like a count of errors | the count in the colour of the heaviest problem, as the check's badge |
| "Never used" showed on the row, not in the sidebar where the rule and the place that cause it are set | said in the sidebar too, under the rule's words |
| Closed, After sending said only the one outcome's kind ("A message") | it names the outcome as its row does |
| Add outcome was offered past the most outcomes the server keeps, which the save then refuses | a line says the limit in its place |

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | a row moved by the keyboard lands at once, the focus on its handle | a move is made from the keyboard, repeated; motion would make each one wait |
| — | a dragged row dims to half and a 2 px line shows where it would go | the editor sees the place before letting go, without the row flying |
| — | the handle shows at once on hover and focus, no fade | it is a tool the hand is already on its way to |
| — | the sidebar slides as Umbraco's own sidebars do | it is Umbraco's modal; a motion of its own would differ from every other sidebar |
| — | Update shows Umbraco's waiting state while the check answers | the press is heard, and a second press keeps nothing twice |
| the focus lost to the page after the toast's Undo | the focus on the outcome back in the list | the toast it was on is gone; the editor goes on where the outcome is |
| rows reused by their place as the list changed | rows kept by their outcome's key | the focus and the dragged state stay on the outcome, not on whatever takes its place |
| the canvas's move handle and insert points faded in and out over 120 ms | they show and hide at once | every selection, focus and hover reveals one and hides another, and each frame of a fade painted the whole sheet again (the gate, below) |

## The words

English and German for every string, in `lang/settings.en.ts`, `settings.de.ts` and the rules' areas, from copy.md's
voice and glossary: "outcome" is "Abschluss"; a sidebar's Update is "Aktualisieren" (the rules builder's too, which
said "Übernehmen" before). An outcome is named by what visitors get ("Redirect to /press/thanks", `Message: "…"`),
the builder says "Use outcome 2 when" or "Use the new outcome when", and a problem in another language's message
names the language.

## Kept as they are

- **Umbraco's 920 px page** on a phone, as every section (ADR-0182).
- **Problem lines are not announced** as they come, as in 10.1a: they describe their box, which says them when it is
  reached; Update's focus goes to the first one.
- **The option editor's rows** still move only by dragging or Alt and the arrows; a pointer way without dragging is
  owed there too, and the phase review (10.4) takes it with the designer's other open findings (ADR-0185).
- **The rules' words in German** keep the builder's order after "wenn" ("Verwendet, wenn die Sprache beginnt mit de"),
  as every rule's words in the designer do since 9.5: they read as the builder's rows, field, operator, value.
- **The start of the backoffice** at 61.37 KB of 64: 10.2's screens will need their words to load with them, as the
  settings' and outcomes' do not yet.

## A reader's review

A reader who had not seen the work read the change against the step, the ADR and the code, and found 27 things.
Every one was fixed before the tamper round, each with a test, but the one kept below:

- **An outcome's rule could not be changed** (the blocker): the builder read an existing `use` rule as a show rule.
  It now opens as itself and stays the outcome's.
- **The rule saw too little:** an outcome's rule is the server's, once the form is sent, so a value that stays on the
  server and a field a member's value stands in for are subjects too; a show rule still can't see them.
- **Update kept back by what isn't there:** a refused box another kind took away, and a problem in another language's
  message, which the sidebar doesn't edit. The first no longer counts; the second is named ("German: …") and
  keeps nothing back.
- **An answer about the outcome before the last change** put a redirect's problem under the kind after a switch to a
  message; such an answer now shows only problems whose box is still drawn as itself.
- **Twice:** a second press of Update or of the rule's button kept the outcome or opened the builder again; Update
  after the sidebar closed while the check answered kept it anyway. Each is now guarded.
- **Changes that change nothing** still said "Updated", offered Undo and announced; they now say nothing.
- **Where a new outcome goes:** before a last outcome for every visitor, so one placed after an earlier such outcome
  was never used; it now goes before the first, until its place is chosen.
- **The limit:** Add outcome went past the server's 50; the settings choices now carry the limit and the list says it.
- **The canvas** showed the first outcome without rules, not the first for every visitor (a rule switched off counts as
  none).
- **Pages named in one language:** a page's name was read once, in the language shown then; it now follows the
  language.
- **Moving without dragging** (WCAG 2.5.7): the sidebar's Place in the list. The option editor has the same gap,
  recorded for 10.4.
- **The rule's button** could not be described by its words; it is a native button now.
- **The focus** went nowhere after the toast's Undo, and the list's own problems sat outside the list, describing
  nothing; the first goes to the outcome back in the list, the second shows above Add outcome and describes it.
- **Counts** of warnings looked like counts of errors; each is now coloured by the heaviest problem among them.
- **Names:** the closed group names its one outcome, the builder says "Use outcome 2 when", the sidebar says
  "Never used" where it applies, and German says "Aktualisieren" for a sidebar's Update.
- **The message's Fix it** put the focus on the rich text's host, not in its text; the editor now takes it.

Kept, as in 10.1a: problem lines are not announced as they come (see above).

Fixing the focus after a delete found one more: the list reused its rows by place, so the focus stayed on the row
while another outcome took it. The rows are now kept by the outcome's key.

## The tamper round

In a throwaway clone of `210a6e8` (ADR-0117), 54 changes each took back one rule of the step, one at a time, after
every check had passed untampered, each run under a time limit and the file restored after it: the server's limit
(1), the outcome model (14), the list and its counts (10), the sidebar (14), the view and the closed line (8), and the
rules builder, the check, the canvas and the Design view (7).

The first run caught 51. The rest:

- **Missed (2):** an outcome's other rules dropped when it gets its first `use` rule (an outcome keeps what the editor
  does not know as it came), and the list's rows kept by their place: the view's wait for its own render had fixed the
  focus after a delete as well, so no test held the rows to their outcomes. The model's test now adds a first rule to
  an outcome with another, and the list's test reorders the list from outside, as an Undo does, with the focus on a
  row.
- **A tamper that did not build (1):** the settings choices saying a limit of 0 left the controller's limits unread,
  which warnings as errors refuse; rewritten as one less than the limit.

The second run of these three caught all three. The handle's reveal on hover, the phone's Delete outcome below the
content and the rule button's look are CSS that jsdom does not lay out: the walk's shots and measures hold them.

## What the journeys found

`form-outcomes.spec.ts` starts from a new form's one outcome, the default message for every visitor; writes its message
with a merge tag and a copy of the answers; refuses a wait of 900 seconds once its box is left and cancels; adds a
redirect whose address the form check refuses, Add kept back with the focus on the address and the check's own words
under it, then a path, and a rule from the rules builder ("Use the new outcome when"); sees it placed before the one
for every visitor and said so; moves it with Alt and the arrows and with the sidebar's Place in the list, "Never used"
said before the move is kept, each undone with Ctrl+Z; deletes it and brings it back with the toast's Undo, the focus
on it again; closes the group and reads the draft as Save draft kept it. The second journey follows the form check's
Fix it of an outcome from the Design view to its address in the sidebar, sees the row count the problem, and follows
the canvas's Change to the outcome for every visitor. Axe ran on the list and the sidebar. `form-settings.spec.ts`,
`designer-form-check.spec.ts` and `designer-rules-overview.spec.ts` ran beside them: 10 of 10.

The first runs failed on what the step had changed around them: a row's move handle, named "Move …" with the
outcome's name, matched a locator for the name too (the spec now finds a row by its name's own element); the Settings
view's status region and the list's both answered a locator for "the status" (the view's has its id now); and axe
reported the row's button named by its name alone while it shows its line too (see impeccable).

## The gate

The first gate, on `70471f1`, failed on one test alone: the designer's budget for a form of 300 fields
(`budgets/designer.spec.ts`, ADR-0169), the drop at 112 ms from the input to the next paint, though nothing the step
changed is on the drop's path. Measured on the idle machine, the test's runs differed by more than the step could:

- **The test counted its own recorder.** Playwright's trace snapshots the page at every action by walking the whole
  DOM on the page's thread, 160 to 210 ms on the 300 fields by the browser's CPU profiler, against 10 to 16 ms of the
  designer's own work. The `budgets` project records no trace now (ADR-0169's addendum).
- **Two fades and a scroller painted the sheet again:** the handle and the insert points faded over 120 ms at every
  selection, focus and hover (emil-design-eng, above), and the canvas's scroller, which the browser saw through, had no
  layer of its own. The fades are gone; the scroller has the host's solid colour, so the browser scrolls it as a
  layer.
- **What was left is the browser's drawing:** a frame right after the drop still lasted 85 to 110 ms while the main
  thread was idle for most of it, and the same measure at ADR-0176's own commit, `d6bad04`, and at 10.1a's `5a46a03`
  gave 92 to 110 ms today, where ADR-0176 had measured 52 at most. The budget now measures what ADR-0169 says it does,
  the main thread's hold: every task and each long frame's own work under 100 ms, a frame's rendering under 50 ms, and
  every interaction under 100 ms as before; a frame's whole duration is reported beside them
  ([ADR-0186](../decisions/0186-the-designer-budget-measures-the-main-thread.md)).

Five runs of the test then passed: interactions 72 to 96 ms, no task of 50 ms or more, at most 43 ms of work in a
frame. A 120 ms stall put into the keyboard's handling of the built bundle failed it, with a task of 134 ms; the bundle
was restored.
