# Step 16.6: the UX polish pass

Phase 16's polish pass walks the whole product as the people who use it do: an editor builds and publishes a form of two
pages with a rule and an action, an entry handler works through the Inbox, an administrator sets permissions and
retention, a visitor fills in the job application on a phone, and a privacy officer erases a person's data. Each journey
is shot at 375, 768 and 1440 px, in Umbraco's light and dark themes, in English and German, beside the UX walk of every
screen. It takes the items 14.1's German walk and 15.3's look carried in, reruns the three design skills of ADR-0019 and
ADR-0020, and counts the clicks of each persona's top five tasks on the built screens. What changed is decided in
[ADR-0241](../decisions/0241-the-ux-polish-pass.md).

## How the journeys are walked

`tests/e2e/specs/ux/journeys.spec.ts` (`JFB_E2E_UX=1`, project `ux`, beside the walk of every screen) follows five
people from start to end in each of the twelve looks of `support/ux-walk.ts`, each person signed in on a page of their
own, and shoots every step with what axe found inside the section's own elements, how far the page scrolls sideways,
what scrolls inside it and what an ellipsis cuts short. The walk's contact sheets show the journeys' steps too. Like the
walk, the journeys find their way by tags, classes and data marks, never by words, so they walk the German backoffice
the same way, and they keep unsaved what the whole site reads.

| Journey | Who | Its steps |
| --- | --- | --- |
| Builds and publishes a form | a designer | a new form from the sidebar's "+", four questions pasted and the form named, a second page with a question of its own, a rule that shows the last question of page 1 by the answer before it, the draft saved, a mail action to the team, Publish… and the published form |
| Works through the Inbox | an entry handler | the Inbox from the sidebar, an entry opened beside the list, approved, a note written, two entries ticked for the bulk bar |
| Sets permissions and retention | an administrator, in German its German twin | a permission ticked in Settings › Access (unsaved), a folder of its own restricted to a group from its "…" and saved, the days for rejected entries changed in Privacy › Retention (unsaved) |
| Fills in a form on a phone | a visitor, at 375 px only, in the site's light or dark scheme | the job application, Next on its empty first page with the messages, the three pages filled in, sent |
| Erases a person's data | a privacy officer | Privacy › People, two entries found by an address, Erase… with what goes, erased |

Before a shot in a window narrower than Umbraco's page, the walk and the journeys pan to the workspace's start, where a
phone's reader pans to read it; a focused field could otherwise leave the page scrolled to its right edge.

## Carried in

### From 15.3: the Design view's outer scroll area

At 1440 × 900 a one-field form's workspace (`uui-scroll-container#main`) scrolled by 1,052 px beside the canvas's own
scroller, before any selection. The view itself kept to its 716 px; what reached below it were the palette's hidden
reasons of the field types the site does not offer, each a `.visually-hidden` span (`position: absolute`). The palette's
list scrolls (`overflow: auto`) but was not positioned, so the spans took the Design view as their containing block,
escaped the list's clip at their place in its scrolled content, and stretched the workspace's scroll area to about 1,770
px. Every scroll area of the backoffice now positions its box (the entries list had the same leak with its status line),
a source test holds the rule for all thirteen, and the designer's browser test measures every box around the view at
1440 px: each holds no more than it shows. Run on the build before the fix, it failed with the 1,052 px.

### From 14.1: the phone

Umbraco's page stays at least 920 px wide, so a 375 px window scrolls sideways and shows only the first part of the 620
px workspace (ADR-0182). The section's views laid out for those 620 px, so on a phone a pair of boxes stood side by side
with the second one past the window's edge, and lines ran past it too. Now a view's column keeps to the window's width
from the workspace's start while the window is narrower than Umbraco's page: once the workspace is in sight the whole
column is too, and the container queries the views already had lay it out for the phone.

