# Step 9.4a: the inspector

ROADMAP 9.4a gives the designer its third column: what the selected field, group or page is, its sections Essentials,
Validation, Rules, Appearance and Advanced, the plain setting editors over the type's schema, the designer locks, the
alias with its rule, texts in every language of the site with the canvas's language switch and the chip of a field
without a text in it, the palette folded beside it up to 1024 px, and the element's "…"
([ADR-0170](../decisions/0170-the-inspector.md)). Per ADR-0019 and ADR-0020 the three design skills ran on the result;
the design comes from Phase 2 ([information-architecture.md](information-architecture.md), section 5.1, and
[copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every choice was decided here
or in the ADR.

## What was looked at

The e2e host (the demo on `127.0.0.1:5520`, ADR-0031) in Edge through Playwright, as the designer, with a form of a text,
a number and a date field made through the API, in English and German; and a host of its own whose configuration locks
the text type's maximum length and hides its prefix.

| Shot | What it shows |
| --- | --- |
| [a text field](review-9.4/inspector-text-1440.png) | "Short text field", Essentials open with the label, help, Required and placeholder; the other sections closed, each with its one line |
| [a value it can't keep](review-9.4/inspector-problem-1440.png) | "Enter 4000 or less." under Maximum length once the box is left, the box marked |
| [a closed section's line](review-9.4/inspector-summary-1440.png) | Validation closed: "Maximum length: 120" |
| [Advanced](review-9.4/inspector-advanced-1440.png) | the alias, the default value, Prefill's three sources, autocomplete, Sensitive and Include in emails with their help, what is kept, and "More options (1)" for the CSS class |
| [a number field's checks](review-9.4/inspector-number-more-1440.png) | seven items, then the rest under "Fewer options" |
| [a date's limits](review-9.4/inspector-date-1440.png) | Earliest date "From today", 2 weeks |
| [the element's "…"](review-9.4/element-more-1440.png) | Start a new page after this and Copy alias, the focus on the first |
| [the insert point above a selection](review-9.4/insert-beside-bar-1440.png) | the "+" alone at its start beside the handle, clear of the bar; the bar measured 300 px in English and 367 px in German on a 440 px field |
| [German](review-9.4/german-1440.png) | the language switch on German, the inspector's note, the label box empty with the English label as its placeholder, and "No German text yet" on the fields without one |
| [the locks](review-9.4/locks-1440.png) | Maximum length read-only with its value and configuration key, and no Text before |
| [at 1024 px](review-9.4/fold-1024.png) | the palette folded to its rail beside the inspector, the bar's buttons as icons on the narrow sheet |
| [at 920 px](review-9.4/overlay-920.png) | the narrowest workspace: the inspector over the canvas with the element's bar in its header |
| [dark](review-9.4/inspector-dark-1440.png) | Umbraco's dark theme around the visitor's white sheet; the inspector in its tokens |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA, what lies inside the designer's elements counted) on the inspector of a text, a number and
  a date field with each section and "More options" open, on the Design view in German with the chip, and on the locks
  host's inspector, in `designer-inspector.spec.ts` and `designer-locks.spec.ts`: none.
- **The bundled detector** (`impeccable detect --json`) on the inspector, its styles, the setting editors, the canvas's
  styles and a canvas field: `[]` for each.
- **The journeys:** every setting of the text, number and date types set and saved, Ctrl+Z after it; the fold at a
  1024 px window; a new page from a field's "…" with the keyboard only; a German label with the chip; an alias checked
  and changed; and the locks on their own host. With them the whole smoke and backoffice projects ran, 69 journeys,
  the 9.3 ones on the canvas the inspector narrows among them, and every persona's top bar, which reads "Form Builder"
  since the owner renamed the section during this step ([ADR-0171](../decisions/0171-the-section-is-called-form-builder.md)).

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors configuring a form's fields, calm and
native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 2 / 5. What it still asks held: one accent
(Umbraco's selection colour for a switch that is on), one radius scale (UUI's), every state composed (empty, set, a value
it can't keep, locked, hidden, another language, a type the site lacks), and no spinner where nothing waits.

## impeccable

Operate mode, a refinement inside the incumbent world. Critique against IA 5.1 and copy.md:

| Finding | What was done |
| --- | --- |
| A newly selected field opened its inspector where the last one had been scrolled to: the date's limits were below the fold | each element selected is shown from its top |
| At 1024 px the selected field's bar wrapped "Move to…" onto two lines on the narrowed sheet | the bar's buttons keep one line, and on a sheet of 430 px or less, or in a group with columns, show their icons alone, named as before; 430 px leaves the German bar clear of the handle |
| "Must match" inherited the nested-group rule and drew a line beside a single list box | the checks sit in a plain stack; only a fieldset's own items are indented |
| The element's "…" menu had menu roles that promise arrow keys it doesn't take | plain buttons in a popover, as the toolbar's "…"; the focus goes to the first as it opens, Escape gives it back to "…" |
| German "Platzhalter" named both a box's placeholder and a merge tag | the placeholder is "Hinweis im leeren Feld"; "Platzhalter" stays the merge tag, as the library says it |

Kept as they are:

- **"More options (1)"** in Advanced, for the CSS class: IA 5.1 says seven items, and the class is the setting changed
  least.
- **Every message box shown in Validation,** even a message for a check that can't fail yet: a rule (9.5a) can make a
  field required, so hiding the required message by Required alone would hide it where it is needed.
- **The 900 px window:** Umbraco keeps its workspace at 620 px at least, so below a 920 px window the workspace runs past
  the window's edge, the inspector's Close with it. That is the backoffice's frame, not the designer's.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | a section opens and closes at once | it opens many times a day, from the keyboard as often as not |
| — | the switch's knob moves in 160 ms with a strong ease-out, its colour in 120 ms; none with reduced motion | a state change the eye should follow, small and fast |
| — | the bar's buttons and "More options" scale to 0.97 on press, none with reduced motion | a press answers at once, as the canvas's tools do |
| — | the menu of "…" is Umbraco's popover, which doesn't animate | the backoffice's own popovers move as Umbraco's do |

## A reader's review

A reader who had not built the step read its diff for defects. Each finding below was traced in the code, fixed and given a
test:

| Finding | What was done |
| --- | --- |
| A text written in another language first was stored without its default, which the server needs in every text: the next save was refused. Emptying the default with a translation left had the same end | every text keeps its default `""`, empty where only another language has words; a text with nothing in any language is none |
| The in-place label on the canvas wrote the German words over the English while German was shown | it writes the shown language's own text, and Enter on the English words as shown writes none |
| A text was read from a sibling culture (`en-GB` for `en-US`), unlike the server, and written there | texts are read as the server resolves them: the culture, then its more general ones, then the default; the canvas's preview follows |
| Fewer columns for a group left a field wider than the group, which the server refuses | a wider field takes the group's new width in the same change |
| Ctrl+Z and Ctrl+Y did nothing on a switch or a list box in the inspector | only a box that takes text keeps its own Undo; on a switch or a list box they undo and redo with the focus kept |
| A box's value was written back while typed in, read the way the form keeps it: a space or a line break just typed went | a box is never rewritten while it has the focus or holds a value it couldn't keep |
| The alias's way back, after a cancelled question, could land in another field's box selected meanwhile | it puts back only its own field's alias, and the change uses the field as it is once the question is answered |
| Unticking a choice list's last box left it unticked while the type's standard choices applied | where the standard chooses some, one at least is needed, and the box says so |
| Folding or opening the inspector dropped the focus before the new mode was drawn | the focus moves once the view has drawn it |
| A problem stayed under a box after Undo put the form's value back | Undo and Redo forget the problems |
| A language chosen while the last one was still being read could lose to it | the answer for a language no longer chosen is dropped |
| While the field types were read, a selected field said its type was gone | the inspector shows a loader until they are read |
| A text length counted characters where the server counts UTF-16 units, so an emoji passed the inspector and failed the save | lengths count as the server and the browser do |

## What the earlier journeys found

The inspector narrows the canvas wherever something is selected, and four journeys of 9.3 failed on the narrower
canvas. Each failure was traced; three were the designer's, one the journey's:

| Failure | What was done |
| --- | --- |
| At 1440 px the selected field's bar covered the middle of the insert point above it, where its pill is: a click there pressed "Move to…", and a keyboard user's focus on that insert point was hidden (WCAG 2.4.11) | the insert point right above the selected element shows its "+" alone at its start, beside the handle, its name unchanged; in a group with columns every insert point does |
| A group's handle lay under the insert point below the last field of the group before it, so pressing the handle opened the palette | a group's handle, while it shows, lies above that insert point |
| Over a narrow canvas, a pick from the palette selected the new field while the palette still covered the inspector, so its heading never took the focus | the heading takes the focus once the palette has closed |
| "Label" named both the canvas's box and the inspector's | the journey asks the canvas for its box |

## The tamper round

In a throwaway clone (ADR-0117), each of 60 changes to the inspector, its values and model, the setting editors, the
Design view, the canvas and the history was run against the Vitest file that should notice it, and each of three
changes to the server against its integration test: the languages as Umbraco lists them, and a form that never or
always has entries. A change counted as caught only where a test failed. The first round caught all three on the
server and 53 on the client; five changes went unnoticed, each a gap now closed with a test:

| Unnoticed | The test now |
| --- | --- |
| eight items before "More options" | the test of the split read the number from the code; it now says seven, as IA 5.1 does |
| a hidden lock drawn inside nested settings | the email's domains with their mode locked and hidden show the list and no mode |
| the default's text as the placeholder of its own box | in the default language the label's and the prefix's boxes have no placeholder; in German the prefix's shows the English one |
| a chip in the default language, on a field written in German only | the canvas shows its language switch, marks a field without a German text in German, and nothing in English |
| Copy alias on a field without an alias | the canvas's "…" of a field without one holds Start a new page after this alone |

Two changes had lost their place to the formatter and were placed again. The second round ran the five, the two and
one more, the default's text as a text setting's placeholder: all eight were caught, each by the test meant for it.