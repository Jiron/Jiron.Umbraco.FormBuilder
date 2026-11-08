# Step 12.3: templates, saved forms and saved groups

ROADMAP 12.1's part 12.3 adds nine built-in templates in English and German, Save as template… on a form, Save group
as template… on the canvas, the palette's Saved groups, and Library › Templates
([ADR-0217](../decisions/0217-templates-saved-forms-and-groups.md), building on ADR-0169 for the canvas's "…" and
ADR-0214 for the library). Per ADR-0019 and ADR-0020 the three design skills ran on the result. The design comes from
Phase 2 ([information-architecture.md](information-architecture.md), sections 4, 5 and 7, and [copy.md](copy.md)). The
owner answers no questions during the run (CLAUDE.md rule 8), so `impeccable`'s `shape` interview and every other
choice were decided here or in the ADR.

## What was looked at

The browser test `tests/e2e/specs/backoffice/templates.spec.ts` ran on the e2e host in Edge, in English, in Umbraco's
light theme. It shot each screen at 1440 px and at Umbraco's narrowest page, 920 px (ADR-0182).

| Shot | What it shows |
| --- | --- |
| [the gallery](review-12.3/gallery-contact.png) | Choose a template with the nine built-in templates as cards, each with its name and one line; Contact is chosen, and its preview beside the cards says "1 page, 3 fields" over Name, Email address and Message |
| [event registration](review-12.3/gallery-event-registration.png) | the sessions with their places as cards with a line each, lunch as Yes and No, the diets as checkboxes with Other |
| [feedback](review-12.3/gallery-feedback.png) | the 0 to 10 scale with its two end labels, the optional reason, and the grid of four ratings for four rows |
| [privacy request](review-12.3/gallery-privacy-request.png) | the last card chosen, its preview in view from the top: what the request does and why the address is confirmed, the two choices, the name's two parts, the address with its help, and the optional box |
| [the gallery at 920 px](review-12.3/gallery-narrow.png) | the cards alone with Preview under them; the footer keeps Cancel and Use template |
| [Save group as template](review-12.3/save-group-as-template.png) | the group Delivery chosen, the inspector saying when it shows, and the dialog "Save Delivery as a template", 520 px wide: the name, the optional description, the line saying what the template holds and what stays out, Cancel and Save template |
| [the palette's saved groups](review-12.3/the-palette-with-a-saved-group.png) | Saved groups after the field types' parts, its group with the stack icon, then Paste questions… |
| [Library › Templates](review-12.3/library-templates.png) | Built in, then Saved forms with the chosen Enquiry and its description; beside them the preview with the name, "…", Use template, "1 page, 3 fields" and the form |
| [Library › Templates at 920 px](review-12.3/library-templates-narrow.png) | one column: the cards, then the chosen template's name with Use template, its line and its preview |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) inside the name dialog, Library › Templates and the gallery: nothing.
- **The bundled detector** (`impeccable detect --json`) over the Templates screen, the name dialog, the gallery, the
  palette and the canvas: `[]`.
- **The budgets:** the backoffice at start is 43.17 KB of 64 (gzip), 0.26 more for the designer's new words in its
  start tables; the screen and the dialog load with their first use. The bundle is 437.85 KB of 512 and its largest
  file 26.17 KB of 64.
- **The form check:** an integration test makes a form from each of the nine templates, finds no error or warning of
  the form check in any of them, and publishes each one.
- **The journeys:**
  - a group saved from the canvas and inserted from another form's palette, as a copy with new keys, a free alias, its
    rule on its own field and the validation rule on its note, while the rule and the validation rule that named a
    field outside stay out, and the form it came from keeps both;
  - a form saved as a template from its "…" in the tree, then found, used (the tree lists the new form), renamed and
    deleted in Library › Templates.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors who keep a good form or block for the
next one, calm and native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 1 / 5, as for the other
library screens. What it still asks held:

- **One look:** Library › Templates draws its cards as the gallery does (the radio, the name, one line), Umbraco's
  popover for "…", UUI's buttons and the form preview the gallery and the designer use. No colour of the package's
  own.
- **Every state composed:**
  - loading: quiet lines in the list's place, which the screen reader hears as "Loading the templates";
  - a read that fails: one line;
  - without saved templates, Built in alone;
  - a group: its preview with the line on where it is used, in Use template's place.
- **Pre-flight:** no emoji, no gradient, no spinner in the content; the only motion is the cards' press and their
  border, as the gallery's.

## impeccable

Operate mode, a refinement inside the incumbent world. The code, IA 4, 5 and 7, ADR-0217 and copy.md were the context
(`impeccable context`, the bundled engine, no hooks or live mode).

The `shape` brief, decided rather than asked: an editor who built a good form or a good block wants to:

- keep it for the next one in two presses;
- find it again where they start a form or add a field;
- never wonder what a template holds, what it leaves out, or what deleting it changes.

Critique against it and what the looks found:

| Finding | What was done |
| --- | --- |
| On Library › Templates a card's radio sat between the name and its line: a native radio stretched over both rows | the radio sits beside the name, as the gallery's does |
| At 820 px the screen looked cut at its right edge | Umbraco's page is never narrower than 920 px, and below that the whole backoffice scrolls sideways (ADR-0182). The narrow shots are taken at 920 px, where nothing is cut |
| In the gallery, a template chosen low in the list showed its preview from the middle, its start scrolled away with the cards | beside the cards the preview stays in view, as the Templates screen's does; the test checks that the preview's first line is in view after the last card is chosen |
| A group whose rule names a field outside it would carry a rule the next form cannot meet | the rule stays out whole, a validation rule too; the dialog says so before saving and the toast counts what stayed out (ADR-0217) |
| A form saved as a template while its draft has changes not saved yet | the stored draft is saved; in the form's workspace with changes not saved yet, the dialog says those stay out, and from the tree it says nothing of them (ADR-0217) |
| The dialog's line, once it named the validation rules, widened the dialog to about 1050 px | the dialog is 520 px wide, as the duplicate's, and the line wraps; the test holds the width |
| Where a saved group shows once saved | the palette's Saved groups, after the field types, and the palette's search finds it; the save's toast says "Any form can insert it from the palette.", and a group's preview in the library says where it is used |
| A Create button on Library › Templates would make an empty template nobody needs | none: templates come from Save as template… on a form and Save group as template… on a group |
| Deleting a template might seem to touch the forms made from it | the question says that forms made from it, and groups inserted from it, stay as they are |

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| a card on Library › Templates without press feedback | `transform: scale(0.99)` on `:active`, 120 ms ease-out, none with reduced motion | the press is felt, as on the gallery's cards |
| the radio stretched between a card's two lines | `align-self: start` beside the name | the eye reads the name and its choice as one |
| the gallery's preview scrolled away with the cards | `position: sticky; top: 0` beside the cards | a choice low in the list shows its result without a scroll back up |
| — | the border and the background change over 120 ms on hover and on choice, the colours only | a choice made often stays calm |
| — | "…" is Umbraco's popover, rename and save share one small dialog, and delete asks first | the backoffice's own menus behave the same, and a deleted template can't be brought back |

## The words

English and German for every string: the screen's, the dialog's and Save as template… in `lang/library.en.ts` and
`library.de.ts`, and the canvas's, the palette's and the save's toasts in `lang/designer.en.ts` and `designer.de.ts`.
The nine templates' own texts (labels, help, options, outcomes, emails) are in both languages in their JSON, which a
unit test holds. copy.md has the buttons, the delete question, the toasts and the dialogs' lines.

## A reader's review

A reader who had not seen the work read the uncommitted client against the step, ADR-0217, IA and copy.md. They found
the key renewal and the alias freeing of a copy, the source group left untouched, the refusal of a type a form has
once, the popover ids and the permissions sound, and eleven findings. Ten were fixed with a test, and one is kept with
its reason:

1. **Over a narrow canvas a group could not be saved:** the inspector takes the element's actions there, and its "…"
   had no Save group as template. It has now, for who may save groups.
2. **A saved group without fields could not be inserted:** the server's canonical form leaves out empty lists, and the
   copy read a group's `fields` that was not there. The palette reads a saved group as the designer reads any
   definition.
3. **Use template did not list the new form in the tree** until a reload. The tree's top reads its children again
   before the form opens, as every other way of making a form does.
4. **The focus was lost after a delete,** and the choice jumped to the top. The next template of the deleted one's part
   is chosen and focused, else the one before it; after a rename the focus is back on the "…".
5. **A rule that named a field outside kept its other conditions,** so it could show a field more often than before
   while the toast said it was left out. A rule or a validation rule naming a field outside is now left out whole, and
   one without conditions stays (ADR-0217, decision 7).
6. **The validation rules of the group's fields were lost.** A group now goes with the form's validation rules on its
   fields, those naming a field outside left out and counted, and the copy brings them on its own fields in the same
   Undo step.
7. **Merge tags inside a copy kept the old aliases,** so a copy's text could show another field's answer after its own
   field became `email2`. Merge tags are repointed as calculations are, for Duplicate too. A tag or a calculation that
   names a field outside the group is kept: where it lands, a field of that alias answers, as for a tag typed there,
   and the form check reports one no field has (ADR-0217, decision 7).
8. **A refused group said only the server's English title.** It now says, in the backoffice's language, that the form
   check finds a problem in the group.
9. **Use template was offered to who could not use it:** making a form at the top of the forms takes `design` there.
   The button shows only then.
10. **The line on changes not saved yet showed every time,** from the tree too, where nothing is unsaved. It shows in
    the form's workspace while it has changes not saved yet, as ADR-0217 says.
11. **Tests that held little:** the menus of the canvas and the inspector, the delete's next choice and focus, the
    rename's focus, the tree's reload and the line on unsaved changes have tests now, and the German toast names "die
    Gruppe" rather than a pronoun that may not fit the name.

## Kept as they are

- **Umbraco's 920 px page** below that width (ADR-0182).
- **At 920 px the Templates screen's preview follows the cards** rather than becoming a step of its own as in the
  gallery. The screen is a page that scrolls, and Use template sits in the preview's head. The gallery's footer is
  fixed, and its Preview step keeps the choice and its result apart on a narrow dialog.
- **Saved groups come last in the palette,** after the field types: the palette is for adding fields, and the search
  finds a saved group by its name as it finds a type.
- **German and the dark theme** are not shot here. The words' test holds the German, and the phase review (12.6) walks
  the library screens in both themes and both languages.