| Before | After |
| --- | --- |
| [Availability's two date boxes side by side, the second cut by the window](review-16.6/availability-375-before.png) | [one under the other, the help in the window](review-16.6/availability-375.png) |
| [the retention days side by side](review-16.6/retention-375-before.png) | [one under the other](review-16.6/retention-375.png) |
| [an action card's switch and "…" beside its text, past the window's edge](review-16.6/actions-375-before.png) | [on a row of their own under the text, the name and its line the card's width](review-16.6/actions-375.png) |
| [the rule builder's target cut beside „wenn"](review-16.6/rule-375-before.png) | [the target on its own line, „wenn" under it leading into the conditions](review-16.6/rule-375.png) |
| [„Vorlage speichern" on two lines in the template dialog](review-16.6/template-375-before.png) | [„Speichern", as the copy guide has a dialog whose title names the object](review-16.6/template-375.png) |

The walk now signs in as the admin's German twin for its German looks, a test user whose language the server keeps, so
the texts the server writes in the user's language show in German: the Overview's alert, the digest's preview and the
email layout's sample.

## What the journeys measured

Across the five journeys in the twelve looks (twenty-two to twenty-seven steps a look), the measures inside the
section's own elements came down to these:

| Measure | Found | What it is |
| --- | --- | --- |
| Sideways scroll of the page | 545 px at 375, 152 px at 768, none at 1440 | Umbraco's own 920 px page (ADR-0182), the same on every screen; nothing of the section's adds to it |
| Boxes that scroll inside | the access matrix, at every width | a grid of permissions by groups that scrolls in its own box with a shade where more groups lie; the permission column stays |
| Texts cut short | the Permissions sidebar's headline at 375 px | Umbraco's own sidebar headline, ending a long generated folder name with its ellipsis as every Umbraco sidebar does |
| axe, Umbraco's parts | the forms tree's rows (`label-content-name-mismatch`), the open row's colours in the dark theme (the Inbox's item while the Inbox is open among them), UUI's "select all" while some rows are ticked | Umbraco's and UUI's own markup inside the section's elements, each in OWNER-TODO's report to Umbraco |
| axe, the section's own | the Inbox's count badge in the dark theme, in all six dark looks | fixed (below): the final build's walk finds it in none |
| axe, passing moments | an action card's handle and the new action's last trigger radio at 375 px in English | a look at both found the handle 32 px with nothing over it once the page settles (a toast lay over it in the journey's shot), and the radio under the sidebar's own sticky footer at that scroll position; each label is the radio's target too |

The visitor's journey on a phone was clean in all four of its looks: no sideways scroll, nothing cut, nothing from axe.
The walk could not reach some screens in seven looks; each was the walk's own: a picker whose row it looked for before
the tree's first page had loaded, a reload given five seconds where the backoffice took longer, and, in the looks that
ran while the machine also built and tested, timeouts. The walk now waits for the tree's first page and for the view
before its Details, and the journey waits for the publish to answer before it reloads (it reloaded at once, which cut
the publish off, so the shot after Publish still showed the draft). On the final build the walk missed one row in one
look: the tree's Load more can show a moment after its first page, and the walk took the moment without it for the
tree's end; it now looks again until the row or the end is certain. The journeys there stopped where this pass changed
the product: the rule on a yes or no question now offers its two answers, where the journey still looked for a box to
type in, so it chooses "yes" from the list.

## The top tasks, counted on the built screens

`docs/ux/top-tasks.md` held each persona's fifteen tasks with the click paths a first-time editor found from the design
alone (step 2.1). The first five of each are now done on the built screens by browser tests, from the section's start
page at 1440 px, each click of the path counted and the task's own input and confirming button not, and each count held
to the document's, which the tests read: `specs/backoffice/top-tasks.spec.ts` for the designer, the entry handler, the
viewer, the privacy officer and a user without grants; the site-wide specs that change and restore the settings for the
administrator's first four; and the setup checklist's own host for the fifth.

| Persona | Tasks 1 to 5, clicks on the built screens | What changed |
| --- | --- | --- |
| Designer | 2 · 2 · 2 · 2 · 2 | task 2 was 3: the template gallery opens with the contact form chosen, so its card needs no click |
| Entry handler | 1 · 2 · 3 · 3 · 3 | as designed |
| Viewer | 0 · 2 · 2 · 3 · 2 | task 4 was 2: the Filter menu names what to filter by first, so the month is under Received |
| Administrator | 2 · 1 · 1 · 2 · 1 | as designed |
| Privacy officer | 1 · 1 · 3 · 2 · 1 | task 2 found a broken path (below); task 3 was 2: the export is put together in the background, and its toast's Download saves it |
| No access | 0 · 0 · 0 | as designed, task 2 with an open checklist; once it is complete, the Overview's "…" shows it again in 2 |

**Removed:** the member search's second click. "Find a member instead…" switched the box and then asked for "Choose a
member"; it now opens Umbraco's member picker at once (2 clicks for a user who may open Members).

**Found broken:** the privacy officer's member search. Umbraco answers its member list only to users who may open its
Members section, so the picker opened empty (403) for the persona People is for. The switch now shows only to those
users; the others find a member by the member's address, which finds the member's entries too (ADR-0223), in one click,
and the line for nobody found no longer points them to the member search.

Every count is within the three clicks of the IA's rules, and none of the longer paths has a step to take away: the
Filter menu's first level names the dimensions (a period under each would double the menu), and a person's export may be
large, so it runs in the background rather than holding the screen.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's backoffice for editors, entry handlers, administrators and privacy
officers, in a calm, trust-first language, leaning toward Umbraco's own design system (UUI's tokens and components); the
visitor's form takes the site's own type and width. The skill's landing-page rules (layout variance, display type,
motion) do not apply (ADR-0019); of its dials the trust-first row fits: variance 3, motion 2, density 5. What it asks
that does apply, held against this pass's screens and changes:

- **No colour, radius or shadow of the package's own.** The phone column, the narrow card's row, the table's focus
  outline and the preview's new note take UUI's tokens only.
- **No button wraps** (its rule against a call to action on two lines): the template dialog's „Vorlage speichern" did at
  375 px; it is „Speichern" now.
- **Every state has its own words.** The phase review found the preview showing a content action's missing rights as a
  failure to load, or turning its switch back without a word; it now says why test entries run no actions (ADR-0241,
  decision 9).
- **The focus is always somewhere you can see.** Esc in the entries list dropped it to the page, where the list's keys
  stop; it rests on the table now, outlined (decision 8).
- **No motion was added** (see emil-design-eng below).

## impeccable

Operate mode, a refinement in the incumbent world, with the copy guide and the IA as the context. The critique ran as
its two assessments in isolated agents, neither seeing the other's answer nor this review: A read the journeys' shots in
the twelve looks with the source, B ran the bundled detector and read the walk's recorded measures.

**Design health (assessment A):**

| # | Heuristic | Score | Key issue |
| --- | --- | --- | --- |
| 1 | Visibility of system status | 3 | state chips, Undo toasts and the form check badge say where things stand; the Inbox's badge counts the unseen, which the list does not |
| 2 | Match with the real world | 3 | plain words throughout; a column named "Actions" full of "none" can read as buttons |
| 3 | User control and freedom | 3 | Undo, Cancel and "Back to waiting for approval" everywhere; on a phone the entry panel's bar lies past the window |
| 4 | Consistency and standards | 3 | Umbraco's own patterns; the access matrix's native boxes beside UUI's elsewhere |
| 5 | Error prevention | 2 | the retention preview and the erase dialog are exemplary; a rule on a yes or no question took a typed value that never matched |
| 6 | Recognition rather than recall | 3 | palette with icons, rules as chips on the canvas, empty states that link to the fix |
| 7 | Flexibility and efficiency | 3 | paste questions, the list's keys, the bulk bar, Move to… |
| 8 | Aesthetic and minimalist design | 3 | calm screens; the canvas's toolbar holds about ten controls, the erase dialog said its count three times |
| 9 | Recognising and recovering from errors | 3 | the visitor's error summary and Fix it; the action sidebar kept a recipient error beside the recipient just added |
| 10 | Help and documentation | 3 | rich help under the settings, sometimes long |
| **Total** | | **29/40** | **Good** |

**Design specificity:** authored for its people and tasks. The chrome is Umbraco's on purpose; what is the product's own
is in what it says: a trigger explained against this form's settings, the access screen naming the groups that cannot
open the section, retention showing what would go and the key that locks it, the erase dialog's "Statistics stay. You
can't undo this." **Deterministic scan (assessment B):** the detector exits 0 with no finding on the backoffice's and
the runtime's sources, the element, logic, themes and AI packages, and the 42 views passed by name (a folder scan skips
`.cshtml` files). On copies beside DESIGN.md its design-system rule adds advisories only: the matrix's scroll shade, a
`#000` in a colour mix, and two test fixtures. In the built bundle, which no one reads, two of its colour advisories are
id selectors (`#add`) and two a colour input's required fallback.

