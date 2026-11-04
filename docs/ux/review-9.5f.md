# Step 9.5f: the form check in the designer

ROADMAP 9.5f brings the server's form check into the designer: a badge in the canvas's toolbar that counts the problems
("3 problems", "No problems"), a sidebar that lists them errors first, each with Fix it, which selects the element and
puts the focus on its setting, opens the rules builder on a rule, or opens the palette for a form without fields; and
the publish dialog showing the check's result, errors blocking and warnings not
([ADR-0181](../decisions/0181-the-form-check-in-the-designer.md), building on ADR-0170, ADR-0175, ADR-0177, ADR-0178,
ADR-0179 and ADR-0180). Per ADR-0019 and ADR-0020 the three design skills ran on the result; the design comes from
Phase 2 ([information-architecture.md](information-architecture.md), sections 5.1 and 5.7, the wireframes
`form-check.html` and `publish-dialog.html`, and [copy.md](copy.md)). The owner answers no questions during the run
(CLAUDE.md rule 8), so every choice was decided here or in the ADR.

## What was looked at

The e2e host (the demo on `127.0.0.1:5520`, ADR-0031) in Edge through Playwright, as the designer, with a form made
through the API whose check finds one of each: a short text without a label (an error), a field labelled "Card number"
and a long text that one rule hides and another requires for the same answer (warnings), and an email address on a form
without a consent field (a suggestion). A second form starts blank, then gets a short text labelled "Card number", then
"Order reference".

| Shot | What it shows |
| --- | --- |
| [the badge](review-9.5f/toolbar-1440.png) | "3 problems" at the toolbar's end, before its "...", the alert in the danger colour |
| [on the canvas](review-9.5f/canvas-1440.png) | the badge in its place above the form |
| [the sidebar](review-9.5f/sidebar-1440.png) | "1 error" with "Blocks publishing", "2 warnings", "1 suggestion", each row the element's name, its sentence and Fix it |
| [Fix it](review-9.5f/fixed-1440.png) | the field without a label selected, the inspector beside it with the focus in Label |
| [a blank form](review-9.5f/sidebar-empty-form.png) | its one error, "Add at least one field" |
| [its Fix it](review-9.5f/fixed-palette.png) | the palette's search, for the end of the page |
| [Publish, an error](review-9.5f/publish-errors-1440.png) | "This form can't be published yet: 1 error needs fixing." with Fix it, the warnings under it, Publish off |
| [Publish, a warning](review-9.5f/publish-warning.png) | "The form check found 1 warning. It doesn't block publishing.", Publish on |
| [Publish, none](review-9.5f/publish-clear.png) | "The form check found no problems." |
| [a warning's badge](review-9.5f/badge-warning.png) | "1 problem", the info sign in the warning colour |
| [no problems](review-9.5f/badge-clear.png) | "No problems" with a check in the positive colour; [its sidebar](review-9.5f/sidebar-clear.png) says the form can be published |
| [at 768 px](review-9.5f/sidebar-768.png) | the sidebar over the canvas |
| [on a phone](review-9.5f/sidebar-phone.png) | Fix it under each sentence; [the dialog](review-9.5f/publish-errors-phone.png) the whole width |
| [dark](review-9.5f/sidebar-dark.png) | Umbraco's dark theme; [the dialog](review-9.5f/publish-errors-dark.png) |
| [high contrast](review-9.5f/sidebar-contrast.png) | Umbraco's high-contrast theme |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) on the sidebar (a form's problems, a blank form's, none) and the publish dialog (errors,
  a warning, none) at 1440 and 768 px, on a phone, in the dark theme and in high contrast: none.
- **The bundled detector** (`impeccable detect --json`) on the sidebar, the publish dialog, the canvas and its styles,
  the inspector and the Design view: `[]` for each, after the change below.
- **Widths:** the badge is 116 by 36 px at every width, the toolbar's "..." right after it; no row of the sidebar is
  wider than its box at 1440, 768 or 375 px.
- **The journeys** of `designer-form-check.spec.ts`, below.

