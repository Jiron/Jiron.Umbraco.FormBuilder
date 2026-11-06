# Step 10.2b: the email editor and its preview

ROADMAP 10.2b gives the email action its own editors in its sidebar: one To box for every kind of recipient, Copy to and
Blind copy to as address chips, fields picked by their kind, the layouts, Insert merge tag for mail, and a live preview
of the mail with a sample entry beside the settings
([ADR-0189](../decisions/0189-the-email-editor.md), building on ADR-0188, ADR-0187, ADR-0173 and ADR-0159). Per
ADR-0019 and ADR-0020 the three design skills ran on the result; the design comes from Phase 2
([information-architecture.md](information-architecture.md), section 5.3, the wireframe `email-editor.html` and
[copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every choice the skills would
have asked about was decided here or in the ADR. Test follows in 10.2c.

## What was looked at

A look of the scratch kind in Edge, on a form with a name, an email address, a topic of three options and a message,
and two emails: one to the team (two addresses, the person who created the form, the addresses Topic chooses, a copy,
and replies to the email field's answer), one to the visitor (the email field's answer alone). It shot the email's
sidebar at 1440 px in Umbraco's light and dark themes, at 920 px and at 375 px, and at 1440 px Add recipient open, an
address refused, the preview in dark, More options open and the visitor's mail. The UX walk
(`tests/e2e/specs/ux/walk.spec.ts`), run with `JFB_UX_ONLY=actions,action-`, shot the Actions view and an action's
sidebar at 375, 768 and 1440 px, light and dark, in English and German.

| Shot | What it shows |
| --- | --- |
| [the team's email](review-10.2b/email-1440-light.png) | the settings beside the preview: To with its address chips, the person who created the form and Addresses chosen by Topic as rows with their remove buttons, then Add recipient; Subject and Body with Insert merge tag; the preview's From, To, Copy to, Reply to and Subject over the mail in its frame |
| [the same, dark](review-10.2b/email-1440-dark.png) | Umbraco's dark surfaces around the mail, which keeps the colours of the scheme its switch chooses |
| [Add recipient](review-10.2b/email-add-1440-light.png) | An address with "Several at once: separate them with commas.", Add, and the kinds not there yet |
| [an address refused](review-10.2b/email-typed-1440-light.png) | 'Enter addresses such as hello@example.com: "sales" isn't one.' under the box it was typed in, the good address kept as a chip |
| [the mail in dark](review-10.2b/email-scheme-dark-1440-light.png) | Dark pressed in Umbraco's selection colours, the layout's dark colours in the frame |
| [More options](review-10.2b/email-more-1440-light.png) | Send as plain text, Layout ("Site default (Card)", Card, Letter), Blind copy to, From address, Razor view, Show sensitive fields and Attach files, each picker offering only the fields of its kind |
| [the visitor's mail](review-10.2b/email-visitor-1440-light.png) | To says the answer's address, in a mail of its own, and the help "As the visitor gets it" |
| [one column](review-10.2b/email-920-light.png) and [its preview](review-10.2b/email-preview-920-light.png) | below a 960 px sidebar the preview follows the settings |
| [a phone](review-10.2b/email-375-light.png) and [its preview](review-10.2b/email-preview-375-light.png) | the rows and the routes in one column, the mail at the phone's width |
| [the walk's sidebar, dark, German](review-10.2b/action-sidebar-1440-dark-de.png) | the demo's email in German: "Vorschau mit einem Beispieleintrag", Hell pressed, "So bekommt das Team sie", An with its chip and Empfänger hinzufügen |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) on the sidebar in the four ways, with Add recipient open, an address refused, More
  options open and the visitor's mail, ten in all: nothing. The walk's 36 shots: nothing in the view's, the cards' or the
  sidebar's own elements; Umbraco's tree reports `label-content-name-mismatch` on its menu items on every shot, as on
  every screen of the section (ADR-0182, OWNER-TODO). Axe leaves the preview's frame alone (ADR-0189): it injects itself
  into every frame and waited without end for this one, where no script may run. The preview's journey runs axe on the
  mail itself, in a page of its own, in the light and the dark scheme: nothing. The journeys run axe inside the sidebar
  too.
- **The bundled detector** (`impeccable detect --json`) on the editors, the preview and the sidebar: `[]`.
- **The layout measures** of the walk: no box scrolls sideways inside the view or the sidebar, and no word is cut by an
  ellipsis at any width. The window scrolls sideways by 545 px at 375 and 152 px at 768, Umbraco's 920 px page
  (ADR-0182).
- **The budgets:**
  - The backoffice at start is 62.10 KB of 64 (gzip), as after 10.2a: the start tables' words for the new settings,
    and two labels made shorter on the way. The editors, the preview and their words load with the sidebar. The bundle
    is 315.26 KB of 512.
  - The runtime is untouched: 59.81 KB of 60 with every feature.
- **The journeys**, below.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors deciding who gets a mail and what it
says, calm and native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 1 / 5, as for 10.1a to 10.2a.
What it still asks held:

- **One look:** Umbraco's surfaces, UUI's buttons and icons, the inspector's boxes, and the chips of a list of values.
  The recipients' rows are bordered boxes on the surface colour, the chips take the package's round token, and the
  chosen scheme takes Umbraco's selection colours. No colour of the package's own.
- **Every state composed:** a preview that waits keeps the last mail and says it is busy (`aria-busy`); one that fails
  says so with Try again; a view the site lacks says which and what to do; a mail with no recipient yet says "No
  recipient yet"; a plain-text mail shows its text; a picker without a field of its kind says so in one line; a field
  or option that is gone stays visible, named so, to be removed.
- **Pre-flight:** no emoji, no gradient, no spinner, no motion of its own; every string from copy.md's voice.

## impeccable

Operate mode, a refinement inside the incumbent world; the code, IA 5.3, the wireframe and copy.md were the context
(`impeccable context`, the bundled engine, no hooks or live mode). The brief, decided rather than asked: an editor
setting up a form's mail wants to say who gets it in one place, write what it says with the answers in it, and see the
mail as it will arrive before saving, without knowing how the server stores any of it.

The critique scored the first look 25 of 40 on the heuristics:

| Heuristic | Score | What it found |
| --- | --- | --- |
| Visibility of status | 3 | the live preview; nothing says a newer one is on its way |
| Match with the real world | 3 | the mail's own words; "Razor view" and "the table" are the system's |
| User control | 3 | every recipient has its remove button; another field for Addresses chosen by dropped the options' addresses |
| Consistency | 2 | addresses as chips, other recipients as loose lines; "Razor view" and "mail view" for one thing |
| Error prevention | 3 | pickers by kind and a check of typed addresses; the field switch above |
| Recognition over recall | 3 | the preview; a merge tag's syntax |
| Flexibility | 2 | several addresses at once; the subject 1.5 screens down |
| Minimalist design | 2 | Blind copy to among the essentials |
| Error recovery | 2 | a refused address said away from its box |
| Help | 2 | no Learn more |

What was done:

| Finding | What was done |
| --- | --- |
| The mail's content came after Copy to and Blind copy to, its subject below the first screen | the order is who gets it, then what it says: To, Subject, Body, Leave out unanswered fields, Reply to, Copy to; Blind copy to under More options (ADR-0189) |
| A refused To address was said under the kinds, after Add recipient's panel, and the box did not point at it; an option's refused addresses were said at the select, the box neither invalid nor pointing at it | each box says its own problem under it, `aria-invalid` and `aria-describedby` on the box; Update that keeps back a problem in a closed Add recipient opens it |
| The person who created the form read as help text, its remove button 340 px away at 1440 | each recipient that is no address is a bordered row, its remove button at its end |
| "As the team gets it" over a mail only the visitor gets | the help says "As the visitor gets it" there |
| Four buttons named "Add" with the footer's | each address box's Add names what it adds to, Add address under Add recipient and Add to Copy to for another box, its word still Add |
| Another field for Addresses chosen by dropped the options' addresses at once | they stay with that field while the sidebar is open, and choosing it again brings them back |
| The routes went to one column by the window's width, the rest by the sidebar's | by the sidebar's (a container query) |
| The pressed scheme differed only by weight on a faint fill | Umbraco's selection colours |
| The preview said "mail view", its setting "Razor view" | the preview says Razor view, as its setting does |
| "Leave unanswered fields out of the table" named the layout's table | "Leave out unanswered fields" |
| The chips' radius was a value of their own | the package's round token |

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | the preview keeps the last mail while the next is asked, and asks once the changes pause | a preview that flashes on each key reads as broken; one that trails a pause reads as live |
| — | the frame is as tall as its mail, measured on load and when the sidebar's width changes | a frame that scrolls inside a scrolling sidebar hides half the mail |
| — | the Light/Dark switch redraws the frame at once, no transition | a switch looked at, not watched |
| — | Add recipient opens in place, as a disclosure, with no animation | it is used several times in a row while building the box |
| — | a removed chip hands the focus to the chip in its place, or the one before, or the box | a keyboard user removing several keeps their place |
| — | the mail's links go nowhere and are no stop of Tab | a preview is looked at; a link that opens a page from it would leave the work |

## The words

English and German for every string, in `lang/actions.en.ts` and `actions.de.ts`, which load with the sidebar; the
settings' labels stay in the start tables (`field-types.*.ts`), as the inspector and the form check name them too.
copy.md has the To box's kinds and buttons, the address box's help, the routes' help, the pickers' lines, the layouts,
the preview's lines and help, and the failures. German says "die Person, die das Formular sendet" for the visitor, as
the rest of the backoffice does.

## Kept as they are

- **The preview after the settings in one column**, below a 960 px sidebar, as the outcome's sidebar has it (10.1c).
  A switch between Settings and Preview would hide the settings while the mail shows, so a change and its mail would
  never be seen together either; and a sidebar of two columns is where the preview can stay in sight.
- **More options under its settings, its toggle after them,** as the inspector's: the toggle stays where it was
  pressed.
- **The plain-text mail's white sheet**, as the outcome's preview sheet: it is the mail's paper, not the backoffice's
  surface, in either theme.
- **The mail's own layout at a phone's width**, the email renderer's (ADR-0159), which a mail client draws the same.
- **A merge tag's syntax** stays the product's own (`{{ field.alias }}`); Insert merge tag offers every tag the mail
  may use, so none needs remembering.
- **Learn more links** come with the documentation's pages (ROADMAP 17.2a), from one map of page addresses.

## A reader's review

A reader who had not seen the work read the three work commits against the step, the ADR, IA 5.3 and copy.md. They
found the permission, the frame that runs nothing, the newest answer winning, the values the server reads and the
focus after adding and removing sound, and these findings. Each was fixed with a test, before the tamper round's second
run:

1. **The preview got a mail only a field's answer gets wrong:** it put the copies and the reply-to on the visitor's
   mail, which a run never does. The preview now routes a mail as a run does, from the same rule: the visitor's own
   mail carries no copy and no reply-to, and its copies are the team's mail of their own. Reading that rule showed a
   run's own flaw: the copies and the blind copies shared that mail's To, so a blind copy was seen. A blind copy now
   stays blind there, and blind copies alone each go alone (ADR-0160, amended).
2. **A refused address under an option kept Update back without a word** once the map went or another field was
   chosen, as its problem stayed with no box to show under, and another field's first option showed the old text. The
   options' boxes now take their problems with them, and a box shows the map's own value.
3. **Settings the type cannot read answered 500,** such as a draft written by 10.2a's sidebar, which wrote a typed
   subject as a bare string. The preview now holds them to the schema first, as the form check does, and answers
   `action_settings_invalid` (409), which it says in its words.
4. **The frame grew but never shrank,** as a frame's document is never shorter than the frame. It is measured from
   the least height now; the journey shortens the body and sees the frame follow.
5. **A setting within a box its editor does not draw** was drawn nowhere. The schema now refuses one within a box of
   another editor than `recipients`, one of a kind the recipients editor does not draw, and two of a kind; the editor
   finds the kinds by their editors.
6. **A preview logged a run's warnings** (no creator with an address, a missing view) each time the changes paused.
   It logs nothing now: nothing is sent.
7. **Accessibility:** the subject is read in the mail's language and direction; Add recipient, where Fix it lands, is
   described by the To box's problem; a field picker with nothing to pick labels no paragraph. The address boxes' Add
   buttons were named alike, which the look had found too.
8. **Tests:** the locked To box, the read-only layout and the empty picker have tests now. Axe still leaves frames
   alone in the backoffice's checks, so a test holds the preview's frame as the backoffice's only one, and the journey
   runs axe on the mail itself, in a page of its own, in each scheme.
9. **Copy:** "Adressen je nach Antwort"; the German visitor's help says who gets the mail first; the read-only To box
   lists one recipient a line, as its kinds are phrases; Reply to is named as the preview and IA name it; copy.md has
   the preview's lines, the frame's title and the switch's name; the ADR's Fix it lands where it does.
10. **Duplication:** the layout a gone default gives way to is one rule (`EmailLayouts.KeyOf`), and an unused id went.

The reader also pointed out that the preview names the form's creator by their address to whoever may design the form.
It is kept, and the ADR now says so: the mail goes to that address, and 10.2c's test mail names the same recipients.
That the preview repeats Copy to and Blind copy to beside their chips is IA's: the preview shows what the mail will
carry, routed, which the chips do not.

## The tamper round

In a throwaway clone (ADR-0117), each change took back one rule of the step, one at a time, after every check had passed
untampered, each under a time limit and with the file restored after it.

- **The first round,** on `85d5d923`, had 53. Its clone lacked the built client, whose website assets the Web assembly
  holds, so every integration check failed there untampered: its integration verdicts were set aside, and the clone
  builds the client first now. Of its other verdicts, one tamper was missed: a recipient map whose field is no text was
  read as a map, which the test of `readMap` now holds. Four broke the build rather than a test (a constant condition,
  a parameter left unread) and were written again.
- **The second round,** on `974bc934`, after the reader's fixes, had 74: the schema (6), the outbox (2), the preview
  (7), the layouts (2), the editors' values (5), the To box (11), the view (2), the preview's document and element (6),
  the design review's fixes (12), and the reader's findings, routing as a run routes it (10) and the editors and the
  preview (11). Every check passed untampered first. 72 were caught on the first run; two broke the build, each
  leaving a parameter unused, and, written again so they compile, both were caught.

The frame's height, a click on a link in the mail and axe on the mail itself are the browser's: the preview's journey
holds them.

## What the journeys found

`form-email.spec.ts` has three journeys:

1. **The recipient builder:** Add action starts ready to email the team, in the large sidebar. Two addresses typed at
   once become chips; one that is none keeps what was typed and says why under its box. The answer of an email field
   offers only the form's email field, and Addresses chosen by an answer the choice field with each option's box.
   Each kind is offered once; Save draft keeps the settings as the server writes them. Axe runs inside the sidebar.
2. **The preview:** beside the settings where the sidebar has room for two columns, the mail for the sample entry, its
   name in place of the body's merge tag, asked once the changes pause. A link in the mail opens no window and leaves
   the mail in place; a shorter body makes the frame shorter. Dark shows the layout's dark colours whatever the
   device's own scheme, and axe runs on the mail itself, in a page of its own, in the light and the dark scheme:
   nothing.
3. **Fix it:** a mail to the answer of a field that is no email field is counted by the Design view's check; its Fix it
   opens the email's sidebar at the field's picker, the problem under it, and choosing the email field mends it once
   the check asks again.

`form-actions.spec.ts` follows the To box: Add is kept back with the focus on Add recipient and the check's "A mail
needs someone to go to." under the box.

The first runs found three things:

- **A text field's sample answer is its label,** so the preview's name was the name field's label; the journey's form
  now has a field of the name type, whose sample is a name.
- **A typed subject was written as a bare string,** where the schema keeps a text per language: the server refused
  the draft ("A localised text is an object"), which 10.2a's sidebar had done too. The sidebar now writes the typed
  text into the language shown, the others kept, as the inspector does, with a test.
- **Copy to's Add was named "Add address" too,** as the To box's is: the look's press of it found two. Each box's Add
  now names what it adds to ("Add to Copy to"), with a test.

Before the reader's review, the three email journeys ran with the actions, outcomes, form check and library journeys:
all 25 passed. After its fixes, the email, actions and form check journeys ran again with the preview journey's new
checks: all 8 passed.

## The gate

The gate runs on the commit of this review in a throwaway clone (ADR-0117), with its clean clones and the whole browser
suite; WORKLOG has its result.