**Priority issues, and what was done:**

| Issue | Found by | Done |
| --- | --- | --- |
| [P1] A rule on a yes or no question offered a box, so a typed "Yes" never matched the answer `yes`, and the form check said nothing | A | The builder lists the question's two answers, as the field names them; the rule's words say them so too; the form check warns at a value that is none of a choice's own options, and the rule search tries a yes or no's answers (ADR-0241 decision 10, ADR-0179 amended) |
| [P2] The access matrix hides groups past its edge at 1440 px | A | Kept: its right edge carries a shade where more groups lie, the permission column stays while it scrolls, and the test site has ten groups where a site has a few |
| [P2] "Desktop" narrowed to fit at 1440 px beside the palette and the inspector | A | Kept: the note says it, the palette and the inspector fold away for the full width (ADR-0016); the Phone and Tablet widths are what the canvas shows exactly |
| [P2] The Inbox and People below a long forms tree | A | The Overview's Recent entries box opens the Inbox (Open the Inbox in its header); the sidebar keeps Umbraco's menu order (IA 2), and the test site's root holds over a hundred forms from twelve looks where a site's holds a few |
| [P3] The erase dialog said its count three times | A | A lone form is named in the sentence and its row goes; with several forms the rows stay, each its own count |
| The Inbox's badge in the dark theme under 4.5:1 | B | The Inbox's item takes the section's tokens, which give a filled badge its white label; six more elements lacked them, and a source test now holds every element to them |
| The action sidebar kept "A mail needs someone to go to" beside the recipient just added, until the next check | A | A problem about a setting changed since the check answered goes with the change |
| A dash for an empty value in two insight tables | A | "no data", as the copy guide has it |
| A configuration key broken inside a word on a phone | A | Keys wrap after their colons only, on the four screens that show one |
| Two headings stacked in the folder's Permissions sidebar | A | The area's heading is heard, not seen: the switch's help above says it |
| The insights table scrolled 21 px sideways in German at 768 px | B | Its cells sit closer in a narrow box |