The look ran again after the reader's fixes below, with the same result; the shots are from that run. Its only console
errors were the two of Umbraco's own token request as the backoffice signs in, which every run shows.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors finding what keeps a form from going
live, calm and native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 2 / 5. What it still asks
held: no colour of its own (the badge's and the rows' icons are Umbraco's danger, warning and positive colours, the
text the default colour, so colour is never the only sign), one radius scale (UUI's), Umbraco's own sidebar, boxes and
buttons, every state composed (checking, an answer awaited again, no answer with Try again, no problems, a blank form,
errors, warnings, suggestions), and no motion of its own.

## impeccable

Operate mode, a refinement inside the incumbent world; no PRODUCT.md, so the code, the IA and copy.md were the context.
Critique against IA 5.1 and 5.7, the wireframes and copy.md, and what the look in Edge found:

| Finding | What was done |
| --- | --- |
| The publish dialog's notes carried a thick coloured border on one side, which the detector names as a tell ([before](review-9.5f/publish-errors-before-1440.png)) | a quiet frame as the wireframes' note, its weight said by the icon beside its first line, the problems under the line's words |
| The suggestions' icon, Umbraco's `icon-lightbulb`, draws a bulb struck through, which reads as "switched off" | the plain bulb, `icon-lightbulb-active` |
| On a phone the sentence shared its row with Fix it and ran to a word a line ([before](review-9.5f/sidebar-phone-before.png)) | under 400 px of list, Fix it goes under the sentence |
| IA 5.1 counts a blank form's one error, "Add at least one field", but no check of 9.5b to 9.5e found it | a server check, `fields_missing`, so the badge, the sidebar and publishing agree (ADR-0181) |
| Hints are suggestions an editor may rightly decline; counted, a form with only suggestions could never read "No problems" | listed in a third box, "1 suggestion", and not counted |

**Noticed for 9.6:** on a phone the Design view keeps Umbraco's workspace at its narrowest, 620 px, so the toolbar and
its badge are reached by scrolling sideways, as every Design view screen before this step; the sidebar and the dialog
fit the width.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | the badge changes without motion, and says nothing aloud | it changes after every pause in typing; motion or an announcement each time would pull the eye and the ear away from the field being typed in |
| — | the badge keeps the last answer while the next is awaited | a count that flickers to "Checking…" and back at every pause would look broken |
| — | the sidebar and the dialog are Umbraco's own, with their own slide and fade | an editor knows them; a motion of ours inside would compete |
| — | a Fix it acts once the sidebar or the dialog is gone | both give the focus back to what opened them as they close, which would take it from the box Fix it chose |
| `border-inline-start: 4px` on the dialog's notes | a 1px frame and the icon | the side tab shouts; the icon says the weight once |
| Fix it beside a sentence on a phone | under it | a sentence of a word a line is hard to read; the button loses nothing below |

## A reader's review

A reader who had not seen the work read the change against the step, the wireframes and ADR-0181. Every finding was
fixed before the tamper round, each with a test:

- **The language of a text.** Fix it on a label in German opened the label in the language the designer showed. Fix it
  now carries the language of the text it is about, the default's for a text as a whole, and the Design view switches
  to it first on a site with more than one language.
- **A text setting read as a nested one.** A field type's text setting was read as a nested setting, its language as a
  setting's name. Fix it now asks the field types which one it is.
- **A grid row's validation rule.** The words named a grid row's rule by its grid, but Fix it led nowhere. Both now
  share one lookup, `messageFieldOf`.
- **Undo and selection in the pause.** An undo within the pause still had the change it took back asked about, and a
  change of the selection, which hands over the same form, started the pause over. Any other form now ends the wait for
  the one before, and the same form keeps it counting.
- **The palette over the inspector.** Fix it left the palette open over the canvas, with the inspector under it; it now
  closes first.
- **The dialog's answer unread.** The publish dialog replaced its status line at each state, so a screen reader read
  "Checking the form…" and never the answer. One region now holds the wait and then the answer.
- **A warning by colour alone.** The badge drew a warning with the error's icon; it now has the info icon, as in the
  sidebar.
- **The button texts unnamed.** A merge tag in a button's text was never said to be in "Button text": the place looked
  for a step the path doesn't have. It now reads the submit, next and back texts by name.
- **An empty alias.** A field without an alias read `"" can't be an alias`; it now says the field has no alias.
- **"Too long".** The sentence blamed the tag; the server's limit is on the whole text, which the sentence now says.
- **Settings from the dialog.** Fix it to Settings or Actions waited for the dialog to close and went by the Design view;
  it now opens the view at once, which the journey follows.

The German sentences lost their comma splices on the way.

## What the journeys found

`designer-form-check.spec.ts` counts a form's problems on the badge ("4 problems", the suggestion listed but not
counted), lists them errors first with axe finding nothing, follows Fix it on the error to the label's box, where a label
takes the badge to "3 problems" in the warning's look, and on a rule's warning to the rules builder on that rule, and
from the publish dialog, which says the three warnings don't block, follows the theme's Fix it to Settings. It has a
blank form read "1 problem", whose Fix it opens the palette, block Publish with its error and Fix it, and publish with a
card number's warning alone. Both passed at the first run.

The rerun after the reader's fixes failed once in three: after the publish dialog's Fix it, the palette's search lost
the focus it had been given. A focus log showed why: the form opens at its own address, without `/view/design`, and the
move to Design made Umbraco's router build the view anew, dropping the focus the old view had just placed; the new view
found no Fix it left to make. The move now reads the view an address shows (Design at the form's own address) and moves
only to another view, which a unit test holds.

## The tamper round

Each tamper is a change to the code that a test must catch by failing, run in a throwaway clone (ADR-0117) with a time
limit, the file restored after each.

- **The first round,** on 05745a2, caught all 66: JSON paths, every kind of Fix it, when the check asks, the words, the
  sidebar, the publish dialog, the badge, the Design view, the inspector's focus and the server's `fields_missing`.
- **The second round,** on b5411fc, ran 84: the first round's on the code as it now stands (one dropped with the count
  it tampered with), and 19 for the reader's fixes and the move between views: the language of a text, a title, a
  message, a check's message and a nested text; a text setting read as nested; the default language; the palette left
  open; the status region; the button texts and Back; an empty alias; the warning's icon; undo and selection in the
  pause; the view an address shows. It caught 83.

The one it missed removed the line in `changed()` that ended the wait on an undo, and no test could tell: `now()`
already answers the form it last asked about from its last answer. Worse, after a check that got no answer the line
kept an undo from asking again, which `now()` would have done. The line is gone; a test now has an undo ask again about
a form whose check got no answer, and putting the line back fails it.
