# Step 10.2a: the Actions view, its cards and an action's sidebar

ROADMAP 10.2a shows a form's actions by trigger as cards that move, switch off, carry a rule and are duplicated, moved
to another trigger or deleted with Undo, and adds or changes an action in a sidebar whose first setting chooses what
happens among the action types and their presets ([ADR-0188](../decisions/0188-the-actions-view.md), building on
ADR-0185, ADR-0158, ADR-0181, ADR-0166 and ADR-0016). Per ADR-0019 and ADR-0020 the three design skills ran on the
result; the design comes from Phase 2 ([information-architecture.md](information-architecture.md), section 5.3, the
wireframe `actions.html` and [copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so
`impeccable`'s `shape` interview and every other choice were decided here or in the ADR. Test, the email editor and the
other editors, and the last run follow in 10.2b to 10.2d.

## What was looked at

The UX walk (`tests/e2e/specs/ux/walk.spec.ts`, ROADMAP 9.6a), run with `JFB_UX_ONLY=actions,action-,view-coming` on
the e2e host in Edge: at 375, 768 and 1440 px, in Umbraco's light and dark themes, in English and German, each shot with
what axe and the layout measures found. Its form is a copy of the demo's contact form with its two emails; the walk's
coming screen moved to History, the next view still to come. A look of the scratch kind shot the "…" menu and the view
of a designer without Manage actions.

| Shot | What it shows |
| --- | --- |
| [the view](review-10.2a/actions-1440.png) | Add action, then the four triggers, each with when it runs: Submitted with the demo's two emails, each card's name, its one line ("To team@lindenhall.example", "To the answer of Email address"), its switch and "…"; Approved "as each entry comes in, because this form has no manual approval" over "No actions here yet."; Rejected and Verified with the setting each needs and a link to it |
| [the same, dark](review-10.2a/actions-1440-dark.png) | Umbraco's dark surfaces; the dashed lines of a trigger without actions stay quiet |
| [an action's sidebar, dark, German](review-10.2a/action-sidebar-1440-dark-de.png) | "Was passiert" keeps the action's type, said once under it; the name; "Wann sie läuft" with each trigger's line; "Platz in der Liste" among Gesendet's two; the email's settings from the inspector's editors; Aktion löschen, Abbrechen, Aktualisieren |
| [a new action on a phone](review-10.2a/action-new-375.png) | the seven choices of what happens with their icons, the name following the choice, then when it runs |
| [the "…" menu](review-10.2a/menu-1440.png) | Duplicate, Move to another trigger…, Delete beside the card; a card with a rule shows it as a chip |
| [read-only](review-10.2a/read-only-1440.png) | one line saying what changing the actions needs, no Add action, handles, switches or "…", and "Off" on an action that is off |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) on the view, an action's sidebar and a new action's in the twelve ways, 36 shots: nothing
  in the view's, the cards' or the sidebar's own elements but one false reading, below. Umbraco's tree reports `label-content-name-mismatch` on its
  menu items on every shot, as on every screen of the section, and once a contrast on its open row in the dark theme at
  375 px (ADR-0182, OWNER-TODO). The journeys run axe inside the view and the sidebar too, read-only included.
- **One false reading:** at 1440 px, once Place in the list joined the sidebar, axe found the email's switch "To the
  person who created the form" too small (`target-size`). The switch then sits at the edge of the sidebar's footer,
  which covers its lower half, and axe measures what is not covered. The look scrolled it into view and ran axe again in
  the three ways it shoots: nothing; the switch is 36 by 20 px with the room around it WCAG 2.5.8 asks of a smaller one.
- **The bundled detector** (`impeccable detect --json`): `[]` for the cards, the view and the sidebar.
- **The layout measures:** no box scrolls sideways inside the view or the sidebar, and no word is cut by an ellipsis at
  any width. The window scrolls sideways by 545 px at 375 and 152 px at 768, Umbraco's 920 px page (ADR-0182).
- **The budgets:**
  - The backoffice at start is 62.10 KB of 64 (gzip): 0.56 more than after 10.1c, for the names of the choices and the
    rules builder's words for an action, which the start tables hold. The view's own words load with it, and the view
    and its sidebar with their first use. The bundle is 306.26 KB of 512.
  - The runtime is untouched: 59.81 KB of 60 with every feature.
- **The journeys**, below.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors deciding what happens after a
submission, calm and native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 1 / 5, as for 10.1a to
10.1c. What it still asks held:

- **One look:** Umbraco's surfaces, UUI's controls and icons, and the outcome list's grammar for a card (a handle, the
  name as the button that opens it, one line under it, the switch and "…" at its end). No colour of the package's own;
  a problem's count takes the check's colours.
- **Every state composed:** loading (three quiet blocks in the cards' place, said by the live region), a read that
  fails (one line and Try again), empty (one sentence that points at Add action), a trigger without actions, a trigger
  whose setting is off (one line with a link, its actions under it), read-only, and the most actions a form can have
  (a line in Add action's place).
- **Pre-flight:** no emoji, no gradient, no spinner in the content, no motion of its own; every string from copy.md's
  voice.

## impeccable

Operate mode, a refinement inside the incumbent world; the code, IA 5.3, the wireframe and copy.md were the context
(`impeccable context`, the bundled engine, no hooks or live mode). The `shape` brief, decided rather than asked: an
editor wiring up what a form does after it is sent wants to see at a glance what runs when, in which order, what is off
and what each action does in plain words; to add the common action in two presses; and never to wonder why an action
doesn't run. Critique against it and what the looks found:

| Finding | What was done |
| --- | --- |
| The wireframe shows Rejected and Verified without cards while their setting is off: an action moved there, or copied from another form, would vanish from sight | the cards show under the line, which says they don't run until the setting is on |
| The presets' names in the backoffice ("Notify the team", "Confirm to the sender", "Message to Slack", "Set state") were not IA 5.3's or copy.md's | IA's names, in English and German |
| A webhook had no choice of its own; a choice for each type and each preset would offer a blank email nobody needs | every preset, and a type without presets as itself; the webhook's presets start with a plain one |
| The choices of a webhook's target and format, the content action's parent and the state read as code ("Url", "Json", "SiteRoot") | labels in words, as the field types' choices have |
| A read-only sidebar listed settings whose editors come later, a recipient map as raw JSON | it shows the settings the editable sidebar shows |
| A card counted a hint as a problem: "1 problem" for a reply-to field the sidebar cannot show yet | a card counts errors and warnings, as an outcome's row does; a hint shows in the sidebar, under its box |
| The wireframe's read-only line names a folder | it names the permission only: the grant that decides may be global or a folder's (ADR-0188) |
| A link to an action on a form that opens with it lost its focus to Umbraco's name box, which takes the focus as a workspace opens | the link waits until the name box has held the focus past its own second look (`name-focus.ts`) |

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | the handle shows on hover and on focus, at once, and always on touch | cards move often; a handle that fades in slows every move |
| — | a keyboard move redraws the list at once, keeps the focus on the handle and says the new place | keyboard moves repeat; motion would trail the key |
| — | while dragging, the card dims and a line shows where it lands | the place is seen before letting go |
| — | the switch acts at once as one step to undo, and the live region says it | a switch used often needs no question |
| — | Delete acts at once, with Undo in the toast | a step that can be taken back needs no confirmation |
| — | the "…" menu is Umbraco's popover, with the focus on its first item | the backoffice's own menus behave the same |

## The words

English and German for every string, in `lang/actions.en.ts` and `actions.de.ts`, which load with the view; the names
of the types, the presets and the choices stay in the start tables (`field-types.*.ts`), as anything that names an
action needs them. copy.md has the triggers' lines, the read-only line, the card's menu, the toasts and the live
region's words, and the sidebar's help. German puts a comma before a "wenn" clause and none before "bei jedem …",
which the words' test holds.

## Kept as they are

- **Umbraco's 920 px page** on a phone, and its dark theme's colours on the tree's open row, as every section
  (ADR-0182).
- **Settings whose editors come later** are not drawn: a field picker, the recipient map, the webhook's field map and
  headers, the content action's mapping and the email's layout (10.2b, 10.2c). "Confirm to the visitor" therefore
  cannot choose the visitor's address field yet; 10.2b gives it its editor.
- **Test, the last run and its Details** are 10.2b's and 10.2d's.
- **The demo's own action names** ("Notify the team", "Confirm to the sender") are the demo's content, which an editor
  names as they like.

## A reader's review

A reader who had not seen the work read the four commits against the step, the ADR, IA 5.3 and copy.md. They found
the order of the actions, the problems counted on the cards, Fix it, the server's changes and the drawing of the
definition's texts sound (nothing of it is drawn as HTML), and twelve findings. Each was fixed before the tamper round's
second run, with a test:

1. **No way to order the actions with a pointer but dragging** (WCAG 2.5.7), which 10.1b had fixed for outcomes. The
   sidebar has the outcome's Place in the list, among the actions of its trigger; Update says where the action went.
2. **Add or Update could do nothing, with no word:** a value typed in a setting another choice took away kept its
   problem, and a problem of a setting whose editor comes later had no box to show under. Another choice now takes the
   typed problems of the settings it replaces, More options stay open over one, and a problem of a setting without
   its editor shows under What happens, by the setting's name, where Fix it leads too.
3. **Duplicate made a copy past the most actions a form can have,** and the focus then had nowhere to go. "…" offers
   no Duplicate at the limit, and the focus goes to the first card where Add action is not offered.
4. **The toast's Undo stayed after a later change** and then did nothing. A change closes it, as the Design view's does.
5. **Ctrl+Z dropped the focus** when it took away the card that held it. Undo and Redo keep the focus on the card, or
   put it on Add action.
6. **A link's `?action=` waited five seconds** for a user without `design`, who has no name box, and could take the
   focus after the user had moved on. A key or a pointer pressed first now ends it, and at its end it leads nowhere for
   who may not design the form, who has no Actions view; the wait itself stays, as the workspace learns who may design
   only once the form is read.
7. **An email routed by a choice** said "No recipient yet". It says "To the addresses an answer chooses".
8. **A new action opened with an error** ("A mail needs someone to go to.") before anything was typed. A new action's
   problems show once their box is left or Add is pressed.
9. **Three tests held nothing:** a read-only change "changing nothing" asserted nothing, and two said "with the focus on
   it" without looking. They look now, and the new behaviours above have their tests.
10. **Copy:** a trigger without actions said when it runs twice, in its heading and its line, which now says "No
    actions here yet."; the rule's words ran into "The rule is off" without a full stop; a site's own type without a
    name of its own showed its type as its name and its line; and the switch's state was said twice, by the switch and
    the live region, which no longer repeats it.
11. **German:** a name that reads as a verb ("Team per E-Mail benachrichtigen") ran into the words around it ("…
    verwenden", "… ausführen, wenn"); a name now stands in quotes. The read-only line and the triggers' "Dafür braucht
    es" read better as "Zum Ändern dieser Aktionen ist … nötig" and "Dafür ist … nötig".
12. **Dead code and a mismatch:** `approvesEachEntry` was the tests' alone, and the view now says Approved by it; the
    sidebar's `trigger`, which the view never passed, is gone; and the sidebar asked the check with an action of
    another trigger in its old place, while Update put it at the end of the new trigger's. Both now keep it by one
    rule, `keptIn`.

## The tamper round

In throwaway clones (ADR-0117), each change took back one rule of the step, one at a time, after every check had
passed untampered, each under a time limit and with the file restored after it.

- **The first round,** on `8dd112d3`, had 47: the model (15), the cards (4), the view (8), the sidebar (9), the rules
  builder (2), Fix it and links (5), the German words (1) and the server (3). All 47 were caught on the first run.
- **The second round,** on `9096b8ca`, had 24 on the reader's findings: the place and the keeping rule (6), the cards
  (2), the view's toasts, focus and limit (5), the sidebar's problems (5), the wait for the name box (3), a routed
  email's line, an unnamed type and the German quotes (3). It caught 23. The one missed handed back no place from the
  sidebar: the test changed the trigger, which resets the place, before it pressed Update. It now presses Update with
  the place chosen first, and the tamper, run again on that test, is caught.

The drag, the sidebar's focus as it opens, and the link's hand-over from the workspace to the view are the browser's:
the journeys hold them.

## What the journeys found

`form-actions.spec.ts` has three journeys:

1. On a form without actions, as a designer: the empty line, then Add action, ready to email the team, with the preset's
   subject. Add is kept back with the focus on To and the check's "A mail needs someone to go to." under it; with an
   address the card comes in, says "To sales@example.com", takes the focus, and the live region says where it runs. A
   second action, Change the entry's state, replaces the email's settings with its own and takes a typed name. Alt and
   Up move it above the email, said with its place and the focus on its handle; its handle drags it back below. Its
   sidebar's Place in the list puts it first without dragging, said as a move, and Ctrl+Z takes that back with the focus
   on its card. Its switch turns it off. The email gets a rule in the rules builder ("Run Email the team when"), shown on its card as a
   chip. Delete in "…" takes the second away, and Undo in the toast brings it back with the focus on it. Save draft
   keeps both as the server writes them, in their order, the second off and the first with its `run` rule. Axe runs
   inside the view and the sidebar.
2. As a layout designer, a new persona with `design` alone: the read-only line, no Add action, switch, handle or "…",
   "Off" on the action that is off, and a sidebar that shows the settings as they are with Close only, after which the
   focus is back on the card. Axe runs inside both.
3. A webhook to a host the site does not allow: the Design view's check counts it, and its Fix it opens the Actions
   view with the webhook's sidebar, the focus in Address and the check's words under it; Cancel leaves the card with
   "1 problem" in front of its line and the focus on it.

The library's copy-actions journey now follows its result's link: the form copied to opens at its Actions view with the
focus on the copy's card.

The first runs found three things:

- **The page's live region** was looked for as the last of the view's, which is a list's; the journey now takes the one
  after the page.
- **The link's card lost its focus** to Umbraco's name box, which takes the focus as a workspace opens and looks 100 ms
  later whether it holds it, taking it again where its first try was still on its way. A wait for the box's first focus
  was not enough: a run of the scratch kind that followed the focus every 100 ms found the card focused, then the name
  box again on that second look, in one run of two. The link now waits until the box has held the focus 250 ms
  (`name-focus.ts`, with its tests). A check after the reader's review that asked who may design before the form was
  read stopped every link; it now asks once the wait is over.
- **A card counted the check's hint** about a reply-to field as a problem, which an outcome's row does not. Cards now
  leave hints to the sidebar, and the view's test holds it.

The last run had the three journeys and the copy-actions one run twice each: all eight passed.

The walk's coming screen moved from Actions to History; the gate runs every other journey.

## The gate

The gate runs on the commit of this review in a throwaway clone (ADR-0117), with its clean clones and the whole browser
suite; WORKLOG has its result.