**Before and after** (the earlier walk's shots beside the final build's):

| Before | After |
| --- | --- |
| [a rule on "Have you joined a circle before?" took "Yes" typed into a box, which never matched the answer `yes`](review-16.6/rule-yesno-before.png) | [the question's two answers to choose from](review-16.6/rule-yesno.png) |
| ["A mail needs someone to go to." under the recipient just added](review-16.6/action-problem-before.png) | [the problem gone with the change, the preview naming the address](review-16.6/action-problem.png) |
| [the erase dialog's count in its sentence, on the form's row and on its button](review-16.6/erase-before.png) | [the one form named in the sentence, the count on the button](review-16.6/erase.png) |
| [the Overview's Recent entries with no way on to the Inbox](review-16.6/overview-before.png) | [Open the Inbox in the box's header](review-16.6/overview.png) |
| [„Abschlussquote" past the box's edge at 768 px in German, a dash for an empty value](review-16.6/glance-768-de-before.png) | [the table inside its box, „keine Daten"](review-16.6/glance-768-de.png) |

The Inbox's count badge is too small to show in a shot: axe found it under 4.5:1 in all six dark looks of the earlier
walk, and in none of the final build's. The item's label while the Inbox is open keeps Umbraco's colours for an open
row, as every tree's open row does (OWNER-TODO's report).

**Audit health:**

| # | Dimension | Score | Key finding |
| --- | --- | --- | --- |
| 1 | Accessibility | 3 | AA held in the section's own markup but for the targets below at 375 px; the rest is Umbraco's and UUI's, reported |
| 2 | Performance | 3 | every size budget held (the backoffice bundle 504 of 512 KB is the one to watch) and no motion added |
| 3 | Responsive design | 3 | the views keep to a phone's window inside Umbraco's 920 px page; the access matrix scrolls in its box |
| 4 | Theming | 3 | UUI's tokens throughout, walked in light and dark; the designer canvas keeps a small palette of its own for the visitor's preview |
| 5 | Implementation integrity | 3 | isolated issues, each found by the walk or the assessments and fixed with a test |
| **Total** | | **15/20** | **Good** |

## emil-design-eng

Read for how each change of this pass behaves as it happens, in the skill's Before/After form. None of them adds motion:
each happens once per visit or follows a person's own action, where a transition would only make the screen feel slower
(the skill's "should this animate at all?").

| Before | After | Why |
| --- | --- | --- |
| an action card's switch and "…" beside its text on a phone, past the window's edge | under the text on a narrow card, moved without motion | it moves only when the column's width changes; nobody watches a layout follow a resize |
| "Find a member instead" swapped the box for a "Choose a member" button, a second press away from the picker | the switch opens Umbraco's member picker at once, with Umbraco's own sidebar motion | one press for one intent; the sidebar slides in as every Umbraco sidebar does, nothing of ours on top |
| after pasting several questions, the last one selected and its inspector over a narrow canvas | nothing selected, the focus on the first new question, no motion | the result is what the editor came to see; an overlay sliding in over it hides the answer to "did it work?" |
| Add page selected the new page, so a narrow canvas showed its inspector instead | the new page shown with the focus on its Add field | the next press is the one the page was added for; the inspector waits until a field is chosen |
| „Vorlage speichern" breaking onto two lines in the template dialog's button | „Speichern" on one line | a button that wraps reads as two buttons for a moment; the title already names the object |
| Availability's dates and the retention days side by side past a phone's edge | one under the other, by the column's own container query | the layout answers the room it has, at once; nothing reflows while the person reads |
| Esc in the entries list dropped the focus to the page, so j and k stopped | the focus rests on the table, outlined, at once | a keyboard action is used many times a day: no motion, and the next key works where the last one did |
| the preview's Run actions switch turned back off by itself when a content action was refused | the note says why test entries run no actions, and the switch goes | a control that undoes itself reads as broken; one sentence in the place the person already reads explains it, and nothing moves |

## Kept as they are

- **Umbraco's own parts inside the section:** the forms tree's rows (axe's `label-content-name-mismatch`, UUI's menu
  item names every row "menuitem"), the open row's colours in the dark theme, and UUI's "select all" while some rows are
  ticked; each is Umbraco's or UUI's markup and in OWNER-TODO's report.
- **Umbraco's 920 px page on a phone** (ADR-0182, amended by ADR-0241 decision 2): the window shows the workspace's
  first part, and the column now keeps to it.
- **Umbraco's sidebar headline** ends a long name with an ellipsis, as every Umbraco sidebar does; the name is whole on
  the row the sidebar was opened from.
- **The Undo toast** offers Undo for the server's ten seconds, then says for six more that it is too late (IA 6.1); it
  is never longer than a minute, whatever a server's window.
- **Columns that read "none"** on every row: the copy guide's word for an empty value, so a column keeps its place and
  its meaning from one entry to the next.
- **Pasted questions are optional, and the email preview's sample answers name their fields:** the editor decides what
  is required, and a sample shows where each answer goes rather than invent one.

## Checks that ran

- **The walk** of every screen in the twelve looks, on the final build: every screen reached in eleven looks; in the
  twelfth the tree row of Copy actions once, before the walk looked again for Load more, then reached in that look's run
  again.
- **The five journeys** in the twelve looks: all twelve pass. The four at 1440 px ran a second time after the test
  host's SQLite database stopped answering partway through (ADR-0011), and passed.
- **axe** in every shot of both: nothing of the section's own; Umbraco's and UUI's parts as listed under "What the
  journeys measured".
- **The tests behind each fix** (unit, client, integration and browser, among them `top-tasks.spec`, `reflow.spec` and
  the phone column at 375 px) run in the phase review's gate with the full Playwright suite; the handover after Phase 16
  in `docs/WORKLOG.md` gives its numbers.
